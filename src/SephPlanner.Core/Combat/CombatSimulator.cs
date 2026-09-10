using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SephPlanner.Core.Combat
{
    public static class CombatSimulator
    {
        private sealed class ActionState
        {
            internal CombatAttackInstance Item = new CombatAttackInstance();
            internal int Capacity;
            internal int Charges;
            internal int Cost;
            internal double Cooldown;
            internal double Next;
            internal double TriggerReady;
            internal Queue<double> Recharge = new Queue<double>();
            internal CombatClock RecoveryClock = new CombatClock();
            internal CombatClock IntervalClock = new CombatClock();
            internal CombatClock TriggerClock = new CombatClock();
            internal CombatContribution Contribution = new CombatContribution();
        }

        public static CombatResult Evaluate(CombatLoadout loadout, CombatSnapshot snapshot, CombatScenario scenario,
            bool details = true, CancellationToken cancellation = default)
        {
            Validate(scenario);
            var timing = CombatTiming.Apply(snapshot, scenario);
            var result = new CombatResult
            {
                DurationSeconds = scenario.DurationSeconds,
                TargetCount = scenario.TargetCount,
                AdditionalTargetFraction = scenario.AdditionalTargetFraction,
                ComparisonWindowSeconds = Math.Min(scenario.ComparisonWindowSeconds, scenario.DurationSeconds),
                Unsupported = new List<string>(loadout.Unsupported),
            };
            var stats = loadout.Stats;
            var infinite = stats.Read("INFINITYMP") > 0;
            var maxMana = Math.Max(0, infinite ? snapshot.InfinityMana : stats.Read("@MAXMP") +
                CombatStatMath.Truncate((float)unchecked(stats.Read("@MAXMP") * stats.Read("FINALMP")) / 100f));
            var regeneration = Math.Max(0, stats.Read("MPREGEN") * 0.1 * CombatDamage.Factor(stats.Read("MPREGENMULTIPLE")));
            var recoveryDelay = stats.Read("MPRESONANCE") <= -100 ? double.PositiveInfinity : snapshot.ManaRecoveryDelay / (100 + stats.Read("MPRESONANCE"));
            var mana = new CombatMana(maxMana, maxMana * scenario.InitialManaFraction, regeneration, recoveryDelay);
            var states = loadout.Attacks.Select(item => State(item, stats, scenario)).ToList();
            var magic = states.Where(state => state.Item.Attack.Action == CombatActionKind.Magic && scenario.UseMagic &&
                    !scenario.DisabledMagic.Contains(state.Item.DefinitionId))
                .OrderBy(state => Priority(scenario, state.Item.DefinitionId)).ThenBy(state => state.Item.DefinitionId)
                .ThenBy(state => state.Item.InstanceId).ToList();
            var automatic = states.Where(state => state.Item.Attack.Action == CombatActionKind.Automatic && state.Item.Attack.Trigger == CombatTrigger.Interval).ToList();
            var triggered = states.Where(state => state.Item.Attack.Trigger == CombatTrigger.AttackHit).ToList();
            var dash = states.Where(state => state.Item.Attack.Trigger == CombatTrigger.Dash).ToList();
            var weapon = states.Where(state => state.Item.Attack.DamageKind == CombatDamageKind.Weapon)
                .GroupBy(state => state.Item.Attack.Action).ToDictionary(group => group.Key, group => group.ToList());
            var sequence = scenario.WeaponSequence.Where(weapon.ContainsKey).ToList();
            if (sequence.Count != scenario.WeaponSequence.Count)
                result.Unsupported.Add("선택한 공격 중 장착 무기에서 자료를 읽지 못한 동작이 있어 제외했습니다.");
            var weaponIndices = new Dictionary<CombatActionKind, int>();
            var sequenceIndex = 0;
            var actorReady = 0d;
            var magicReady = 0d;
            var magicClock = new CombatClock();
            var actorClock = new CombatClock();
            var time = 0d;
            var steps = 0;
            while (time < scenario.DurationSeconds)
            {
                if ((steps++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
                if (steps > 1_000_000) throw new InvalidOperationException("전투 사건 수가 계산 한도를 넘었습니다. 공격 주기 설정을 확인해 주세요.");
                foreach (var state in states)
                    while (state.Recharge.Count > 0 && state.Recharge.Peek() <= time)
                    {
                        state.Recharge.Dequeue();
                        state.Charges = Math.Min(state.Capacity, state.Charges + 1);
                    }
                foreach (var state in automatic)
                    if (state.Next <= time)
                    {
                        Hit(state, time, 0, true);
                        state.Next = state.IntervalClock.Advance(time, state.Cooldown);
                    }
                if (actorReady <= time)
                {
                    var cast = magicReady <= time ? magic.FirstOrDefault(state => state.Charges > 0 && (infinite || mana.AvailableAt(state.Cost) <= time)) : null;
                    if (cast != null)
                    {
                        UseCharge(cast, time);
                        if (!infinite) mana.Spend(cast.Cost, time);
                        Hit(cast, time, cast.Cost, true);
                        actorReady = actorClock.Advance(time, Duration(cast.Item.Attack, stats, timing));
                        magicReady = magicClock.Advance(time, Math.Max(0, snapshot.GlobalMagicCooldown));
                    }
                    else if (sequence.Count > 0)
                    {
                        var kind = sequence[sequenceIndex++ % sequence.Count];
                        var choices = weapon[kind];
                        var index = weaponIndices.GetValueOrDefault(kind);
                        var action = choices[index % choices.Count];
                        weaponIndices[kind] = index + 1;
                        var paid = action.Cost > 0 && (infinite || mana.AvailableAt(action.Cost) <= time);
                        if (paid)
                        {
                            if (!infinite) mana.Spend(action.Cost, time);
                        }
                        Hit(action, time, paid ? action.Cost : 0, true);
                        if (kind == CombatActionKind.Dash)
                            foreach (var effect in dash) Hit(effect, time, paid ? action.Cost : 0, false);
                        actorReady = actorClock.Advance(time, Duration(action.Item.Attack, stats, timing));
                    }
                    else actorReady = double.PositiveInfinity;
                }
                var next = Math.Min(actorReady > time ? actorReady : double.PositiveInfinity,
                    automatic.Count > 0 ? automatic.Min(state => state.Next) : double.PositiveInfinity);
                foreach (var state in states)
                    if (state.Recharge.Count > 0) next = Math.Min(next, state.Recharge.Peek());
                if (magic.Count > 0 && sequence.Count == 0)
                {
                    var ready = Math.Max(time, Math.Max(magicReady, double.IsInfinity(actorReady) ? time : actorReady));
                    foreach (var state in magic.Where(state => state.Charges > 0))
                    {
                        var affordable = infinite ? time : mana.AvailableAt(state.Cost);
                        next = Math.Min(next, Math.Max(ready, affordable));
                    }
                    if (next > time && !double.IsInfinity(next)) actorReady = Math.Min(actorReady, next);
                }
                if (next <= time) throw new InvalidOperationException("전투 시간이 진행되지 않습니다. 충전·공격 주기를 확인해 주세요.");
                time = next;
            }
            result.RemainingMana = mana.At(scenario.DurationSeconds);
            result.Contributions = states.Select(state => state.Contribution).Where(value => value.Uses > 0).ToList();
            result.TotalDamage = result.Contributions.Sum(value => value.Damage);
            if (!CombatDamage.Finite(result.TotalDamage)) throw new ArithmeticException("총 피해를 계산하지 못했습니다.");
            if (!details) return result;
            foreach (var source in stats.Export())
            {
                foreach (var pair in source.Stats) CombatLoadouts.Add(result.FinalStats, pair.Key, pair.Value);
                foreach (var pair in source.Amplification) CombatLoadouts.Add(result.FinalAmplification, pair.Key, pair.Value);
            }
            result.Unsupported.AddRange(states.SelectMany(state => state.Item.Attack.Unsupported));
            if (scenario.MeasuredActionSeconds.Count > 0)
                result.Unsupported.Add(CombatTiming.Matches(snapshot, scenario)
                    ? "사용자가 입력한 실측 평균 공격 간격으로 동작 시간을 보정했습니다. 실제 피해량을 검증한 결과는 아닙니다."
                    : "측정 당시와 무기가 달라 실측 간격 보정을 적용하지 않았습니다. 전투 탭의 기본 동작 시간을 사용합니다.");
            if (states.Any(state => state.Item.Attack.DurationSeconds <= 0 && state.Item.Attack.Action != CombatActionKind.Automatic))
                result.Unsupported.Add("공격·시전 시간은 설정한 비교용 추정값입니다. 이동·조준·투사체 비행과 실제 명중률은 재현하지 않습니다.");
            if (magic.Count > 0 || states.Any(state => state.Cost > 0))
                result.Unsupported.Add("마나는 소비 순서와 회복 지연을 반영한 연속 회복 추정입니다. 프레임별 소수 누적·마나 예약·회복 발동 효과는 미반영입니다.");
            result.Unsupported.AddRange(CombatCoverage.Missing(stats));
            result.Unsupported = result.Unsupported.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToList();
            return result;

            void Hit(ActionState state, double now, int cost, bool callbacks)
            {
                var item = state.Item;
                var attack = item.Attack;
                var support = CombatDamage.Factor(item.SupportPercent + (item.InstanceId != 0 ? stats.Read("CHARMDAMAGEBONUS") : 0));
                var damage = CombatDamage.Base(attack, item.Level, stats, cost, support);
                var hits = CombatDamage.At(attack.HitsByLevel, item.Level, 1) * attack.Hits;
                var targets = attack.MaxTargets == 0 ? scenario.TargetCount : Math.Min(scenario.TargetCount, attack.MaxTargets);
                var expectedTargets = 1 + (targets - 1) * scenario.AdditionalTargetFraction;
                var dealt = CombatDamage.ExpectedHit(attack, damage, stats, scenario, loadout.DirectAttackCritical) * hits * expectedTargets;
                state.Contribution.Damage += dealt;
                if (details)
                {
                    if (now < result.ComparisonWindowSeconds) result.OpeningDamage += dealt;
                    if (now >= scenario.DurationSeconds - result.ComparisonWindowSeconds) result.EndingDamage += dealt;
                }
                state.Contribution.Uses++;
                if (!callbacks || hits <= 0 || targets <= 0) return;
                foreach (var effect in triggered)
                {
                    if (effect.TriggerReady > now || effect.Charges <= 0) continue;
                    if (attack.DamageKind != CombatDamageKind.Weapon && attack.Action != CombatActionKind.Magic &&
                        !(attack.DamageKind == CombatDamageKind.IceRelic && stats.Read("FLAMESWORDCALLBACKFROST") > 0)) continue;
                    effect.TriggerReady = effect.TriggerClock.Advance(now, effect.Item.Attack.TriggerCooldownSeconds);
                    var count = 1 + stats.Read("FLAMESWORDADDITIONALATTACK") + (attack.DamageKind == CombatDamageKind.Weapon ?
                        stats.Read("FLAMESWORDADDITIONALATTACKFROMWEAPON") : attack.Action == CombatActionKind.Magic ? stats.Read("FLAMESWORDADDITIONALATTACKFROMMAGIC") : 0);
                    for (var i = 0; i < count && effect.Charges > 0; i++)
                    {
                        UseCharge(effect, now);
                        Hit(effect, now, 0, false);
                    }
                }
            }
        }

        private static ActionState State(CombatAttackInstance item, CombatStatState stats, CombatScenario scenario)
        {
            var attack = item.Attack;
            var cooldown = attack.Action == CombatActionKind.Automatic && attack.Trigger == CombatTrigger.Interval ?
                CombatDamage.At(attack.IntervalByLevel, item.Level) : attack.CooldownSeconds;
            if (attack.Action == CombatActionKind.Magic)
                cooldown = RecoverAfter(cooldown, stats.Read("COOLDOWNRECOVERYSPEED") + item.CooldownBonus);
            if (attack.ChargingCharm) cooldown = RecoverAfter(cooldown, stats.Read("CHARGINGCHARMBONUS"));
            if (double.IsNaN(cooldown) || cooldown < 0 ||
                (attack.Action == CombatActionKind.Automatic && attack.Trigger == CombatTrigger.Interval && cooldown <= 0))
                throw new InvalidOperationException("공격 주기 자료가 올바르지 않습니다: " + attack.Name);
            var capacity = Math.Max(0, attack.Charges + (attack.DamageKind == CombatDamageKind.FlameSword ? stats.Read("FLAMESWORDMAX") : 0));
            var state = new ActionState
            {
                Item = item,
                Capacity = capacity,
                Charges = (int)Math.Floor(capacity * scenario.InitialChargeFraction),
                Cooldown = cooldown,
                Cost = Math.Max(0, CombatDamage.At(attack.ManaCost, item.Level)),
                Next = scenario.InitialChargeFraction == 1 ? 0 : cooldown * (1 - scenario.InitialChargeFraction),
                Contribution = new CombatContribution { Id = item.InstanceId + ":" + attack.Id, Name = attack.Name },
            };
            if (attack.Action == CombatActionKind.Magic)
            {
                var bonus = item.CostBonus - stats.Read("MAGICCOSTREDUCE");
                float cost = state.Cost;
                // Charm_Magic.GetCost와 같은 float 정밀도·연산 순서로 반 정수 경계를 계산한다.
                state.Cost = stats.Read("NOMAGICCOST") > 0 || bonus <= -100 ? 0 :
                    Math.Max(0, (int)Math.Round(cost + cost * ((float)bonus / 100f)));
            }
            for (var i = 0; attack.Recharges && i < capacity - state.Charges; i++)
                state.Recharge.Enqueue(cooldown * (attack.ParallelRecharge ? 1 : i + 1));
            return state;
        }

        private static void UseCharge(ActionState state, double time)
        {
            state.Charges--;
            if (!state.Item.Attack.Recharges) return;
            if (state.Cooldown == 0) { state.Charges++; return; }
            var start = state.Item.Attack.ParallelRecharge || state.Recharge.Count == 0 ? time : state.Recharge.Last();
            state.Recharge.Enqueue(state.RecoveryClock.Advance(start, state.Cooldown));
        }

        private static int Priority(CombatScenario scenario, int id)
        {
            var index = scenario.MagicPriority.IndexOf(id);
            return index < 0 ? int.MaxValue : index;
        }

        private static double RecoverAfter(double seconds, double bonus)
        {
            if (bonus < -100) throw new InvalidOperationException("회복 속도가 음수인 동작은 아직 재현하지 못합니다.");
            return bonus == -100 ? double.PositiveInfinity : seconds / CombatDamage.Factor(bonus);
        }

        internal static double Duration(CombatAttack attack, CombatStatState stats, CombatScenario scenario)
        {
            var duration = attack.DurationSeconds > 0 ? attack.DurationSeconds : attack.Action switch
            {
                CombatActionKind.Dash => scenario.DashSeconds,
                CombatActionKind.Special => scenario.SpecialSeconds,
                CombatActionKind.Magic => scenario.CastingSeconds,
                _ => scenario.BasicSeconds,
            };
            var speed = attack.Action == CombatActionKind.Magic ? 0 :
                attack.Action == CombatActionKind.Special ? stats.Read("SPECIALATTACKSPEED") + (attack.SpecialUsesAttackSpeed ? stats.Read("ATTACKSPEED") : 0) :
                stats.Read("ATTACKSPEED") * (1 + attack.AttackSpeedAmplification);
            var factor = CombatDamage.Factor(speed);
            if (attack.Action != CombatActionKind.Magic && attack.Action != CombatActionKind.Special && stats.Read("FIXEDATTACKSPEED") > 0)
                factor = stats.Read("FIXEDATTACKSPEED") / 100d;
            if (factor <= 0 || !CombatDamage.Finite(factor)) throw new InvalidOperationException("공격 속도가 0 이하입니다.");
            return duration / factor;
        }

        public static void Validate(CombatScenario scenario)
        {
            if (scenario != null && !Enum.IsDefined(typeof(SephPlanner.Core.Model.HorizontalSide), scenario.EternalSide))
                throw new ArgumentException("영원의 식 방향은 자동·왼쪽·오른쪽 중에서 선택해 주세요.");
            if (scenario != null && !Enum.IsDefined(typeof(SephPlanner.Core.Model.HorizontalSide), scenario.ScalesSide))
                throw new ArgumentException("대립의 천칭 방향은 자동·왼쪽·오른쪽 중에서 선택해 주세요.");
            if (scenario == null || scenario.TargetStats == null || scenario.WeaponSequence == null || scenario.MagicPriority == null ||
                scenario.DisabledMagic == null || scenario.MeasuredActionSeconds == null || scenario.MeasuredWeaponKey == null ||
                scenario.WeaponSequence.Any(action => action != CombatActionKind.Basic && action != CombatActionKind.Dash && action != CombatActionKind.Special))
                throw new ArgumentException("전투 조건 목록이 없거나 무기 공격 순서가 올바르지 않습니다.");
            if (!CombatDamage.Finite(scenario.DurationSeconds) || scenario.DurationSeconds <= 0 || scenario.DurationSeconds > 600 ||
                scenario.TargetCount < 1 || scenario.TargetCount > 100 || !Fraction(scenario.InitialManaFraction) || !Fraction(scenario.InitialChargeFraction) ||
                !Fraction(scenario.AdditionalTargetFraction) || !CombatDamage.Finite(scenario.ComparisonWindowSeconds) || scenario.ComparisonWindowSeconds <= 0 || scenario.ComparisonWindowSeconds > 600 ||
                new[] { scenario.BasicSeconds, scenario.DashSeconds, scenario.SpecialSeconds, scenario.CastingSeconds }
                    .Concat(scenario.MeasuredActionSeconds.Values).Any(value => !CombatDamage.Finite(value) || value < 0.01 || value > 60) ||
                scenario.MeasuredActionSeconds.Keys.Any(key => key != nameof(CombatActionKind.Basic) && key != nameof(CombatActionKind.Dash) && key != nameof(CombatActionKind.Special) && key != nameof(CombatActionKind.Magic)))
                throw new ArgumentException("전투 조건을 확인해 주세요. 비교·구간 시간 0초 초과~600초, 대상 1~100명, 시작 자원·추가 대상 적중 0~100%, 동작 시간 0.01~60초를 지원합니다.");
        }

        private static bool Fraction(double value) => CombatDamage.Finite(value) && value >= 0 && value <= 1;
    }
}

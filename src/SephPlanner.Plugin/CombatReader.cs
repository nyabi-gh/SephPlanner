using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SephPlanner.Core.Combat;

namespace SephPlanner.Plugin
{
    internal static class CombatReader
    {
        internal static CombatSnapshot Read(PlayerAvatar avatar, WeaponControllerSimple controller)
        {
            var result = new CombatSnapshot { InfinityMana = KeywordDatabase.GetConstValue("infinityMPMax") };
            foreach (var key in avatar.customStats.Keys.Concat(avatar.calculatedBonusStats.Keys).Concat(avatar.customStatsAmp.Keys).Distinct())
            {
                result.ObservedStats[key] = avatar.GetCustomBaseStatUnsafe(key);
                result.ObservedAmplification[key] = avatar.GetCustomStatAmp(key);
            }
            result.ObservedStats["@MAXMP"] = avatar.maxMp;
            result.ObservedStats["@MAXHP"] = (int)avatar.maxHp;
            result.ManaRecoveryDelay = ReadTimer(avatar, "mpPeaceRegenTimer");
            var skill = avatar.GetComponent<SkillController>();
            if (skill != null) result.GlobalMagicCooldown = ReadTimer(skill, "globalCooldownTimer");
            var weapon = controller != null ? controller.currentWeapon : null;
            if (weapon == null)
            {
                result.Unsupported.Add("장착 무기의 공격 자료가 없습니다.");
                return result;
            }
            ReadAttacks(result, weapon, weapon.basicComboAttacks, CombatActionKind.Basic);
            ReadAttacks(result, weapon, weapon.dashAttacks, CombatActionKind.Dash);
            ReadAttacks(result, weapon, weapon.specialAttacks, CombatActionKind.Special);
            result.Unsupported.Add("무기별 동작 분기·충전·마나 소모·적중 후 발동은 일부 미반영입니다. 수집한 기본 공격 프리팹을 선택한 순서로 반복합니다.");
            result.Unsupported.Add("출처를 재현하지 못한 능력치는 현재 관측값에 고정됩니다. 해당 효과의 레벨·활성·자리 변화는 정확히 예측하지 못합니다.");
            return result;
        }

        private static void ReadAttacks(CombatSnapshot snapshot, WeaponSimple weapon, NewWeaponFireData[] attacks, CombatActionKind kind)
        {
            if (attacks == null)
            {
                snapshot.Unsupported.Add("무기 공격 프리팹 목록을 읽지 못했습니다: " + kind);
                return;
            }
            for (var index = 0; index < attacks.Length; index++)
            {
                var data = attacks[index];
                if (data == null) continue;
                var element = data.damageElementalType.ToString().ToUpperInvariant();
                var attack = new CombatAttack
                {
                    Id = "weapon:" + weapon.entityId + ":" + kind + ":" + index,
                    Name = (kind == CombatActionKind.Basic ? "일반" : kind == CombatActionKind.Dash ? "대시" : "특수") + " 공격 " + (index + 1),
                    Action = kind,
                    DamageKind = CombatDamageKind.Weapon,
                    Element = element,
                    ElementFromRelatedStat = data.useElementalTypeFromRelatedStatFormula,
                    Stat = data.relatedStatFormula ?? "",
                    Multiplier = data.damageMultiplier,
                    AttackSpeedAmplification = weapon.attackSpeedAmplify,
                    SpecialUsesAttackSpeed = weapon.specialAttackIsRelatedToAttackSpeed,
                    MaxTargets = data is NewWeaponFireData_MeleeAttack ? 0 : 1,
                };
                if (data is NewWeaponFireData_BulletBurst burst)
                {
                    attack.Hits = burst.burstRound;
                    attack.Unsupported.Add("무기 연사탄은 모두 적중하는 것으로 추정하며 발사 간격은 미반영입니다.");
                }
                else if (data is NewWeaponFireData_BulletSpread spread && spread.betweenAngleDegrees > 0)
                {
                    attack.Hits = Math.Ceiling(spread.spreadAngle / spread.betweenAngleDegrees);
                    attack.Unsupported.Add("무기 분산탄은 모두 적중하는 비교용 추정입니다.");
                }
                else if (!(data is NewWeaponFireData_MeleeAttack) && !(data is NewWeaponFireData_Bullet))
                    attack.Unsupported.Add("특수 공격 프리팹의 적중 횟수 미지원: " + data.GetType().Name);
                snapshot.WeaponAttacks.Add(attack);
            }
        }

        private static double ReadTimer(object instance, string name)
        {
            for (var type = instance.GetType(); type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null) return ((global::Timer)field.GetValue(instance)).time;
            }
            throw new MissingFieldException(instance.GetType().FullName, name);
        }
    }
}

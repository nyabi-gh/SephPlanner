using System;
using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;

namespace SephPlanner.Plugin
{
    internal static class CombatCatalog
    {
        internal static void ReadCharm(Charm_Basic charm, CharmDefinition definition)
        {
            var effect = definition.Combat;
            effect.Collected = charm != null;
            if (charm == null)
            {
                effect.Unsupported.Add("아티팩트 프리팹을 읽지 못했습니다.");
                return;
            }
            if (charm is Charm_StatusInstance status)
                foreach (var group in status.stats)
                    AddStat(effect, group.statusID, group.valuesByLevel, 0);
            foreach (var context in definition.ContextStats)
            {
                var grant = Stat(context.StatusId, Array.Empty<int>());
                if (grant == null) effect.Unsupported.Add("수량·행 능력치 " + context.StatusId);
                else
                {
                    context.CombatKey = grant.Key;
                    context.CombatAmplification = grant.Amplification;
                }
            }
            var supported = charm.GetType() == typeof(Charm_StatusInstance) || definition.ContextStats.Count > 0 ||
                definition.NeighborLevelBonus.Count > 0 || definition.DependencyBonusByLevel.Count > 0 ||
                definition.MagicSupport != null || charm is Charm_WhitePaper;
            if (charm is Charm_DashAttackDamage dash)
            {
                effect.Stats.Add(new CombatStatGrant { Key = "DASHATTACKDAMAGEBONUS", Values = dash.bonusByLevel.ToList() });
                supported = true;
            }
            if (charm is Charm_IncreaseMPRegen regen)
            {
                effect.Stats.Add(new CombatStatGrant { Key = "MPREGEN", Values = regen.addMPRegenByLevel.ToList() });
                supported = true;
            }
            if (charm is Charm_FireIceWeapon)
            {
                effect.FireIcePosition = true;
                supported = true;
            }
            if (charm is Charm_DashDamage collision)
            {
                effect.Attacks.Add(new CombatAttack
                {
                    Id = "dash-collision",
                    Name = Name(definition),
                    Action = CombatActionKind.Automatic,
                    DamageKind = CombatDamageKind.Physical,
                    Trigger = CombatTrigger.Dash,
                    StatPercent = collision.physicalDashDamage.Select(value => (double)value).ToList(),
                    MaxTargets = 0,
                    RequiresPaidDash = true,
                    Unsupported = new List<string> { "충전을 소비한 대시의 충돌 피해 두 배와 대시 충전 회복은 미반영입니다." },
                });
                supported = true;
            }
            if (charm is Charm_Magic magic && magic.ContainedMagic != null)
            {
                var skill = magic.ContainedMagic;
                var bolt = skill.magicPrefab != null ? skill.magicPrefab.GetComponent<ActiveSkill_Bolt>() : null;
                if (bolt != null)
                {
                    effect.Attacks.Add(new CombatAttack
                    {
                        Id = "magic",
                        Name = Name(definition),
                        Action = CombatActionKind.Magic,
                        DamageKind = CombatDamageKind.Bolt,
                        Stat = bolt.relatedDamage.ToUpperInvariant(),
                        Element = bolt.elementalType.ToString().ToUpperInvariant(),
                        BaseDamage = bolt.defaultDamageByLevel.Select(value => (double)value).ToList(),
                        StatPercent = bolt.damagePercentByLevel.Select(value => (double)value).ToList(),
                        ManaCost = skill.mpCostsByLevel.ToList(),
                        CooldownSeconds = skill.cooldownTime,
                        Charges = skill.ammo,
                        Hits = Math.Ceiling(bolt.fireCount),
                        BasicDamageBonus = bolt.relatedToBasicAttackDamageBonus,
                        UsesMagicCritical = true,
                        Unsupported = new List<string> { "볼트의 비행·관통·다중 발사와 적중 후 부가 효과는 일부 미반영입니다." },
                    });
                    supported = true;
                }
            }
            if (charm is Charm_IceSpear spear)
            {
                effect.Attacks.Add(new CombatAttack
                {
                    Id = "ice-spear",
                    Name = Name(definition),
                    Action = CombatActionKind.Automatic,
                    DamageKind = CombatDamageKind.IceRelic,
                    Stat = "ICEDAMAGE",
                    Element = "ICE",
                    BaseDamage = new List<double> { spear.defaultDamage },
                    StatPercent = spear.damagePercentByLevel.Select(value => (double)value).ToList(),
                    HitsByLevel = spear.fireCountByLevel.ToList(),
                    ChargingCharm = true,
                    IntervalByLevel = new List<double> { spear.chargingCharm.defaultChargeTimer },
                    Unsupported = new List<string> { "얼음 무구의 충전 추가 발동·마나 증폭·투사체 적중 조건은 일부 미반영입니다." },
                });
                supported = true;
            }
            if (!supported) effect.Unsupported.Add("전투 동작 미지원: " + definition.Behavior);
        }

        internal static void ReadCombo(ComboEffectBase combo, ComboDefinition definition)
        {
            var effect = definition.Combat;
            effect.Collected = combo != null;
            if (combo == null) return;
            foreach (var grant in combo.addStatByCombo)
                foreach (var entry in grant.status)
                {
                    var parts = entry.Split('/');
                    if (parts.Length < 2 || !int.TryParse(parts[1], out var value))
                        effect.Unsupported.Add("콤보 능력치 해석 누락: " + entry);
                    else AddStat(effect, parts[0], new[] { value }, grant.comboCount);
                }
            if (combo is ComboEffect_FlameSword sword)
                effect.Attacks.Add(new CombatAttack
                {
                    Id = "flame-sword",
                    Name = "화염검",
                    Action = CombatActionKind.Automatic,
                    DamageKind = CombatDamageKind.FlameSword,
                    Trigger = CombatTrigger.AttackHit,
                    Threshold = sword.flameSwordComboCount,
                    StatPercent = new List<double> { sword.damagePercent },
                    Charges = sword.defaultMaxSword,
                    Recharges = false,
                    TriggerCooldownSeconds = sword.minCooldownTime,
                    Unsupported = new List<string> { "화염검 회수·행운·추가 치명타·관통과 연속 적중 중 추가 발동은 미반영입니다. 시작 검 수와 공격별 발동을 계산합니다." },
                });
            else if (combo.GetType() != typeof(ComboEffectBase))
                effect.Unsupported.Add("특수 콤보의 발동 효과는 일부 미반영입니다: " + definition.Id);
        }

        internal static CombatStatGrant Stat(string id, IEnumerable<int> values)
        {
            var entity = StatusDatabase.GetStatusEntity(id);
            if (entity == null) return null;
            var parts = entity.className.Split('/');
            var key = parts.Length > 1 && (parts[0] == "StatusInstance_Custom" || parts[0] == "StatusInstance_CustomAmp")
                ? parts[1].ToUpperInvariant() : StatKey(parts[0]);
            return key.Length == 0 ? null : new CombatStatGrant
            {
                Key = key,
                Amplification = parts[0] == "StatusInstance_CustomAmp",
                Values = values.ToList(),
            };
        }

        private static void AddStat(CharmCombatEffect effect, string id, IEnumerable<int> values, int threshold)
        {
            var grant = Stat(id, values);
            if (grant == null) effect.Unsupported.Add("능력치 적용 방식 미지원: " + id);
            else
            {
                grant.Threshold = threshold;
                effect.Stats.Add(grant);
            }
        }

        private static string Name(CharmDefinition definition) =>
            definition.Names.TryGetValue("current", out var name) ? name : definition.Id;

        private static string StatKey(string type) => type switch
        {
            "StatusInstance_AP" => "AP",
            "StatusInstance_AttackSpeed" => "ATTACKSPEED",
            "StatusInstance_BasicAttackDamage" => "BASICATTACKDAMAGEBONUS",
            "StatusInstance_BuffDuration" => "BUFFDURATION",
            "StatusInstance_ChargingCharmBonus" => "CHARGINGCHARMBONUS",
            "StatusInstance_CooldownRecoverySpeed" => "COOLDOWNRECOVERYSPEED",
            "StatusInstance_Critical" => "CRITICAL",
            "StatusInstance_CriticalDamageRate" => "CRITICALDAMAGEBONUS",
            "StatusInstance_DashAttackDamage" => "DASHATTACKDAMAGEBONUS",
            "StatusInstance_DashCount" => "DASHCOUNT",
            "StatusInstance_DashRecoverySpeed" => "DASHRECOVERY",
            "StatusInstance_DashSpeed" => "DASHSPEEDBONUSPERCENT",
            "StatusInstance_DebuffDuration" => "DEBUFFDURATION",
            "StatusInstance_Defense" => "DAMAGEREDUCTION",
            "StatusInstance_EXPDrop" => "EXPDROP",
            "StatusInstance_Evasion" => "EVASION",
            "StatusInstance_FinalAP" => "FINALAP",
            "StatusInstance_FinalDamage" => "ALLDAMAGEBONUS",
            "StatusInstance_FinalMP" => "FINALMP",
            "StatusInstance_FinalWeaponDamage" => "FINALWEAPONDAMAGE",
            "StatusInstance_FireDamage" => "FIREDAMAGE",
            "StatusInstance_HPPotionBonus" => "HPPOTIONBONUS",
            "StatusInstance_HPRegen" => "HPREGEN",
            "StatusInstance_HPSteal" => "HPSTEAL",
            "StatusInstance_IceDamage" => "ICEDAMAGE",
            "StatusInstance_LeafDrop" => "MONEYDROP",
            "StatusInstance_LightningDamage" => "LIGHTNINGDAMAGE",
            "StatusInstance_Luck" => "LUCK",
            "StatusInstance_MPPotionBonus" => "MPPOTIONBONUS",
            "StatusInstance_MPRegen" => "MPREGEN",
            "StatusInstance_MPSteal" => "MPSTEAL",
            "StatusInstance_MagicCritical" => "MAGICCRITICAL",
            "StatusInstance_MagicCriticalDamageRate" => "MAGICCRITICALDAMAGEBONUS",
            "StatusInstance_MinDarkCloud" => "MINDARKCLOUD",
            "StatusInstance_Negotiation" => "NEGOTIATION",
            "StatusInstance_PhysicalDamage" => "PHYSICALDAMAGE",
            "StatusInstance_SpecialAttackDamage" => "SPECIALATTACKDAMAGEBONUS",
            "StatusInstance_SweepCostReduction" => "SWEEPCOSTREDUCTION",
            "StatusInstance_Thorns" => "THORNS",
            "StatusInstance_TrueDamage" => "TRUEDAMAGE",
            "StatusInstance_MaxMP" => "@MAXMP",
            "StatusInstance_MaxHP" => "@MAXHP",
            "StatusInstance_MaxHPNoRatio" => "@MAXHP",
            "StatusInstance_FinalHP" => "@FINALHP",
            _ => "",
        };
    }
}

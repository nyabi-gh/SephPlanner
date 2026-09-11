using System;
using System.Collections.Generic;
using System.Globalization;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임 리소스에서 아이템 정의를 읽어 Core 모델로 옮긴다.
    /// ItemDatabase 내부 상태에 기대지 않고 Resources 를 직접 훑는다.
    /// </summary>
    internal static class ItemCatalog
    {
        private static ItemEntity[] _items;
        private static ItemCategoryEntity[] _categories;

        /// <summary>
        /// 리소스 목록. <b>한 번만 훑고 들고 있는다.</b>
        ///
        /// 카탈로그를 짓는 일은 게임 부팅 직후에는 실패한다 - 지역화 표가 아직 없어서 이름을
        /// 읽는 순간 터진다. 그런데 부르는 쪽은 준비될 때까지 되풀이해 물어보므로(폴링마다,
        /// 초당 네 번), 실패할 때마다 리소스를 통째로 다시 훑으면 그 몇 초 동안 메인 스레드가
        /// 같은 일을 수십 번 한다. 시작 화면에서 인게임 진입이 느리다는 제보가 여기와 맞물린다.
        ///
        /// 목록 자체는 한 번 읽으면 바뀌지 않으므로, 준비를 기다리는 동안 들고 있다가
        /// <see cref="Release"/> 로 놓아준다.
        /// </summary>
        private static ItemEntity[] Items() =>
            _items ?? (_items = Resources.LoadAll<ItemEntity>("Item"));

        private static ItemCategoryEntity[] Categories() =>
            _categories ?? (_categories = Resources.LoadAll<ItemCategoryEntity>("ItemCategory"));

        public static MysticRule LoadMysticRule()
        {
            foreach (var category in Categories())
            {
                if (category.id != "MYSTIC" || !category.isEnabled || category.comboEffectPrefab == null) continue;
                var effect = category.comboEffectPrefab.GetComponent<ComboEffect_Mystic>();
                if (effect == null) continue;
                var entity = ItemDatabase.FindItemById(effect.stoneTabletEntityID);
                var tablet = entity != null && entity.resourcePrefab != null
                    ? entity.resourcePrefab.GetComponent<StoneTablet>() : null;
                if (tablet == null || tablet.isCustomTablet) return null;
                return new MysticRule
                {
                    FirstThreshold = effect.first,
                    FirstCount = effect.firstEngravingCount,
                    SecondThreshold = effect.second,
                    SecondCount = effect.secondEngravingCount,
                    Query = tablet.query ?? "",
                    ConditionQuery = tablet.conditionQuery ?? "",
                };
            }
            return null;
        }

        /// <summary>
        /// 다 짓고 나면 놓아준다. 계속 들고 있으면 게임이 쓰지 않는 에셋을 정리하지 못한다.
        /// </summary>
        public static void Release()
        {
            _items = null;
            _categories = null;
        }
        public static List<TabletDefinition> LoadTablets()
        {
            var result = new List<TabletDefinition>();
            foreach (var entity in Items())
            {
                if (entity.type != EItemType.StoneTablet) continue;
                if (entity.activeType == EItemActiveType.Disabled) continue;

                var tablet = entity.resourcePrefab != null
                    ? entity.resourcePrefab.GetComponent<StoneTablet>()
                    : null;
                if (tablet == null) continue;

                result.Add(new TabletDefinition
                {
                    Id = IdFromKey(entity.aName?.key, "Item_StoneTablet_", entity.id),
                    EntityId = entity.id,
                    Rarity = (Rarity)(int)entity.rarity,
                    IsRotatable = tablet.isRotatable,
                    IsCustom = tablet.isCustomTablet,
                    Names = DisplayName(entity),
                    Query = tablet.query ?? "",
                    ConditionQuery = tablet.conditionQuery ?? "",
                });
            }
            return result;
        }

        public static List<CharmDefinition> LoadCharms()
        {
            var result = new List<CharmDefinition>();
            foreach (var entity in Items())
            {
                if (entity.type != EItemType.Charm) continue;
                if (entity.activeType == EItemActiveType.Disabled) continue;

                var charm = entity.resourcePrefab != null
                    ? entity.resourcePrefab.GetComponent<Charm_Basic>()
                    : null;

                var definition = new CharmDefinition
                {
                    Id = IdFromKey(entity.aName?.key, "Item_", entity.id),
                    EntityId = entity.id,
                    Rarity = (Rarity)(int)entity.rarity,
                    Cost = entity.cost,
                    SapphirePrice = entity.sapphirePrice,
                    MaxLevel = charm != null ? charm.maxLevel : 5,
                    CriteriaType = charm != null && charm.criteria != null ? charm.criteria.GetType().Name : "",
                    IsMagic = charm is Charm_Magic,
                    IsWeaponRelated = charm != null && charm.isWeaponRelatedCharm,
                    RelatedWeapon = charm != null && charm.isWeaponRelatedCharm
                        ? charm.relatedWeapon.ToString()
                        : "",
                    Behavior = charm != null ? charm.GetType().Name : "",
                    HasNoActivationEffect = charm != null && charm.GetType() == typeof(Charm_StatusInstance) &&
                        ((Charm_StatusInstance)charm).stats != null && ((Charm_StatusInstance)charm).stats.Length == 0 &&
                        !charm.isUniqueEffect && !charm.flameGround && !charm.darkCloud,
                    CannotDiscard = entity.cannotThrow,
                    EffectLines = EffectLines(charm),
                    NeighborLevelBonus = NeighborLevelBonus(charm),
                    IsAttackable = charm is IAttackableCharm &&
                                   (!(charm is Charm_Magic attackMagic) || attackMagic.IsAttackableCharm()),
                    IsCompanion = charm is ICompanionCharm,
                    GrowthQuestGoal = charm is Charm_GrowthStatusInstance growth && growth.hasGrowthQuest
                        ? growth.growthQuestGoal : 0,
                    GrowthRewardEntityId = charm is Charm_GrowthStatusInstance reward && reward.reward != null
                        ? reward.reward.id : 0,
                    NeighborEnhanceCategory = NeighborEnhanceCategory(charm),
                    LineCategories = LineCategories(charm),
                    Categories = entity.categories ?? new List<string>(),
                    Names = DisplayName(entity),
                };
                if (charm is Charm_RightSpellCooldownHelper helper)
                    definition.MagicSupport = new DirectedMagicSupport
                    {
                        OffsetX = 1,
                        AmountByLevel = Doubles(helper.cooldownRecoveryByLevel),
                    };
                if (charm is Charm_ReduceMPCost reducer)
                    definition.MagicSupport = new DirectedMagicSupport
                    {
                        Effect = MagicSupportEffect.ManaCostReduction,
                        OffsetX = -1,
                        AmountByLevel = Doubles(reducer.reducePercentByLevel),
                    };
                if (charm is Charm_Magic magic && magic.ContainedMagic != null)
                {
                    definition.MagicCostByLevel = Doubles(magic.ContainedMagic.mpCostsByLevel);
                    definition.UsesMagicCritical = magic.ContainedMagic.magicPrefab != null &&
                        magic.ContainedMagic.magicPrefab.GetComponent<ActiveSkill>() is ActiveSkill_Bolt;
                }
                Dependency(charm, definition);
                ContextStats(charm, definition);
                if (charm is Charm_WhitePaper paper) definition.PaperMatch = paper.match;
                result.Add(definition);
            }
            return result;
        }

        /// <summary>
        /// 일반 능력치·구형 세트 효과의 단계와 게임 공통 콤보 데이터의 특수 단계를 합친다.
        /// </summary>
        public static List<ComboDefinition> LoadCombos()
        {
            var result = new List<ComboDefinition>();
            foreach (var category in Categories())
            {
                if (!category.isEnabled) continue;

                var thresholds = new SortedSet<int>();
                var combo = category.comboEffectPrefab != null
                    ? category.comboEffectPrefab.GetComponent<ComboEffectBase>()
                    : null;
                if (combo != null)
                {
                    foreach (var stat in combo.addStatByCombo)
                        if (stat.comboCount > 0) thresholds.Add(stat.comboCount);
                }
                foreach (var target in category.setStatus)
                    if (target.itemCount > 0) thresholds.Add(target.itemCount);

                var definition = ComboCatalogBuilder.Build(category.id, thresholds, () => EffectLines(combo));
                if (definition.Thresholds.Count == 0) continue;

                var names = new Dictionary<string, string>();
                var text = category.categoryName?.ToString();
                if (!string.IsNullOrEmpty(text)) names["current"] = text;

                definition.Names = names;
                result.Add(definition);
            }
            return result;
        }

        /// <summary>
        /// 콤보 가중치를 재기 위한 원자료. 아티팩트가 레벨마다 올려 주는 능력치와, 콤보가 임계값에서
        /// 주는 능력치를 게임에서 그대로 떠 온다. 두 쪽이 같은 <c>StatusDatabase</c> 체계를 쓰므로
        /// 능력치별로 "레벨 하나당 얼마"를 구하면 콤보를 레벨 단위로 옮길 수 있다.
        /// 해석은 게임 밖(<c>ComboWorthMeasure</c>)에서 한다.
        /// </summary>
        public static StatMeasurement LoadStatMeasurement()
        {
            var measurement = new StatMeasurement();

            foreach (var entity in Items())
            {
                if (entity.type != EItemType.Charm) continue;
                if (entity.activeType == EItemActiveType.Disabled) continue;
                if (entity.resourcePrefab == null) continue;

                var charm = entity.resourcePrefab.GetComponent<Charm_StatusInstance>();
                if (charm == null || charm.stats == null) continue;

                foreach (var group in charm.stats)
                {
                    if (group == null || group.valuesByLevel == null) continue;

                    var table = new CharmStatTable { EntityId = entity.id, StatusId = group.statusID };
                    foreach (var value in group.valuesByLevel) table.ValuesByLevel.Add(value);
                    measurement.CharmStats.Add(table);
                }
            }

            foreach (var category in Categories())
            {
                if (!category.isEnabled || category.comboEffectPrefab == null) continue;

                var combo = category.comboEffectPrefab.GetComponent<ComboEffectBase>();
                if (combo == null) continue;

                foreach (var stat in combo.addStatByCombo)
                {
                    if (stat == null || stat.status == null) continue;

                    foreach (var entry in stat.status)
                    {
                        // 게임의 CreateStatusEntity 와 같은 분해다. 값이 없는 능력치는 잴 수 없다.
                        var parts = (entry ?? "").Split('/');
                        if (parts.Length < 2 || !int.TryParse(parts[1], out var value)) continue;

                        measurement.ComboStats.Add(new ComboStatGrant
                        {
                            CategoryId = category.id,
                            Threshold = stat.comboCount,
                            StatusId = parts[0],
                            Value = value,
                        });
                    }
                }
            }
            return measurement;
        }

        /// <summary>
        /// 조화의 수정 계열의 레벨별 배수. 이웃 여덟 칸의 유효 레벨 합에 곱해지는 값이라, 솔버가
        /// 이 아티팩트의 자리 가치를 계산하려면 이 표가 있어야 한다.
        /// </summary>
        /// <summary>
        /// 북향의 침(<c>Charm_UpCharmDamage</c>)이 대상을 찾는 오프셋과, 대상에게 주는 몫.
        /// 대상은 사람이 고르는 것이 아니라 자리가 정한다 - 침의 <c>xOffset</c>/<c>yOffset</c>이
        /// 가리키는 칸에 있는 아티팩트다. 그래서 솔버가 알아서 좋은 자리를 찾을 수 있고,
        /// 값어치의 크기도 두 표의 비율로만 쓰므로 단위를 옮길 필요가 없다.
        /// </summary>
        private static void Dependency(Charm_Basic charm, CharmDefinition definition)
        {
            if (!(charm is Charm_UpCharmDamage needle)) return;

            definition.DependencyOffsetX = needle.xOffset;
            definition.DependencyOffsetY = needle.yOffset;
            definition.HasDependencyCondition = needle.hasDependencyCondition;
            definition.DependencyMaxRarity = (Rarity)(int)needle.maxRarity;
            definition.DependencyBonusByLevel = Doubles(needle.damageBonusByLevel);
            definition.DependencyExtraByLevel = Doubles(needle.dependencyDamageBonusByLevel);

            // 값이 하나도 없으면 솔버가 침으로 알아보지 못한다. 게임 기본값이 다섯 칸이라
            // 실제로는 오지 않는 길이지만, 오면 조용히 평범한 아티팩트가 되는 편이 낫다.
            if (definition.DependencyBonusByLevel.Count == 0)
                definition.DependencyBonusByLevel.Add(1);
        }

        /// <summary>
        /// 이웃 여덟 칸에서 강화할 아티팩트의 카테고리. 거대한 망원경이 <c>"PLANET"</c>을 찾는
        /// 것이 유일한 예이고, 그 글자가 <c>Charm_PlanetModule.SearchPlanet</c> 안에 박혀 있어
        /// 필드로 읽어 올 수 없다. 클래스를 보고 여기서 채운다.
        /// </summary>
        private static string NeighborEnhanceCategory(Charm_Basic charm) =>
            charm is Charm_PlanetModule ? "PLANET" : "";

        /// <summary>
        /// 놓인 행이 정하는 카테고리(<c>Charm_3Elemental_ByRow.lineCategory</c>). 캘세더니 열쇠가
        /// 어느 줄에 서느냐로 어떤 콤보를 미느냐가 갈린다.
        /// </summary>
        private static List<string> LineCategories(Charm_Basic charm)
        {
            var result = new List<string>();
            if (charm is Charm_3Elemental_ByRow byRow && byRow.lineCategory != null)
            {
                foreach (var category in byRow.lineCategory)
                {
                    result.Add(category ?? "");
                }
            }
            return result;
        }

        private static void ContextStats(Charm_Basic charm, CharmDefinition definition)
        {
            if (charm is Charm_WoodenBox belt)
                foreach (var stat in new[] { "FIRE_DAMAGE", "ICE_DAMAGE", "LIGHTNING_DAMAGE" })
                    definition.ContextStats.Add(new ContextStatBonus
                    {
                        Source = StatCountSource.QuickSlotCharms,
                        SlotCount = 6, // Charm_WoodenBox.CheckQuickSlot의 인벤토리 인덱스 범위.
                        StatusId = stat,
                        AmountByLevel = Doubles(belt.apPerQuickSlotCharmByLevel),
                    });
            if (charm is Charm_CriticalChanceIncreaseWithTablets scale)
                definition.ContextStats.Add(new ContextStatBonus
                {
                    Source = StatCountSource.StoneTablets,
                    StatusId = "CRITICAL",
                    AmountByLevel = Doubles(scale.criticalBonusByLevel),
                });
            if (charm is Charm_3Elemental_ByRow key)
            {
                var stats = new Dictionary<string, string>
                {
                    ["EMBER"] = "FIRE_DAMAGE",
                    ["GLACIER"] = "ICE_DAMAGE",
                    ["MAGITECH"] = "LIGHTNING_DAMAGE",
                    ["STURDY"] = "PHYSICAL_DAMAGE",
                };
                foreach (var pair in stats)
                    definition.ContextStats.Add(new ContextStatBonus
                    {
                        Source = StatCountSource.RowCategory,
                        Category = pair.Key,
                        StatusId = pair.Value,
                        AmountByLevel = Doubles(key.addElementalStatByLevel),
                    });
            }
        }

        private static List<double> Doubles(int[] values)
        {
            var result = new List<double>();
            if (values != null)
            {
                foreach (var value in values) result.Add(value);
            }
            return result;
        }

        private static List<double> NeighborLevelBonus(Charm_Basic charm)
        {
            var result = new List<double>();
            if (charm is Charm_NearLevelDamage near && near.allDamageBonusByLevel != null)
                foreach (var value in near.allDamageBonusByLevel) result.Add(value);
            return result;
        }

        /// <summary>
        /// 아티팩트 효과 설명. 게임 툴팁이 쓰는 <c>BuildEffectString</c>을 프리팹 컴포넌트에 그대로
        /// 부른다 — 문장을 우리가 흉내 내면 게임과 어긋나고, 값 자리표시자({VALUE} 같은 것)를
        /// 채우는 것이 이 함수이기 때문이다.
        ///
        /// <c>showAllLevel</c>을 켜서 레벨별 값이 범위로 나오게 한다. 덤프는 아티팩트 종류마다
        /// 한 번뿐이라 특정 레벨의 값을 담으면 다른 레벨에서 거짓말이 된다. 아바타를 넘기지 않고
        /// <c>ignoreAvatarStatus</c>를 켜는 것도 같은 이유다.
        /// </summary>
        private static List<string> EffectLines(Charm_Basic charm)
        {
            var lines = new List<string>();
            if (charm == null) return lines;

            try
            {
                var text = charm.BuildEffectString(
                    null, "", "", 1, 0, showAllLevel: true, ignoreAvatarStatus: true);

                foreach (var line in text.Split('\n'))
                {
                    var clean = RichText.Strip(line).Trim();
                    if (clean.Length > 0) lines.Add(clean);
                }
            }
            catch (System.Exception)
            {
                // 런타임 상태가 있어야 문장을 만드는 아티팩트가 있다. 그런 것은 설명 없이 둔다.
                lines.Clear();
            }
            return lines;
        }

        /// <summary>
        /// 설명이 비어 있어도 단계는 남긴다. 게임 데이터 요청 실패는 호출자가 갱신 실패로 처리한다.
        /// </summary>
        private static List<ComboEffectLine> EffectLines(ComboEffectBase combo)
        {
            var lines = new List<ComboEffectLine>();
            if (combo == null) return lines;

            foreach (var element in combo.RequestComboData(null))
            {
                lines.Add(new ComboEffectLine { Threshold = element.comboCount, Text = element.effectName });
            }
            return lines;
        }

        // 게임에 설정된 언어로 표시 이름을 담아 둔다. HUD가 그대로 보여준다.
        private static Dictionary<string, string> DisplayName(ItemEntity entity)
        {
            var names = new Dictionary<string, string>();
            var text = entity.aName?.ToString();
            if (!string.IsNullOrEmpty(text)) names["current"] = text;
            return names;
        }

        // 이름 키가 없는 아이템도 있어 그때는 엔티티 번호를 식별자로 쓴다.
        private static string IdFromKey(string key, string prefix, int entityId)
        {
            if (string.IsNullOrEmpty(key)) return entityId.ToString(CultureInfo.InvariantCulture);
            var start = key.StartsWith(prefix, StringComparison.Ordinal) ? prefix.Length : 0;
            var end = key.LastIndexOf('_');
            var id = end > start ? key.Substring(start, end - start) : key.Substring(start);
            return id.Length > 0 ? id : entityId.ToString(CultureInfo.InvariantCulture);
        }
    }
}

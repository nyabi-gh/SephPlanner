using System;
using System.Collections.Generic;
using System.Globalization;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임 리소스에서 아이템 정의를 읽어 Core 모델로 옮긴다.
    /// ItemDatabase 내부 상태에 기대지 않고 Resources 를 직접 훑는다.
    /// </summary>
    internal static class ItemCatalog
    {
        public static List<TabletDefinition> LoadTablets()
        {
            var result = new List<TabletDefinition>();
            foreach (var entity in Resources.LoadAll<ItemEntity>("Item"))
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
            foreach (var entity in Resources.LoadAll<ItemEntity>("Item"))
            {
                if (entity.type != EItemType.Charm) continue;
                if (entity.activeType == EItemActiveType.Disabled) continue;

                var charm = entity.resourcePrefab != null
                    ? entity.resourcePrefab.GetComponent<Charm_Basic>()
                    : null;

                result.Add(new CharmDefinition
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
                    EffectLines = EffectLines(charm),
                    NeighborLevelBonus = NeighborLevelBonus(charm),
                    Categories = entity.categories ?? new List<string>(),
                    Names = DisplayName(entity),
                });
            }
            return result;
        }

        /// <summary>
        /// 콤보(세트 효과) 정의. 임계값은 콤보 프리팹의 <c>addStatByCombo</c>와 구형 세트 효과의
        /// <c>setStatus</c> 양쪽에서 모은다. 게임의 <c>SearchSetEffectInInventory</c>가 두 경로를
        /// 다 쓰기 때문이다.
        /// </summary>
        public static List<ComboDefinition> LoadCombos()
        {
            var result = new List<ComboDefinition>();
            foreach (var category in Resources.LoadAll<ItemCategoryEntity>("ItemCategory"))
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

                if (thresholds.Count == 0) continue;

                var names = new Dictionary<string, string>();
                var text = category.categoryName?.ToString();
                if (!string.IsNullOrEmpty(text)) names["current"] = text;

                result.Add(new ComboDefinition
                {
                    Id = category.id,
                    Thresholds = new List<int>(thresholds),
                    Names = names,
                    Effects = EffectLines(combo),
                });
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

            foreach (var entity in Resources.LoadAll<ItemEntity>("Item"))
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

            foreach (var category in Resources.LoadAll<ItemCategoryEntity>("ItemCategory"))
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
        /// 임계값별 효과 텍스트. 게임 콤보 패널이 쓰는 <c>RequestComboData</c>를 프리팹 컴포넌트에
        /// 그대로 부른다. 아바타 없이도 대부분 동작하지만, 런타임 상태가 필요한 콤보는 터질 수
        /// 있으므로 그때는 임계값만 남긴다.
        /// </summary>
        private static List<ComboEffectLine> EffectLines(ComboEffectBase combo)
        {
            var lines = new List<ComboEffectLine>();
            if (combo == null) return lines;

            try
            {
                foreach (var element in combo.RequestComboData(null))
                {
                    if (string.IsNullOrEmpty(element.effectName)) continue;
                    lines.Add(new ComboEffectLine { Threshold = element.comboCount, Text = element.effectName });
                }
                lines.Sort((a, b) => a.Threshold.CompareTo(b.Threshold));
            }
            catch (System.Exception)
            {
                lines.Clear();
            }
            return lines;
        }

        // 게임에 설정된 언어로 표시 이름을 담아 둔다. 오버레이가 그대로 보여준다.
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

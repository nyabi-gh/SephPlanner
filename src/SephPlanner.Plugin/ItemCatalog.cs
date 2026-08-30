using System.Collections.Generic;
using SephPlanner.Core.Model;
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
                    MaxLevel = charm != null ? charm.maxLevel : 5,
                    CriteriaType = charm != null && charm.criteria != null ? charm.criteria.GetType().Name : "",
                    IsMagic = charm is Charm_Magic,
                    IsWeaponRelated = charm != null && charm.isWeaponRelatedCharm,
                    RelatedWeapon = charm != null && charm.isWeaponRelatedCharm
                        ? charm.relatedWeapon.ToString()
                        : "",
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
            if (string.IsNullOrEmpty(key)) return entityId.ToString();
            var start = key.StartsWith(prefix) ? prefix.Length : 0;
            var end = key.LastIndexOf('_');
            var id = end > start ? key.Substring(start, end - start) : key.Substring(start);
            return id.Length > 0 ? id : entityId.ToString();
        }
    }
}

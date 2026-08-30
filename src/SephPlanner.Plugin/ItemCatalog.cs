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
                    Id = IdFromKey(entity.aName?.key, "Item_StoneTablet_"),
                    EntityId = entity.id,
                    Rarity = (Rarity)(int)entity.rarity,
                    IsRotatable = tablet.isRotatable,
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

                result.Add(new CharmDefinition
                {
                    Id = IdFromKey(entity.aName?.key, "Item_"),
                    EntityId = entity.id,
                    Rarity = (Rarity)(int)entity.rarity,
                    Categories = entity.categories ?? new List<string>(),
                });
            }
            return result;
        }

        private static string IdFromKey(string key, string prefix)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var start = key.StartsWith(prefix) ? prefix.Length : 0;
            var end = key.LastIndexOf('_');
            return end > start ? key.Substring(start, end - start) : key.Substring(start);
        }
    }
}

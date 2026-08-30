using System.Collections.Generic;
using SephPlanner.Core.Ipc;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 지금 집거나 살 수 있는 아이템을 모은다.
    ///
    /// 상자, 바닥에 떨어진 꾸러미, 상점이 모두 각자의 GridInventory 를 들고 있다. 그래서 화면별
    /// UI 클래스를 따로 다룰 필요 없이, 플레이어 것이 아닌 가까운 인벤토리를 훑으면 된다.
    /// </summary>
    internal static class OfferReader
    {
        public static void Fill(GameSnapshot snapshot, PlayerAvatar player, float radius)
        {
            var playerInventory = player.Inventory;
            var origin = player.transform.position;

            foreach (var inventory in UnityEngine.Object.FindObjectsByType<GridInventory>(FindObjectsSortMode.None))
            {
                if (inventory == null || inventory == playerInventory) continue;
                if (inventory.UnitAvatar is PlayerAvatar) continue;
                if (Vector3.Distance(origin, inventory.transform.position) > radius) continue;

                Collect(snapshot.Offers, inventory);
            }
        }

        private static void Collect(List<OfferedItem> offers, GridInventory inventory)
        {
            var seen = new HashSet<int>();
            foreach (var pair in inventory.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null || !seen.Add(instance.InstanceID)) continue;

                var entity = instance.Entity;
                if (entity == null) continue;

                offers.Add(new OfferedItem
                {
                    DefinitionId = instance.EntityID,
                    Kind = KindOf(entity.type),
                    Price = entity.cost,
                    SlotIndex = offers.Count,
                });
            }
        }

        private static string KindOf(EItemType type)
        {
            switch (type)
            {
                case EItemType.Charm: return "charm";
                case EItemType.StoneTablet: return "tablet";
                default: return "other";
            }
        }
    }
}

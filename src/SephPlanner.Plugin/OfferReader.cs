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

                Collect(snapshot.Offers, inventory, player);
            }
        }

        private static void Collect(List<OfferedItem> offers, GridInventory inventory, PlayerAvatar buyer)
        {
            // 상자와 바닥에 떨어진 꾸러미는 주인이 없어 그냥 집으면 되고, 주인이 있는 인벤토리는 사야 한다.
            var seller = inventory.UnitAvatar;
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
                    Price = PriceOf(entity, seller, buyer),
                    SlotIndex = offers.Count,
                });
            }
        }

        /// <summary>
        /// 원가가 아니라 실제로 내야 하는 값. 게임은 파는 쪽과 사는 쪽의 협상 스탯 차이로
        /// 원가의 0.66배에서 3배까지 조정한다(<c>ItemDatabase.GetItemBuyPrice</c>).
        /// </summary>
        private static int PriceOf(ItemEntity entity, UnitAvatar seller, PlayerAvatar buyer)
        {
            if (seller == null) return 0;

            return ItemDatabase.GetItemBuyPrice(
                entity,
                seller.GetCustomStat(ECustomStat.Negotiation),
                buyer.GetCustomStat(ECustomStat.Negotiation));
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

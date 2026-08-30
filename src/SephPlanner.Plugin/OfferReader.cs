using System.Collections.Generic;
using SephPlanner.Core.Ipc;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 지금 집거나 살 수 있는 아이템을 모은다.
    ///
    /// 두 갈래다. 상자와 바닥에 떨어진 꾸러미, 상점은 각자의 GridInventory 를 들고 있어 한 번에
    /// 훑을 수 있다. 하지만 석판과 아티팩트가 나오는 세피라이트는 GridInventory 가 없고
    /// 보상 목록을 따로 들고 있어서, 그쪽은 따로 봐야 한다.
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

            CollectSephirites(snapshot.Offers, origin, radius);
        }

        /// <summary>
        /// 세피라이트 안에 든 후보들. 제단에서 무엇이 나올지는 고르기 전까지 서버만 알지만,
        /// 일단 세피라이트가 생기고 나면 <c>rewards</c>가 동기화되어 무엇이 들었는지 알 수 있다.
        /// 석판은 대개 이 경로로 나오므로 여기를 빼면 석판 추천이 아예 되지 않는다.
        /// </summary>
        private static void CollectSephirites(List<OfferedItem> offers, Vector3 origin, float radius)
        {
            foreach (var sephirite in UnityEngine.Object.FindObjectsByType<Sephirite>(FindObjectsSortMode.None))
            {
                if (sephirite == null || sephirite.isAcquired || !sephirite.isGenerated) continue;
                if (Vector3.Distance(origin, sephirite.transform.position) > radius) continue;

                foreach (var reward in sephirite.Rewards)
                {
                    var entity = ItemDatabase.FindItemById(reward.entityID);
                    if (entity == null) continue;

                    offers.Add(new OfferedItem
                    {
                        DefinitionId = reward.entityID,
                        Kind = KindOf(entity.type),

                        // 세피라이트는 사는 것이 아니라 여는 것이라 값이 없다.
                        Price = 0,
                        SlotIndex = offers.Count,
                    });
                }
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

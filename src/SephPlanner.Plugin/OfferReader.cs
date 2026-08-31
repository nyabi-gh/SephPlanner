using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SephPlanner.Core.Ipc;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 지금 집거나 살 수 있는 아이템을 모은다.
    ///
    /// 두 갈래다. 상자와 바닥에 떨어진 꾸러미, 상점은 각자의 GridInventory 를 들고 있어 한 번에
    /// 훑을 수 있고, 거리로 지금 닿을 수 있는 것만 고른다.
    ///
    /// 석판과 아티팩트가 나오는 세피라이트는 GridInventory 가 없어 보상 목록을 따로 봐야 하고,
    /// 거리도 쓰지 않는다. 세피라이트의 좌표는 플레이어와 같은 기준이 아니기 때문이다.
    /// </summary>
    internal static class OfferReader
    {
        public static void Fill(GameSnapshot snapshot, PlayerAvatar player, float radius)
        {
            var playerInventory = player.Inventory;
            var origin = player.transform.position;

            // 세피라이트를 먼저 담는다. 후보 수에 상한이 있어서, 뒤로 밀리면 상자가 많은 자리에서
            // 석판이 통째로 잘려 나간다. 석판은 대개 세피라이트로만 나오므로 이쪽이 우선이다.
            CollectSephirites(snapshot.Offers, origin, radius);

            var shown = ShownInventory();
            foreach (var inventory in UnityEngine.Object.FindObjectsByType<GridInventory>(FindObjectsSortMode.None))
            {
                if (inventory == null || inventory == playerInventory) continue;
                if (inventory.UnitAvatar is PlayerAvatar) continue;
                if (Vector3.Distance(origin, inventory.transform.position) > radius) continue;
                if (!IsVisible(inventory, shown)) continue;

                Collect(snapshot.Offers, inventory, player);
            }
        }

        /// <summary>
        /// 지금 플레이어가 볼 수 있는 인벤토리인가. 동기화돼 있어서 읽을 수 있는 것과 화면에
        /// 보이는 것은 다르고, 보이지 않는 것을 알려주는 순간 손으로도 할 수 있는 일의 대행이
        /// 아니게 된다.
        ///
        /// <b>상점이 그 예다.</b> 상점 재고는 상인의 <c>CurrentSelling</c> 인벤토리에 있는데
        /// 세상에 진열되는 것이 아니라 <c>UI_ShopPanel</c> 안에서만 그려진다. 그래서 가까이
        /// 가기만 해도 후보가 뜨는 것은 열기 전 상자를 들여다보는 것과 같다. 실제로 그렇게
        /// 보인다는 제보를 받고 고쳤다.
        /// </summary>
        private static bool IsVisible(GridInventory inventory, GridInventory shown)
        {
            if (inventory == shown) return true;

            // 상자는 뚜껑이 열렸는지가 곧 보이는지다.
            var chest = inventory.GetComponentInParent<ItemChest>();
            if (chest != null) return chest.isOpened;

            // 바닥에 떨어진 꾸러미는 보이던 인벤토리가 통째로 떨어진 것이라 숨길 이유가 없다.
            return inventory.GetComponentInParent<DroppedInventory>() != null;
        }

        /// <summary>
        /// 게임이 지금 창에 띄워 놓은 인벤토리. 상점은 공개 속성이 있고, 금고·시체를 여는
        /// 인벤토리 창은 비공개라 리플렉션으로 읽는다 - 읽기만 하고, 못 읽으면 "보이지 않는 것"
        /// 으로 물러선다(덜 보여주는 쪽이 안전하다).
        /// </summary>
        private static GridInventory ShownInventory()
        {
            var ui = UIManager.Instance;
            if (ui == null) return null;

            var shop = ui.GetElement<UI_ShopPanel>();
            if (shop != null && shop.IsOpened && shop.Shop != null) return shop.Shop;

            var viewer = ui.GetElement<UI_InventoryViewer>();
            if (viewer == null || !viewer.IsOpened) return null;

            if (_viewerInventory == null)
            {
                _viewerInventory = typeof(UI_InventoryViewer).GetProperty(
                    "Inventory", BindingFlags.NonPublic | BindingFlags.Instance);
            }
            return _viewerInventory?.GetValue(viewer, null) as GridInventory;
        }

        private static PropertyInfo _viewerInventory;

        /// <summary>
        /// 세피라이트 안에 든 후보들. 제단에서 무엇이 나올지는 고르기 전까지 서버만 알지만,
        /// 일단 세피라이트가 생기고 나면 <c>rewards</c>가 동기화되어 무엇이 들었는지 알 수 있다.
        /// 석판은 대개 이 경로로 나오므로 여기를 빼면 석판 추천이 아예 되지 않는다.
        /// </summary>
        /// <summary>
        /// 마지막으로 훑은 세피라이트들의 상태. 왜 어떤 선택지가 추천에 안 들어왔는지는 거리와
        /// 생성 여부로 갈리는데, 단축키로 덤프를 받는 방식은 키 입력이 게임에 닿아야만 해서
        /// 정작 필요할 때 못 쓴다. 그래서 플러그인이 스스로 로그에 남긴다.
        /// </summary>
        public static string LastSephiriteReport { get; private set; } = "";

        private static void CollectSephirites(List<OfferedItem> offers, Vector3 origin, float radius)
        {
            var report = new StringBuilder();

            // 판정 기준은 하나다: 보상 창이 열려 있고, 그 창이 지금 보여주는 세피라이트인가.
            // isGenerated 만으로는 부족하다 - 레벨업 보상은 창을 열기 전에 미리 생성될 수 있고
            // (실제로 레벨업을 미룬 채 다른 세피라이트를 열면 그 내용이 섞여 나왔다), 이전 방에
            // 열어 두고 온 세피라이트도 생성된 채 남아 있다. 화면에 보이는 것만 후보다.
            var rewardPanel = UIManager.Instance != null
                ? UIManager.Instance.GetElement<UI_SephiriteRewardPanel>()
                : null;
            var showing = rewardPanel != null && rewardPanel.IsOpened ? rewardPanel.sephirite : null;

            // 비활성 오브젝트도 함께 찾는다. 세피라이트를 여는 동안 본체가 잠시 꺼져 있으면
            // 기본 탐색으로는 보이지 않아, 정작 고르는 순간에 후보가 사라진다.
            var found = UnityEngine.Object.FindObjectsByType<Sephirite>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (var sephirite in found)
            {
                if (sephirite == null) continue;

                var distance = Vector3.Distance(origin, sephirite.transform.position);

                // own: 멀티에서는 세피라이트가 플레이어마다 겹쳐 스폰되고 내 것만 열린다
                // (SephiriteSpawner.TrySpawnForConnection). 남의 것 앞의 "후보 0개"를 가리는 값이다.
                report.Append($"[{sephirite.type} d={distance:0.0} gen={sephirite.isGenerated} ")
                      .Append($"acq={sephirite.isAcquired} n={sephirite.Rewards.Count} ")
                      .Append($"active={sephirite.gameObject.activeInHierarchy} ")
                      .Append($"own={sephirite.isOwned} shown={sephirite == showing}] ");

                if (sephirite != showing) continue;
                if (sephirite.isAcquired || !sephirite.isGenerated) continue;

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

            LastSephiriteReport = report.ToString();
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

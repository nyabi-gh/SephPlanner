using System.Collections.Generic;
using System.Reflection;
using System.Text;
using SephPlanner.Core.Runtime;
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
        /// <summary>
        /// 상자·상점·시체의 인벤토리. 뚜껑이 열리는 것은 이미 씬에 있던 것의 상태가 바뀌는
        /// 것이라, 목록을 들고 있어도 여는 순간이 늦어지지 않는다. 늦어질 수 있는 것은 새로
        /// 떨어진 꾸러미뿐이고 그것도 이 간격만큼이다.
        /// </summary>
        /// <summary>
        /// 등록부로 바꾼 것이 무언가를 놓치는지 <b>가끔 확인만</b> 한다. 옛 방식(씬 전수 탐색)이
        /// 6ms 라 폴링마다 돌릴 수는 없지만, 아주 가끔이면 값이 없고 어긋남은 반드시 잡힌다.
        /// 한 세션 내내 0 이면 이 대조를 걷어낸다.
        /// </summary>
        private const float CrossCheckInterval = 10f;
        private static float _crossCheckedAt = float.NegativeInfinity;

        /// <summary>
        /// 세피라이트는 <b>보상 창이 열려 있을 때만</b> 찾는다. 후보가 될 수 있는 것은 그 창이
        /// 지금 보여주는 하나뿐이므로, 닫혀 있으면 아무리 찾아도 후보가 나올 수 없다.
        ///
        /// 비활성까지 뒤지는 것은 여는 동안 본체가 잠시 꺼지기 때문인데, 그 순간이 바로 창이
        /// 열려 있는 때다. 그래서 정확히 필요한 동안에만 가장 비싼 탐색을 한다.
        /// </summary>
        private static readonly SceneCache<Sephirite> Sephirites =
            new SceneCache<Sephirite>(1f, FindObjectsInactive.Include);
        public static void Fill(GameSnapshot snapshot, PlayerAvatar player, float radius)
        {
            var playerInventory = player.Inventory;
            var origin = player.transform.position;

            // 세피라이트를 먼저 담는다. 후보 수에 상한이 있어서, 뒤로 밀리면 상자가 많은 자리에서
            // 석판이 통째로 잘려 나간다. 석판은 대개 세피라이트로만 나오므로 이쪽이 우선이다.
            var step = FrameCost.Now;
            CollectSephirites(snapshot.Offers, origin, radius);
            FrameCost.Sephirites.Add(step);

            step = FrameCost.Now;
            var walking = FrameCost.Now;
            var found = NetworkedInventories.All();
            FrameCost.ChestAlive.Add(walking);
            CrossCheck(found);

            var filtering = FrameCost.Now;
            var shown = ShownInventory();
            var nearby = new List<GridInventory>();
            foreach (var inventory in found)
            {
                if (inventory == null || inventory == playerInventory) continue;
                if (inventory.UnitAvatar is PlayerAvatar) continue;
                if (Vector3.Distance(origin, inventory.transform.position) > radius) continue;
                if (!IsVisible(inventory, shown)) continue;

                nearby.Add(inventory);
            }

            // FindObjectsByType 의 순서는 비보장이다. 순서만 흔들려도 스냅샷이 "변경"으로 보여
            // 재계산이 돌고 후보 순번이 바뀐다.
            nearby.Sort((a, b) => a.netId.CompareTo(b.netId));
            FrameCost.ChestFilter.Add(filtering);

            var collecting = FrameCost.Now;
            foreach (var inventory in nearby) Collect(snapshot.Offers, inventory, player);
            FrameCost.ChestCollect.Add(collecting);

            FrameCost.CountInventories(found.Count, nearby.Count);
            FrameCost.Chests.Add(step);
        }

        /// <summary>
        /// 등록부가 찾은 것과 옛 씬 전수 탐색이 찾은 것을 견준다. 어긋나면 그 수를 덤프에 남긴다 -
        /// 바닥 꾸러미가 풀에서 나올 때 Mirror 에 등록되지 않는다면 여기서 드러난다.
        /// </summary>
        private static void CrossCheck(List<GridInventory> registry)
        {
            if (Time.unscaledTime - _crossCheckedAt < CrossCheckInterval) return;
            _crossCheckedAt = Time.unscaledTime;

            var at = FrameCost.Now;
            var scanned = Object.FindObjectsByType<GridInventory>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            FrameCost.ChestFind.Add(at);

            var missing = 0;
            GridInventory first = null;
            foreach (var inventory in scanned)
            {
                if (inventory == null || registry.Contains(inventory)) continue;
                missing++;
                if (first == null) first = inventory;
            }
            FrameCost.CountRegistryCheck(
                registry.Count, scanned.Length,
                missing == 0
                    ? $"등록부 {registry.Count} 대 탐색 {scanned.Length}"
                    : $"등록부에 없는 것 {missing}개, 예: {first.name} netId={first.netId}");
        }

        /// <summary>
        /// 진단이 쓰는 같은 판정. 덤프도 보이지 않는 인벤토리의 내용을 적으면 안 되므로, 규칙을
        /// 복사하지 않고 여기 하나를 부른다.
        /// </summary>
        public static bool IsShown(GridInventory inventory) =>
            inventory != null && IsVisible(inventory, ShownInventory());

        /// <summary>보상 창이 지금 보여 주는 세피라이트. 없으면 <c>null</c>.</summary>
        public static Sephirite ShownSephirite()
        {
            var panel = UIManager.Instance != null
                ? UIManager.Instance.GetElement<UI_SephiriteRewardPanel>()
                : null;
            return panel != null && panel.IsOpened ? panel.sephirite : null;
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
        /// 마지막으로 훑은 세피라이트들의 상태. 왜 어떤 선택지가 추천에 안 들어왔는지는 거리와
        /// 생성 여부로 갈리는데, 단축키로 덤프를 받는 방식은 키 입력이 게임에 닿아야만 해서
        /// 정작 필요할 때 못 쓴다. 그래서 플러그인이 스스로 로그에 남긴다.
        /// </summary>
        public static string LastSephiriteReport { get; private set; } = "";

        /// <summary>
        /// 현재 열린 보상 창과 연결된 세피라이트의 동기화된 후보를 수집한다.
        /// </summary>
        private static void CollectSephirites(List<OfferedItem> offers, Vector3 origin, float radius)
        {
            // 판정 기준은 하나다: 보상 창이 열려 있고, 그 창이 지금 보여주는 세피라이트인가.
            // isGenerated 만으로는 부족하다 - 레벨업 보상은 창을 열기 전에 미리 생성될 수 있고
            // (실제로 레벨업을 미룬 채 다른 세피라이트를 열면 그 내용이 섞여 나왔다), 이전 방에
            // 열어 두고 온 세피라이트도 생성된 채 남아 있다. 화면에 보이는 것만 후보다.
            var showing = ShownSephirite();

            // 창이 닫혀 있으면 후보가 될 수 있는 것이 하나도 없다. 그런데도 씬을 뒤지면 실기에서
            // 폴링마다 7ms 를 그냥 버린다(비활성까지 뒤지는 가장 비싼 형태다). 가까이 있는
            // 세피라이트의 상태가 궁금할 때는 F10 덤프가 따로 훑어 준다.
            if (showing == null)
            {
                LastSephiriteReport = "보상 창 닫힘";
                return;
            }

            var report = new StringBuilder();
            foreach (var sephirite in Sephirites.Get())
            {
                if (sephirite == null) continue;

                var distance = Vector3.Distance(origin, sephirite.transform.position);

                // own: 멀티에서는 세피라이트가 플레이어마다 겹쳐 스폰되고 내 것만 열린다
                // (SephiriteSpawner.TrySpawnForConnection). 남의 것 앞의 "후보 0개"를 가리는 값이다.
                // 보상 개수는 보이는 세피라이트에만 적는다. 열기 전에 내용이 채워지는 것이
                // 있어서(레벨업 보상), 안 보이는 것의 개수까지 적으면 화면에 없는 것을 알리게 된다.
                var shown = sephirite == showing;
                report.Append($"[{sephirite.type} d={distance:0.0} gen={sephirite.isGenerated} ")
                      .Append($"acq={sephirite.isAcquired} ")
                      .Append(shown ? $"n={sephirite.Rewards.Count} " : "")
                      .Append($"active={sephirite.gameObject.activeInHierarchy} ")
                      .Append($"own={sephirite.isOwned} shown={shown}] ");

                if (!shown) continue;
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
            var instances = new List<NewItemOwnInstance>();

            foreach (var pair in inventory.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null || !seen.Add(instance.InstanceID)) continue;
                instances.Add(instance);
            }

            // 딕셔너리 열거 순서도 보장이 아니다. 줄이 흔들리면 후보 순번이 흔들린다.
            instances.Sort((a, b) => a.InstanceID.CompareTo(b.InstanceID));

            foreach (var instance in instances)
            {
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

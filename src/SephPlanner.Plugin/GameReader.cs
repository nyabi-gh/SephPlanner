using System;
using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SephPlanner.Plugin
{
    /// <summary>살아 있는 게임 상태를 스냅샷으로 옮긴다. 읽기만 한다.</summary>
    internal static class GameReader
    {
        /// <summary>
        /// 항상 스냅샷을 돌려준다. 런이 끝났거나 플레이어가 죽었으면 인벤토리가 비어 있는 스냅샷이다.
        /// 이 상태를 넘겨야 HUD에서 직전 런의 배치를 지울 수 있다.
        /// </summary>
        public static GameSnapshot Read(float offerRadius, bool includeRecommendations = true)
        {
            var snapshot = new GameSnapshot
            {
                GameVersion = Application.version,
            };

            // 아바타가 없거나 죽어서 일찍 돌아가는 스냅샷도 멀티 여부는 정확해야 한다.
            // 여기서 기본값 false 로 두면 자동 배치 잠금이 열린 쪽으로 무너진다.
            snapshot.IsMultiplayer = IsMultiplayerSession();

            var avatar = FindLocalPlayer();

            // 성장 진행도는 게임이 화면으로 보낼 때만 손에 들어온다. 아바타가 바뀌면 다시 붙는다.
            GrowthProgressWatch.Follow(avatar);
            if (avatar == null || avatar.Inventory == null || avatar.IsDead)
            {
                FixedEffectLayer.Forget();
                return snapshot;
            }

            snapshot.Run = ReadRun(avatar);

            var step = FrameCost.Now;
            snapshot.Inventory = ReadInventory(avatar.Inventory);
            FrameCost.Inventory.Add(step);

            if (includeRecommendations)
            {
                step = FrameCost.Now;
                snapshot.Mixer = ReadMixer(avatar.transform.position, offerRadius * MixerReach);
                snapshot.EnchantChance = ReadEnchantChance(
                    avatar.transform.position, offerRadius * MixerReach);
                FrameCost.Mixer.Add(step);

                OfferReader.Fill(snapshot, avatar, offerRadius);
            }
            return snapshot;
        }

        public static RuntimeSimulationCheck CheckSimulation()
        {
            var avatar = FindLocalPlayer();
            if (avatar == null || avatar.Inventory == null || avatar.IsDead)
            {
                return new RuntimeSimulationCheck
                {
                    Status = PlanVerificationStatus.Unavailable,
                    Reason = "실시간 시뮬레이션을 검증할 인벤토리가 없습니다.",
                };
            }

            return SimulationVerifier.Check(avatar.Inventory);
        }

        /// <summary>
        /// 가방에서 게임이 지금 선택으로 보는 칸의 아티팩트 번호. 커서를 올리거나 패드로 고른 칸이다.
        /// 가방이 닫혀 있거나, 내 가방이 아니거나, 아티팩트가 아니면 0 이다.
        /// </summary>
        public static int SelectedCharm()
        {
            var selected = EventSystem.current == null ? null : EventSystem.current.currentSelectedGameObject;
            var icon = selected == null ? null : selected.GetComponent<UI_NewInventoryIcon>();
            var avatar = FindLocalPlayer();
            if (icon == null || avatar == null || avatar.Inventory == null || icon.Inventory != avatar.Inventory) return 0;

            var item = icon.Item;
            var entity = item?.Entity;
            return entity != null && entity.type == EItemType.Charm ? item.EntityID : 0;
        }

        internal static PlayerAvatar FindLocalPlayer()
        {
            var identity = NetworkClient.localPlayer;
            if (identity != null)
            {
                var byIdentity = identity.GetComponent<PlayerAvatar>();
                if (byIdentity != null) return byIdentity;
            }

            // 세션 초기화 중에는 localPlayer 가 비어 있을 수 있어 씬을 훑는 폴백을 남겨 둔다.
            // 다만 클라이언트가 서 있지 않으면 이 폴백은 반드시 빈손이다 - 게임의 Mirror 는
            // NetworkClient 의 스폰 경로에서만 isLocalPlayer 를 켜고 Shutdown 이 그것을 지운다.
            // 그런데 폴백이 FindObjectsByType 이라 이 게임에서 한 번에 4ms 대이고, 타이틀·로비가
            // 정확히 localPlayer 가 비어 있는 상태여서 폴링마다 그 값을 치르고 있었다.
            // active 는 접속 중에도 참이므로 위 초기화 구간은 그대로 살아 있다.
            if (!NetworkClient.active) return null;

            foreach (var candidate in UnityEngine.Object.FindObjectsByType<PlayerAvatar>(FindObjectsSortMode.None))
            {
                if (candidate.isLocalPlayer) return candidate;
            }
            return null;
        }

        internal static bool IsMultiplayerSession()
        {
            if (NetworkClient.active && !NetworkServer.active) return true;
            return NetworkServer.active && NetworkServer.connections.Count > 1;
        }

        /// <summary>
        /// 이 층의 석판 합성기. 거리를 보지 않는 것은 의도다 - 미니맵에 뜨는 고정물이라 층에
        /// 있다는 사실 자체가 이미 보이는 정보이고, 무엇을 합칠지는 합성기 앞에 서기 전에 정해
        /// 두는 편이 쓸모 있다.
        ///
        /// <c>LocalUsed</c>는 나 자신이 썼는지다. 합성기는 사람마다 층에 한 번씩 쓸 수 있다.
        /// </summary>

        internal static bool IsMixerOpen()
        {
            var panel = UIManager.Instance != null ? UIManager.Instance.GetElement<UI_TabletMixPanel>() : null;
            return panel != null && panel.IsOpened;
        }

        /// <summary>
        /// 제단이나 인챈트 물약이 연 아티팩트 선택 창이 지금 떠 있는가. 인챈트 조언은 이때만
        /// 화면에 나간다 - 합성 추천이 상점을 열어도 같이 보이던 것과 같은 실수를 피한다
        /// (<c>NativeHud.RenderMixes</c> 의 주석).
        ///
        /// 가방 창과 같은 <c>UI_CharacterStatusPanel</c> 이고 모드로만 갈린다. 창이 닫힐 때
        /// 게임이 스스로 <c>None</c> 으로 되돌리므로 모드만 봐도 된다.
        /// </summary>
        internal static bool IsEnchantOpen()
        {
            var panel = UIManager.Instance != null
                ? UIManager.Instance.GetElement<UI_CharacterStatusPanel>()
                : null;
            return panel != null && panel.IsOpened &&
                   panel.InventoryMode == UI_CharacterStatusPanel.EInventoryMode.Enchant;
        }

        private static readonly System.Reflection.FieldInfo AltarRemaining =
            GameBinding.Field(typeof(AltarOfEnchant), "localRemaining");

        /// <summary>
        /// 지금 인챈트를 걸 수 있는 기회. 제단도 없고 창도 닫혀 있으면 <c>null</c> 이다.
        ///
        /// 합성기와 같이 <b>거리를 함께 싣는다.</b> 미니맵에 뜨는 고정물이라 이 층에 있다는 사실
        /// 자체는 숨은 정보가 아니지만, 인챈트 조언은 공짜가 아니라서(아티팩트 35개 가방에서 377ms)
        /// 층에 제단이 있기만 하면 계속 돌게 두면 안 된다. 걸어가는 동안 준비될 만큼 넉넉한
        /// 거리에서만 돈다 - 합성기와 같은 반경이다.
        ///
        /// 게임은 사람마다 따로 세고(<c>remainingByGuid</c>), 클라이언트는 접속할 때 한 번 물어
        /// 받아 둔다. <b>답이 오기 전에는 -1 이다</b> - 그대로 0 으로 옮기면 아직 쓰지도 않은
        /// 제단이 다 쓴 것으로 보이므로, 그때는 프리팹이 정한 <c>localUseCount</c> 로 메운다.
        ///
        /// 층에 제단이 여럿이면 더한다. 제단마다 내 몫이 따로 있어 실제로 그만큼 걸 수 있다.
        /// </summary>
        private static EnchantChanceState ReadEnchantChance(Vector3 origin, float reach)
        {
            var found = false;
            var uses = 0;
            var near = false;
            foreach (var altar in NetworkRegistry.All<AltarOfEnchant>())
            {
                if (altar == null) continue;
                found = true;

                var local = AltarRemaining != null && AltarRemaining.GetValue(altar) is int value ? value : -1;
                var remaining = local < 0 ? Math.Max(0, altar.localUseCount) : local;
                uses += remaining;

                // 아직 쓸 수 있는 제단만 거리를 따진다. 다 쓴 제단 앞에 서 있다고 계산할 일은 없다.
                if (remaining > 0 && Vector3.Distance(origin, altar.transform.position) <= reach) near = true;
            }

            // 물약은 제단 없이도 창을 연다. 층에 제단이 없어도 창이 열려 있으면 기회가 있는 것이다.
            var open = IsEnchantOpen();
            return found || open
                ? new EnchantChanceState { AltarUses = uses, Open = open, Near = near }
                : null;
        }

        /// <summary>
        /// 합성 추천을 계산하기 시작할 거리. 후보 반경의 배수다 - 걸어가는 동안 준비되어야
        /// 창을 열었을 때 이미 떠 있다. 문턱을 넘는 순간 한 번 다시 푸는 것은 정상이다.
        /// </summary>
        private const float MixerReach = 3f;

        private static MixerState ReadMixer(Vector3 origin, float reach)
        {
            // 아무거나 하나를 함께 기억해 두면, 다 썼을 때를 위해 목록을 다시 훑지 않아도 된다.
            TabletMix any = null;
            MixerState unused = null;
            foreach (var mixer in NetworkRegistry.All<TabletMix>())
            {
                if (mixer == null) continue;

                // 유니티 객체에는 ?? 를 쓰지 않는다. 파괴된 객체를 null 로 보는 것은 유니티가
                // 덮어쓴 == 뿐이라, ?? 로는 이미 파괴된 것을 붙들게 된다.
                if (mixer.LocalUsed)
                {
                    if (any == null) any = mixer;
                    continue;
                }

                // 여럿이면 아직 쓸 수 있는 쪽이, 그중에서도 가까운 쪽이 답이다. 먼 것 때문에
                // 바로 앞 합성기의 추천을 미루면 안 된다.
                var near = Vector3.Distance(origin, mixer.transform.position) <= reach;
                if (unused == null || near && unused.Near == false)
                    unused = new MixerState { Cost = mixer.mixCost, Used = false, Near = near };
            }

            if (unused != null) return unused;
            return any == null ? null : new MixerState { Cost = any.mixCost, Used = true, Near = false };
        }

        private static RunState ReadRun(PlayerAvatar avatar)
        {
            var weapons = avatar.GetComponent<WeaponControllerSimple>();
            return new RunState
            {
                WeaponId = weapons != null && weapons.currentWeapon != null
                    ? weapons.currentWeapon.weaponType.ToString()
                    : "",
                Gold = avatar.Money,
            };
        }

        private static InventoryState ReadInventory(GridInventory inv)
        {
            var state = new InventoryState
            {
                Width = inv.Width,
                Height = inv.Height,
                Storage = inv.CurrentInventoryStorage,
            };

            var grid = GridOf(inv);
            var seenItems = new HashSet<int>();
            foreach (var pair in inv.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null || instance.StoneTablet != null) continue;

                // 포션 벨트는 같은 딕셔너리를 쓰지만 격자가 아니라 y=100 줄에 산다
                // (x 는 0..numberOfPotionStorage-1). 석판 배치와는 아무 상관이 없는 자리이므로
                // 아이템 목록에 섞으면 "가방에 아이템 몇 개" 같은 셈이 조용히 어긋난다.
                // 진단이 필요할 때는 F10 덤프가 딕셔너리를 있는 그대로 보여준다.
                if (!grid.Contains(instance.XIdx, instance.YIdx)) continue;

                var identity = ItemIdentity.Of(instance.InstanceID, grid, instance.XIdx, instance.YIdx);
                if (!seenItems.Add(identity)) continue;

                state.Items.Add(new PlacedItem
                {
                    DefinitionId = instance.EntityID,
                    InstanceId = identity,
                    Immovable = instance.InstanceID == 0,
                    ObservedCategories = instance.Charm is Charm_WhitePaper paper
                        ? new List<string>(paper.GetItemCategory()) : null,
                    Position = new GridPos(instance.XIdx, instance.YIdx),
                    EffectiveLevel = LookupMatrix(inv.levelMatrix, instance.XIdx, instance.YIdx),
                    IsActive = LookupMatrix(inv.disableMatrix, instance.XIdx, instance.YIdx) <= 0,
                    IsAttackable = instance.Charm != null
                        ? (bool?)(instance.Charm is IAttackableCharm attackable && attackable.IsAttackableCharm())
                        : null,
                    Enchant = EnchantOf(instance.InstanceID),
                    GrowthProgress = GrowthProgressOf(instance.InstanceID, instance.Charm),
                    GrowthGoal = instance.Charm is Charm_GrowthStatusInstance goal && goal.hasGrowthQuest
                        ? goal.growthQuestGoal : 0,
                });
            }

            var seenTablets = new HashSet<int>();
            foreach (var pair in inv.stoneTablets)
            {
                var tablet = pair.Value;
                if (tablet == null || !seenTablets.Add(tablet.instanceID)) continue;

                state.Tablets.Add(Describe(tablet));
            }

            foreach (var engraving in inv.engravings)
            {
                if (engraving == null) continue;
                state.Engravings.Add(Describe(engraving));
            }

            // 게임이 계산해 둔 값. 우리 시뮬레이터를 대조하는 정답지로 쓴다.
            foreach (var pair in inv.levelMatrix)
                state.LevelMatrix[CellKey(pair.Key.x, pair.Key.y)] = pair.Value;

            foreach (var pair in inv.disableMatrix)
                if (pair.Value > 0) state.DisabledCells.Add(CellKey(pair.Key.x, pair.Key.y));

            // 유니크 페어 보정 같은 규칙까지 게임이 이미 반영해 둔 값이다. 우리가 다시 세지 않는다.
            foreach (var pair in inv.currentSetEffectCount)
                state.ComboCounts[pair.Key] = pair.Value;

            var layer = FixedEffectLayer.Get(inv);
            state.FixedEffects.AddRange(layer.Cells);
            state.ComboEngraving = layer.ComboEngraving;

            return state;
        }

        /// <summary>
        /// 인챈트는 게임이 인스턴스마다 따로 들고 있다. 보고된 레벨에서 역산하면 배수가 걸린 칸에서
        /// 어긋나므로 그대로 읽는다. SyncDictionary 라 클라이언트에서도 값이 있다.
        /// </summary>
        internal static int EnchantOf(int instanceId)
        {
            var dungeon = DungeonManager.Instance;
            if (dungeon == null) return 0;

            return int.TryParse(dungeon.GetGlobalItemStatValue(instanceId, "Enchant"), out var enchant)
                ? enchant
                : 0;
        }

        private static readonly System.Reflection.FieldInfo GrowthCounter =
            GameBinding.Field(typeof(Charm_GrowthStatusInstance), "questCounter");

        /// <summary>
        /// 성장 아티팩트가 목표까지 얼마나 왔는지. 읽지 못하면 <c>null</c> 이다.
        ///
        /// 게임은 이 값을 <c>protected</c> 필드에 두고 SyncVar 로 내보내지 않는다. 소유자 화면에는
        /// <c>SetEffectHUDValue</c> 로 문자열만 가므로 <b>참가자 세션에서는 읽을 길이 없다</b> -
        /// 그 자리에서 필드를 읽으면 서버의 진짜 값이 아니라 초기값이 나온다. 0 으로 적으면
        /// 아직 아무것도 못 채운 것과 구분되지 않으므로 모를 때는 비워 둔다.
        /// </summary>
        private static int? GrowthProgressOf(int instanceId, Charm_Basic charm)
        {
            if (charm is not Charm_GrowthStatusInstance growth || !growth.hasGrowthQuest) return null;

            // 게임이 보내 준 표시값이 먼저다. 호스트든 참가자든 같은 이벤트로 오고, 효과가 켜질
            // 때마다 다시 오므로 배치가 갱신되면 최신값이 들어와 있다.
            var watched = GrowthProgressWatch.Of(instanceId);
            if (watched != null) return watched;

            // 아직 한 번도 안 왔으면 서버에서만 원본을 읽는다. 참가자 자리에서 이 필드를 읽으면
            // 서버의 값이 아니라 초기값이 나오므로 모르는 채로 둔다.
            if (!Mirror.NetworkServer.active || GrowthCounter == null) return null;
            return GrowthCounter.GetValue(growth) is int counter ? counter : (int?)null;
        }

        private static PlacedTablet Describe(StoneTablet tablet) => new PlacedTablet
        {
            DefinitionId = tablet.entityID,
            InstanceId = tablet.instanceID,
            Position = new GridPos(tablet.xIdx, tablet.yIdx),
            Rotation = tablet.rotation,
            IsApplied = tablet.IsApplied,
            IsRotatable = DungeonManager.IsTabletRotatable(tablet.instanceID, tablet.isRotatable),
            Query = tablet.isCustomTablet ? tablet.GetQuery(tablet.instanceID) : null,
            ConditionQuery = tablet.isCustomTablet ? tablet.GetConditionQuery(tablet.instanceID) : null,
            Name = DungeonManager.GetItemName(tablet.instanceID, null),
        };

        /// <summary>
        /// 살아 있는 인벤토리를 솔버가 쓰는 격자 규격으로. 칸 안팎을 가리는 판정이 이것 하나로
        /// 모여, 읽기와 자동 배치와 솔버가 같은 격자를 본다(<see cref="GridSpec.Contains"/>).
        /// </summary>
        internal static GridSpec GridOf(GridInventory inv) =>
            new GridSpec(inv.Width, inv.Height, inv.CurrentInventoryStorage);

        private static int LookupMatrix(SyncDictionary<ItemPosition, int> matrix, sbyte x, sbyte y) =>
            matrix.TryGetValue(new ItemPosition(x, y), out var value) ? value : 0;

        private static string CellKey(int x, int y) => x + "," + y;
    }
}

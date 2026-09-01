using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>살아 있는 게임 상태를 스냅샷으로 옮긴다. 읽기만 한다.</summary>
    internal static class GameReader
    {
        /// <summary>
        /// 항상 스냅샷을 돌려준다. 런이 끝났거나 플레이어가 죽었으면 인벤토리가 비어 있는 스냅샷이다.
        /// 이 상태를 넘겨야 HUD에서 직전 런의 배치를 지울 수 있다.
        /// </summary>
        public static GameSnapshot Read(float offerRadius)
        {
            var snapshot = new GameSnapshot
            {
                GameVersion = Application.version,
            };

            // 아바타가 없거나 죽어서 일찍 돌아가는 스냅샷도 멀티 여부는 정확해야 한다.
            // 여기서 기본값 false 로 두면 자동 배치 잠금이 열린 쪽으로 무너진다.
            snapshot.IsMultiplayer = IsMultiplayerSession();

            var avatar = FindLocalPlayer();
            if (avatar == null || avatar.Inventory == null || avatar.IsDead) return snapshot;

            snapshot.Run = ReadRun(avatar);
            snapshot.Inventory = ReadInventory(avatar.Inventory);
            snapshot.Mixer = ReadMixer();
            OfferReader.Fill(snapshot, avatar, offerRadius);
            return snapshot;
        }

        public static string DumpInventory(float offerRadius)
        {
            var avatar = FindLocalPlayer();
            return avatar?.Inventory == null
                ? null
                : InventoryDiagnostics.Write(avatar.Inventory, avatar, offerRadius);
        }

        public static string CheckSimulation()
        {
            var avatar = FindLocalPlayer();
            if (avatar == null || avatar.Inventory == null || avatar.IsDead) return null;
            return SimulationVerifier.Check(avatar.Inventory);
        }

        internal static PlayerAvatar FindLocalPlayer()
        {
            var identity = NetworkClient.localPlayer;
            if (identity != null)
            {
                var byIdentity = identity.GetComponent<PlayerAvatar>();
                if (byIdentity != null) return byIdentity;
            }

            // 세션 초기화 중에는 localPlayer 가 비어 있을 수 있다.
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
        private static MixerState ReadMixer()
        {
            foreach (var mixer in UnityEngine.Object.FindObjectsByType<TabletMix>(FindObjectsSortMode.None))
            {
                if (mixer == null) continue;

                // 여럿이면 아직 쓸 수 있는 쪽이 답이다.
                if (!mixer.LocalUsed) return new MixerState { Cost = mixer.mixCost, Used = false };
            }

            foreach (var mixer in UnityEngine.Object.FindObjectsByType<TabletMix>(FindObjectsSortMode.None))
                if (mixer != null) return new MixerState { Cost = mixer.mixCost, Used = true };

            return null;
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

            var seenItems = new HashSet<int>();
            foreach (var pair in inv.inventoryMatrix)
            {
                var instance = pair.Value;
                if (instance == null || instance.StoneTablet != null) continue;
                if (!seenItems.Add(instance.InstanceID)) continue;

                // 포션 벨트는 같은 딕셔너리를 쓰지만 격자가 아니라 y=100 줄에 산다
                // (x 는 0..numberOfPotionStorage-1). 석판 배치와는 아무 상관이 없는 자리이므로
                // 아이템 목록에 섞으면 "가방에 아이템 몇 개" 같은 셈이 조용히 어긋난다.
                // 진단이 필요할 때는 F10 덤프가 딕셔너리를 있는 그대로 보여준다.
                if (!IsOnGrid(instance.XIdx, instance.YIdx, inv)) continue;

                state.Items.Add(new PlacedItem
                {
                    DefinitionId = instance.EntityID,
                    InstanceId = instance.InstanceID,
                    Position = new GridPos(instance.XIdx, instance.YIdx),
                    EffectiveLevel = LookupMatrix(inv.levelMatrix, instance.XIdx, instance.YIdx),
                    IsActive = LookupMatrix(inv.disableMatrix, instance.XIdx, instance.YIdx) <= 0,
                    Enchant = EnchantOf(instance.InstanceID),
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

            state.FixedEffects.AddRange(ReadFixedEffects(inv));

            return state;
        }

        /// <summary>
        /// 고정 각인(신비 콤보 등)이 칸에 박아 둔 효과. 서버에만 있는 값이라 호스트(싱글 포함)에서만
        /// 읽을 수 있고, 클라이언트로 접속한 세션에서는 빈 목록이 된다.
        /// </summary>
        internal static List<FixedEffectCell> ReadFixedEffects(GridInventory inv)
        {
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            if (NetworkServer.active)
            {
                foreach (var engraving in inv.fixedEngravingsOnServer)
                {
                    if (engraving == null) continue;
                    foreach (var pair in engraving.fixedLevel) At(cells, pair.Key).Level += pair.Value;
                    foreach (var pair in engraving.fixedDisable) At(cells, pair.Key).Disable += pair.Value;
                    foreach (var pair in engraving.fixedIgnoreCriteria) At(cells, pair.Key).IgnoreCriteria += pair.Value;
                    foreach (var pair in engraving.fixedMultiplyLevel) At(cells, pair.Key).Multiply += pair.Value;
                }
            }
            return new List<FixedEffectCell>(cells.Values);
        }

        private static FixedEffectCell At(Dictionary<GridPos, FixedEffectCell> cells, ItemPosition position)
        {
            var key = new GridPos(position.x, position.y);
            if (!cells.TryGetValue(key, out var cell))
            {
                cell = new FixedEffectCell { Position = key };
                cells[key] = cell;
            }
            return cell;
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
        /// 본 격자 안의 자리인가. 격자 밖 좌표는 포션 벨트(y=100)이고, 보조 가방은 아예 다른
        /// 딕셔너리(<c>subBagMatrix</c>)라 여기 오지 않는다.
        /// </summary>
        private static bool IsOnGrid(sbyte x, sbyte y, GridInventory inv) =>
            x >= 0 && x < inv.Width && y >= 0 && y < inv.Height;

        private static int LookupMatrix(SyncDictionary<ItemPosition, int> matrix, sbyte x, sbyte y)
        {
            foreach (var pair in matrix)
                if (pair.Key.x == x && pair.Key.y == y) return pair.Value;
            return 0;
        }

        private static string CellKey(int x, int y) => x + "," + y;
    }
}

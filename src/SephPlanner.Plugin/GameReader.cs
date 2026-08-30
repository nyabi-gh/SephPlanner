using System;
using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>살아 있는 게임 상태를 스냅샷으로 옮긴다. 읽기만 한다.</summary>
    internal static class GameReader
    {
        /// <summary>
        /// 항상 스냅샷을 돌려준다. 런이 끝났거나 플레이어가 죽었으면 인벤토리가 비어 있는 스냅샷이다.
        /// 이때 아무것도 보내지 않으면 오버레이에 직전 런의 배치가 그대로 남는다.
        /// </summary>
        public static GameSnapshot Read(float offerRadius)
        {
            var snapshot = new GameSnapshot
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameVersion = Application.version,
            };

            var avatar = FindLocalPlayer();
            if (avatar == null || avatar.Inventory == null || avatar.IsDead) return snapshot;

            snapshot.IsMultiplayer = IsMultiplayerSession();
            snapshot.Run = ReadRun(avatar);
            snapshot.Inventory = ReadInventory(avatar.Inventory);
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

        private static PlayerAvatar FindLocalPlayer()
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

        private static bool IsMultiplayerSession()
        {
            if (NetworkClient.active && !NetworkServer.active) return true;
            return NetworkServer.active && NetworkServer.connections.Count > 1;
        }

        /// <summary>
        /// 배치와 무관하지만 추천에 영향을 주는 런 상태. 무기 연동 아티팩트는 해당 무기를 들고
        /// 있어야 효과가 켜지므로(<c>Charm_Basic.RefreshCharm</c>) 장착 무기를 싣고, 살 수 없는
        /// 후보를 가려내려고 소지금도 함께 보낸다.
        /// </summary>
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

        private static PlacedTablet Describe(StoneTablet tablet) => new PlacedTablet
        {
            DefinitionId = tablet.entityID,
            InstanceId = tablet.instanceID,
            Position = new GridPos(tablet.xIdx, tablet.yIdx),
            Rotation = tablet.rotation,
            IsApplied = tablet.IsApplied,
            Query = tablet.isCustomTablet ? tablet.GetQuery(tablet.instanceID) : null,
            ConditionQuery = tablet.isCustomTablet ? tablet.GetConditionQuery(tablet.instanceID) : null,
        };

        private static int LookupMatrix(SyncDictionary<ItemPosition, int> matrix, sbyte x, sbyte y)
        {
            foreach (var pair in matrix)
                if (pair.Key.x == x && pair.Key.y == y) return pair.Value;
            return 0;
        }

        private static string CellKey(int x, int y) => x + "," + y;
    }
}

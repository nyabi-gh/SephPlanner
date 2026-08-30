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
        public static GameSnapshot TryRead()
        {
            var avatar = FindLocalPlayer();
            if (avatar == null || avatar.Inventory == null) return null;

            return new GameSnapshot
            {
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                GameVersion = Application.version,
                IsMultiplayer = IsMultiplayerSession(),
                Inventory = ReadInventory(avatar.Inventory),
            };
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
                });
            }

            var seenTablets = new HashSet<int>();
            foreach (var pair in inv.stoneTablets)
            {
                var tablet = pair.Value;
                if (tablet == null || !seenTablets.Add(tablet.instanceID)) continue;

                state.Tablets.Add(new PlacedTablet
                {
                    DefinitionId = tablet.entityID,
                    InstanceId = tablet.instanceID,
                    Position = new GridPos(tablet.xIdx, tablet.yIdx),
                    Rotation = tablet.rotation,
                    IsApplied = tablet.IsApplied,
                });
            }

            // 게임이 계산해 둔 값. 우리 시뮬레이터를 대조하는 정답지로 쓴다.
            foreach (var pair in inv.levelMatrix)
                state.LevelMatrix[CellKey(pair.Key.x, pair.Key.y)] = pair.Value;

            foreach (var pair in inv.disableMatrix)
                if (pair.Value > 0) state.DisabledCells.Add(CellKey(pair.Key.x, pair.Key.y));

            return state;
        }

        private static int LookupMatrix(SyncDictionary<ItemPosition, int> matrix, sbyte x, sbyte y)
        {
            foreach (var pair in matrix)
                if (pair.Key.x == x && pair.Key.y == y) return pair.Value;
            return 0;
        }

        private static string CellKey(int x, int y) => x + "," + y;
    }
}

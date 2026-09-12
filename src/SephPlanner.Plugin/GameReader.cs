using System;
using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;
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
            if (avatar == null || avatar.Inventory == null || avatar.IsDead) return snapshot;

            snapshot.Run = ReadRun(avatar);

            var step = FrameCost.Now;
            snapshot.Inventory = ReadInventory(avatar.Inventory);
            FrameCost.Inventory.Add(step);

            if (includeRecommendations)
            {
                step = FrameCost.Now;
                snapshot.Mixer = ReadMixer();
                FrameCost.Mixer.Add(step);

                OfferReader.Fill(snapshot, avatar, offerRadius);
            }
            return snapshot;
        }

        public static string DumpInventory(float offerRadius)
        {
            var avatar = FindLocalPlayer();
            if (avatar?.Inventory == null) return null;

            var dump = InventoryDiagnostics.Write(avatar.Inventory, avatar, offerRadius);
            try
            {
                return dump + ", " + InventoryDiagnostics.WriteSnapshot(Read(offerRadius));
            }
            catch (Exception ex)
            {
                // 스냅샷을 못 남겼다고 덤프까지 없던 일이 되면 안 된다. 진단의 본체는 덤프다.
                return dump + " (스냅샷은 남기지 못했습니다: " + ex.Message + ")";
            }
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

            var issue = SimulationVerifier.Check(avatar.Inventory);
            return new RuntimeSimulationCheck
            {
                Status = issue == null
                    ? PlanVerificationStatus.Passed
                    : PlanVerificationStatus.Failed,
                Reason = issue == null ? "" : "시뮬레이터 불일치: " + issue,
            };
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

        private static MixerState ReadMixer()
        {
            // 쓸 수 있는 것을 찾으면서 아무거나 하나를 함께 기억해 두면, 다 썼을 때를 위해
            // 목록을 다시 훑지 않아도 된다.
            TabletMix any = null;
            foreach (var mixer in NetworkRegistry.All<TabletMix>())
            {
                if (mixer == null) continue;

                // 여럿이면 아직 쓸 수 있는 쪽이 답이다.
                if (!mixer.LocalUsed) return new MixerState { Cost = mixer.mixCost, Used = false };

                // 유니티 객체에는 ?? 를 쓰지 않는다. 파괴된 객체를 null 로 보는 것은 유니티가
                // 덮어쓴 == 뿐이라, ?? 로는 이미 파괴된 것을 붙들게 된다.
                if (any == null) any = mixer;
            }

            return any == null ? null : new MixerState { Cost = any.mixCost, Used = true };
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
                if (!seenItems.Add(instance.InstanceID)) continue;

                // 포션 벨트는 같은 딕셔너리를 쓰지만 격자가 아니라 y=100 줄에 산다
                // (x 는 0..numberOfPotionStorage-1). 석판 배치와는 아무 상관이 없는 자리이므로
                // 아이템 목록에 섞으면 "가방에 아이템 몇 개" 같은 셈이 조용히 어긋난다.
                // 진단이 필요할 때는 F10 덤프가 딕셔너리를 있는 그대로 보여준다.
                if (!grid.Contains(instance.XIdx, instance.YIdx)) continue;

                state.Items.Add(new PlacedItem
                {
                    DefinitionId = instance.EntityID,
                    InstanceId = instance.InstanceID,
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

            state.FixedEffects.AddRange(ReadFixedEffects(inv));

            return state;
        }

        /// <summary>
        /// 호스트는 고정 각인 원본을 읽고, 참가자는 동기화된 신비 좌표와 실제 효과 정의로 복원한다.
        /// </summary>
        internal static List<FixedEffectCell> ReadFixedEffects(GridInventory inv)
        {
            var effects = ReadFixedEngravings(inv);
            // 친타마니가 사라진 뒤에도 좌표에 남으며, 참가자에게도 동기화되는 보너스다.
            foreach (var pair in inv.dungeonTempLevels)
            {
                if (pair.Value == 0) continue;
                effects.Add(new FixedEffectCell
                {
                    Position = new GridPos(pair.Key.x, pair.Key.y),
                    Level = pair.Value,
                });
            }
            return effects;
        }

        private static List<FixedEffectCell> ReadFixedEngravings(GridInventory inv)
        {
            if (!NetworkServer.active)
            {
                if (!inv.currentSetEffectCount.TryGetValue("MYSTIC", out var count) || count <= 0)
                    return new List<FixedEffectCell>();
                var rule = ItemCatalog.LoadMysticRule();
                if (rule == null) return new List<FixedEffectCell>();
                var positions = new List<GridPos>();
                foreach (var position in inv.mysticPositions)
                    positions.Add(new GridPos(position.x, position.y));
                return MysticEngravings.Resolve(rule, count, positions, GridOf(inv));
            }
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            foreach (var engraving in inv.fixedEngravingsOnServer)
            {
                if (engraving == null) continue;
                foreach (var pair in engraving.fixedLevel) At(cells, pair.Key).Level += pair.Value;
                foreach (var pair in engraving.fixedDisable) At(cells, pair.Key).Disable += pair.Value;
                foreach (var pair in engraving.fixedIgnoreCriteria) At(cells, pair.Key).IgnoreCriteria += pair.Value;
                foreach (var pair in engraving.fixedMultiplyLevel) At(cells, pair.Key).Multiply += pair.Value;
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

        private static readonly System.Reflection.FieldInfo GrowthCounter =
            typeof(Charm_GrowthStatusInstance).GetField(
                "questCounter",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

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

        private static int LookupMatrix(SyncDictionary<ItemPosition, int> matrix, sbyte x, sbyte y)
        {
            foreach (var pair in matrix)
                if (pair.Key.x == x && pair.Key.y == y) return pair.Value;
            return 0;
        }

        private static string CellKey(int x, int y) => x + "," + y;
    }
}

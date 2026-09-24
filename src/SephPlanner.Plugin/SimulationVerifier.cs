using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Plugin
{
    internal sealed class RuntimeSimulationCheck
    {
        public PlanVerificationStatus Status { get; set; }
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// Core 의 석판 시뮬레이터를 게임의 실제 적용 결과와 대조한다.
    /// 게임이 이미 계산해 둔 <c>IsApplied</c>와 <c>EffectRange</c>, 그리고 <c>levelMatrix</c>가 정답지다.
    ///
    /// 못 읽은 입력이 있으면 어긋남이 아니라 <see cref="PlanVerificationStatus.Unavailable"/>이다.
    /// 우리가 눈을 감은 것과 게임이 다른 것을 같은 이름으로 부르면, 참가자 자리에서는 눈을 감은
    /// 쪽이 영원히 어긋남으로 올라간다.
    /// </summary>
    internal static class SimulationVerifier
    {
        public static int LastCheckedTablets { get; private set; }
        public static int LastFixedCells { get; private set; }

        public static RuntimeSimulationCheck Check(GridInventory inv)
        {
            var layer = FixedEffectLayer.Get(inv);
            var view = layer.View;
            LastCheckedTablets = view.Placements.Count;
            LastFixedCells = layer.All.Count;

            if (layer.Pending.Length > 0) return Result(PlanVerificationStatus.Unavailable, layer.Pending);
            if (layer.Blocker.Length > 0) return Result(PlanVerificationStatus.Failed, layer.Blocker);

            var result = TabletSimulator.Run(view.Placements, view.Occupancy, view.Grid, layer.All);

            for (var i = 0; i < view.Tablets.Count; i++)
            {
                if (result.Applied[i] != view.Tablets[i].IsApplied)
                {
                    return Result(PlanVerificationStatus.Failed,
                        $"석판 {view.Tablets[i].entityID} 적용 여부 {result.Applied[i]} != {view.Tablets[i].IsApplied}" +
                        Collapsed(inv, view, result));
                }

                if (!result.Applied[i]) continue;

                var difference = CompareEffects(view.Placements[i], view.Grid, view.Tablets[i]);
                if (difference != null)
                    return Result(PlanVerificationStatus.Failed, $"석판 {view.Tablets[i].entityID} {difference}");
            }

            var mismatch = CompareMatrices(inv, view, result);
            return mismatch == null
                ? Result(PlanVerificationStatus.Passed, "")
                : Result(PlanVerificationStatus.Failed, mismatch);
        }

        private static RuntimeSimulationCheck Result(PlanVerificationStatus status, string reason) =>
            new RuntimeSimulationCheck
            {
                Status = status,
                Reason = status == PlanVerificationStatus.Failed ? "시뮬레이터 불일치: " + reason : reason,
            };

        /// <summary>
        /// 어긋남에 덧붙일 한 마디. 게임은 맞바꿈·회전 끝에 레벨 행렬을 다시 만드는데, 그것이
        /// 도중에 예외로 멈추면 석판이 전부 비적용이 되고 칸 레벨이 0 으로 남는다(제보 5915982c,
        /// 2026-09-15). 그 상태에서 어긋남만 적으면 화면에는 "석판 2053 적용 여부 True != False"
        /// 만 떠서 무슨 일이 난 것인지 읽을 수 없다.
        ///
        /// <b>어긋남을 대신하지 않고 덧붙이기만 한다.</b> 여기에는 "조금 전까지 효과가 있었다" 는
        /// 근거가 없어서 - <c>ApplyPlanRoutine.Collapsed</c> 는 그것을 들고 있다 - 아직 아무 석판도
        /// 발동하지 않은 초반 판과 구별되지 않기 때문이다.
        /// </summary>
        private static string Collapsed(GridInventory inv, InventoryView view, SimulationResult result)
        {
            var applied = 0;
            for (var i = 0; i < view.Tablets.Count; i++)
            {
                if (view.Tablets[i].IsApplied) return "";
                if (result.Applied[i]) applied++;
            }
            if (applied == 0) return "";

            for (var index = 0; index < view.Grid.Storage; index++)
            {
                var position = view.Grid.ToPosition(index);
                if (LookupLevel(inv, (sbyte)position.X, (sbyte)position.Y) != 0) return "";
            }

            return $" - 게임이 계산한 가방 효과가 비어 있습니다(석판 {view.Tablets.Count}개가 전부 비적용, " +
                   "칸 레벨도 전부 0). 동기화 중이면 곧 사라지고, 계속 남으면 방을 나갔다 들어오세요.";
        }

        private static string CompareMatrices(GridInventory inv, InventoryView view, SimulationResult result)
        {
            for (var index = 0; index < view.Grid.Storage; index++)
            {
                var position = view.Grid.ToPosition(index);
                view.Enchants.TryGetValue(position, out var enchant);
                var ours = result.EffectiveLevel(position, enchant);
                var theirs = LookupLevel(inv, (sbyte)position.X, (sbyte)position.Y);
                if (ours != theirs)
                    return $"칸 {position} 레벨 {ours} != 게임 {theirs} (인챈트 {enchant})";

                // 레벨 0인 칸도 배수·제한 해제가 맞아야 옮긴 뒤의 결과를 믿을 수 있다.
                inv.multiplyLevelMatrix.TryGetValue(new ItemPosition((sbyte)position.X, (sbyte)position.Y), out var multiplier);
                if (result.MultiplierAt(position) != multiplier)
                    return $"칸 {position} 배수 {result.MultiplierAt(position)} != 게임 {multiplier}";
                inv.ignoreCriteriaMatrix.TryGetValue(new ItemPosition((sbyte)position.X, (sbyte)position.Y), out var ignore);
                if (result.IgnoreCriteriaAt(position) != ignore)
                    return $"칸 {position} 제한 해제 {result.IgnoreCriteriaAt(position)} != 게임 {ignore}";

                var disabled = LookupDisabled(inv, (sbyte)position.X, (sbyte)position.Y);
                if (result.IsDisabled(position) != disabled)
                    return $"칸 {position} 비활성 {result.IsDisabled(position)} != 게임 {disabled}";
            }
            return null;
        }

        private static int LookupLevel(GridInventory inv, sbyte x, sbyte y)
        {
            inv.levelMatrix.TryGetValue(new ItemPosition(x, y), out var level);
            return level;
        }

        private static bool LookupDisabled(GridInventory inv, sbyte x, sbyte y)
        {
            inv.disableMatrix.TryGetValue(new ItemPosition(x, y), out var disabled);
            return disabled > 0;
        }

        private static string CompareEffects(TabletPlacement placement, GridSpec grid, StoneTablet tablet)
        {
            var cells = TabletQuery.Parse(placement.Query, grid, placement.Position, placement.Rotation);
            if (cells.Count != tablet.EffectRange.Count)
                return $"효과 칸 수 {cells.Count} != {tablet.EffectRange.Count}";

            for (var i = 0; i < cells.Count; i++)
            {
                var expected = tablet.EffectRange[i];
                var (kind, levelParam) = QueryValue.ReadEffect(cells[i].Value);

                if (cells[i].Position.X != expected.position.x || cells[i].Position.Y != expected.position.y)
                    return $"효과 [{i}] 위치 불일치";
                if ((int)kind != (int)expected.effectType)
                    return $"효과 [{i}] 종류 {kind} != {expected.effectType}";
                if (levelParam != expected.levelParam)
                    return $"효과 [{i}] 수치 {levelParam} != {expected.levelParam}";
            }
            return null;
        }
    }
}

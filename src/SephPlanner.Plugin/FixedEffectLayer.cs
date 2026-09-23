using System.Collections.Generic;
using Mirror;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;
using UnityEngine;

namespace SephPlanner.Plugin
{
    internal sealed class FixedEffectLayerState
    {
        public InventoryView View { get; set; }
        public IReadOnlyList<FixedEffectCell> Cells { get; set; } = new List<FixedEffectCell>();

        /// <summary>계산을 믿을 수 없는 이유. 우리 쪽 어긋남이라 알릴 값이다.</summary>
        public string Blocker { get; set; } = "";

        /// <summary>아직 판단할 수 없는 이유. 잠시 뒤 풀리므로 알리지 않는다.</summary>
        public string Pending { get; set; } = "";
    }

    /// <summary>
    /// 칸에 박힌 고정 효과를 구한다. 호스트는 <c>fixedEngravingsOnServer</c> 원본을 읽고,
    /// 참가자는 게임 행렬에서 되뺀다(<see cref="FixedEffectResidual"/>).
    ///
    /// 호스트에서는 원본과 되뺀 값을 대조해 되빼기 자체를 검증한다.
    /// </summary>
    internal static class FixedEffectLayer
    {
        private static readonly FixedEffectTracker Tracker = new FixedEffectTracker();
        private static FixedEffectLayerState _cached;
        private static GridInventory _inventory;
        private static GridSpec _grid;
        private static int _frame = -1;

        internal static void Forget()
        {
            Tracker.Reset();
            _cached = null;
            _inventory = null;
            _frame = -1;
        }

        /// <summary>
        /// 프레임 캐시만 버리고 추적 기록은 둔다. 호스트의 자동 배치는 한 프레임 안에 끝나므로,
        /// 그 뒤 같은 프레임의 검증이 배치 전 뷰를 받지 않게 쓰기가 끝나면 부른다.
        /// </summary>
        internal static void Invalidate()
        {
            _cached = null;
            _frame = -1;
        }

        internal static FixedEffectLayerState Get(GridInventory inv)
        {
            if (_cached != null && _frame == Time.frameCount && ReferenceEquals(_inventory, inv)) return _cached;

            var grid = GameReader.GridOf(inv);
            if (!ReferenceEquals(_inventory, inv) ||
                grid.Width != _grid.Width || grid.Height != _grid.Height || grid.Storage != _grid.Storage)
            {
                Tracker.Reset();
            }

            _inventory = inv;
            _grid = grid;
            _frame = Time.frameCount;
            _cached = Build(inv, InventoryView.Of(inv));
            return _cached;
        }

        private static FixedEffectLayerState Build(GridInventory inv, InventoryView view)
        {
            var state = new FixedEffectLayerState { View = view };

            var observed = TabletSimulator.Run(view.Placements, view.Occupancy, view.Grid);
            var residual = FixedEffectResidual.Extract(view.Grid, observed, view.Matrices(inv));
            Tracker.Observe(residual, view.Sources, view.Arrangement);

            if (NetworkServer.active)
            {
                state.Cells = HostFixedEffects(inv);
                if (residual.Status == FixedEffectResidualStatus.Extracted &&
                    !FixedEffectResidual.Same(state.Cells, residual.Cells))
                {
                    state.Blocker = "고정 효과 원본과 게임 행렬에서 되뺀 값이 다릅니다.";
                }
                return state;
            }

            state.Cells = Tracker.Layer;
            if (view.Severed > 0)
            {
                state.Pending = $"석판·각인 참조 {view.Severed}개를 읽지 못했습니다. 방에 재접속하면 다시 읽습니다.";
                return state;
            }
            if (Tracker.Contradicted) state.Blocker = Tracker.Reason;
            else if (!Tracker.Trustworthy) state.Pending = Tracker.Reason;
            return state;
        }

        private static List<FixedEffectCell> HostFixedEffects(GridInventory inv)
        {
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            foreach (var engraving in inv.fixedEngravingsOnServer)
            {
                if (engraving == null) continue;
                foreach (var pair in engraving.fixedLevel) At(cells, pair.Key).Level += pair.Value;
                foreach (var pair in engraving.fixedDisable) At(cells, pair.Key).Disable += pair.Value;
                foreach (var pair in engraving.fixedIgnoreCriteria) At(cells, pair.Key).IgnoreCriteria += pair.Value;
                foreach (var pair in engraving.fixedMultiplyLevel) At(cells, pair.Key).Multiply += pair.Value;
            }

            // 친타마니가 사라진 뒤에도 좌표에 남는 보너스다.
            foreach (var pair in inv.dungeonTempLevels)
            {
                if (pair.Value == 0) continue;
                At(cells, pair.Key).Level += pair.Value;
            }

            var grid = GameReader.GridOf(inv);
            var result = new List<FixedEffectCell>();
            foreach (var cell in cells.Values)
                if (grid.Contains(cell.Position) && !IsEmpty(cell)) result.Add(cell);
            return result;
        }

        private static bool IsEmpty(FixedEffectCell cell) =>
            cell.Level == 0 && cell.Multiply == 0 && cell.Disable == 0 && cell.IgnoreCriteria == 0;

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
    }
}

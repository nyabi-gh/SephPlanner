using System;
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

        /// <summary>배치와 무관한 고정 효과. 콤보 각인은 빠져 있다.</summary>
        public IReadOnlyList<FixedEffectCell> Cells { get; set; } = new List<FixedEffectCell>();

        /// <summary>콤보 각인 규칙. 읽지 못했으면 null 이고, 그때는 그 각인이 <see cref="Cells"/> 에 섞여 있다.</summary>
        public ComboEngravingRule ComboEngraving { get; set; }

        /// <summary>지금 콤보 수에서 살아 있는 콤보 각인 몫.</summary>
        public IReadOnlyList<FixedEffectCell> Engraved { get; set; } = new List<FixedEffectCell>();

        /// <summary>지금 칸에 걸린 것 전부. 게임 행렬과 대조할 때 쓴다.</summary>
        public IReadOnlyList<FixedEffectCell> All { get; set; } = new List<FixedEffectCell>();

        /// <summary>계산을 믿을 수 없는 이유. 우리 쪽 어긋남이라 알릴 값이다.</summary>
        public string Blocker { get; set; } = "";

        /// <summary>아직 판단할 수 없는 이유. 잠시 뒤 풀리므로 알리지 않는다.</summary>
        public string Pending { get; set; } = "";
    }

    /// <summary>
    /// 칸에 박힌 고정 효과를 구한다. 호스트는 <c>fixedEngravingsOnServer</c> 원본을 읽고,
    /// 참가자는 게임 행렬에서 되뺀다(<see cref="FixedEffectResidual"/>).
    ///
    /// 신비 콤보 각인은 따로 든다. 게임은 가방에 쓸 때마다 콤보를 껐다 켜며 그 각인을 지우고
    /// 다시 심으므로, 배치가 신비 수를 바꾸면 칸도 따라 바뀐다. 게임도 판을 저장할 때 그 각인
    /// (<c>createdByExternalSystem</c>)만은 빼고 적는다. 양쪽 모두 규칙과 동기화된 좌표로 지금
    /// 몫을 풀어 고정 층에서 뺀다.
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

        private static ComboEngravingTemplate _template;
        private static bool _templateUnsupported;

        /// <summary>
        /// 추적기의 셈이 바뀐 까닭을 남길 곳. 진단 묶음에 들어가는 것은 플러그인 로그뿐이다.
        /// </summary>
        internal static Action<object> Log { get; set; }

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

            var rule = ComboRule(inv);
            state.ComboEngraving = rule;
            if (rule != null)
            {
                inv.currentSetEffectCount.TryGetValue(rule.Category, out var count);
                state.Engraved = ComboEngravings.Cells(rule, ComboEngravings.StageAt(rule, count), view.Grid);
            }

            var observed = TabletSimulator.Run(view.Placements, view.Occupancy, view.Grid);
            var residual = ComboEngravings.Without(
                FixedEffectResidual.Extract(view.Grid, observed, view.Matrices(inv)), state.Engraved);
            Tracker.Observe(residual, view.Sources, view.Arrangement);
            if (Tracker.Note.Length > 0) Log?.Invoke(Tracker.Note);

            if (NetworkServer.active)
            {
                state.Cells = HostFixedEffects(inv, rule != null);
                state.All = ComboEngravings.Combine(state.Cells, state.Engraved);
                if (residual.Status == FixedEffectResidualStatus.Extracted &&
                    !FixedEffectResidual.Same(state.Cells, residual.Cells))
                {
                    state.Blocker = "고정 효과 원본과 게임 행렬에서 되뺀 값이 다릅니다.";
                }
                return state;
            }

            state.Cells = Tracker.Layer;
            state.All = ComboEngravings.Combine(state.Cells, state.Engraved);
            if (view.Severed > 0)
            {
                state.Pending = $"석판·각인 참조 {view.Severed}개를 읽지 못했습니다. 방에 재접속하면 다시 읽습니다.";
                return state;
            }
            if (Tracker.Contradicted) state.Blocker = Tracker.Reason;
            else if (!Tracker.Trustworthy) state.Pending = Tracker.Reason;
            return state;
        }

        private static List<FixedEffectCell> HostFixedEffects(GridInventory inv, bool withoutComboEngravings)
        {
            var cells = new Dictionary<GridPos, FixedEffectCell>();
            foreach (var engraving in inv.fixedEngravingsOnServer)
            {
                if (engraving == null) continue;
                if (withoutComboEngravings && engraving.createdByExternalSystem) continue;
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

        /// <summary>
        /// 이 판의 콤보 각인 규칙. 문턱과 석판은 프리팹 값이라 한 번만 읽고, 좌표는 판마다 달라
        /// 매번 읽는다. 프리팹 값은 코드의 기본값과 다르다(신비는 2 에 1칸, 4 에 2칸 더).
        /// </summary>
        private static ComboEngravingRule ComboRule(GridInventory inv)
        {
            var template = Template();
            if (template == null) return null;

            var rule = new ComboEngravingRule { Category = MysticCategory, Query = template.Query };
            rule.Tiers.AddRange(template.Tiers);
            foreach (var position in inv.mysticPositions) rule.Positions.Add(new GridPos(position.x, position.y));
            return rule;
        }

        private static ComboEngravingTemplate Template()
        {
            if (_template != null || _templateUnsupported) return _template;

            var category = ItemDatabase.FindItemCategory(MysticCategory);
            if (category == null) return null;

            var effect = category.comboEffectPrefab != null
                ? category.comboEffectPrefab.GetComponent<ComboEffect_Mystic>() : null;
            var entity = effect != null ? ItemDatabase.FindItemById(effect.stoneTabletEntityID) : null;
            var tablet = entity != null && entity.resourcePrefab != null
                ? entity.resourcePrefab.GetComponent<StoneTablet>() : null;

            // 조건이 있는 각인은 심는 순간의 점유로 갈려 규칙으로 풀 수 없고, 사용자 석판은
            // 인스턴스마다 질의가 다르다. 그런 게임 버전에서는 예전처럼 고정 층에 섞어 둔다.
            if (tablet == null || tablet.isCustomTablet || !string.IsNullOrEmpty(tablet.conditionQuery))
            {
                _templateUnsupported = true;
                Log?.Invoke("신비 콤보 각인 규칙을 읽지 못했습니다. 그 각인은 고정 효과로 다룹니다.");
                return null;
            }

            _template = new ComboEngravingTemplate { Query = tablet.query ?? "" };
            _template.Tiers.Add(new ComboEngravingTier { Threshold = effect.first, Count = effect.firstEngravingCount });
            _template.Tiers.Add(new ComboEngravingTier { Threshold = effect.second, Count = effect.secondEngravingCount });
            return _template;
        }

        private const string MysticCategory = "MYSTIC";

        private sealed class ComboEngravingTemplate
        {
            public string Query = "";
            public readonly List<ComboEngravingTier> Tiers = new List<ComboEngravingTier>();
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

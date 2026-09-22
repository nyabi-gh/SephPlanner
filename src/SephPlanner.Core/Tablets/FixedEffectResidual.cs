using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>게임이 들고 있는 행렬 한 벌. 칸 하나씩 읽는다.</summary>
    public readonly struct GameEffectMatrices
    {
        private readonly Func<GridPos, int> _level;
        private readonly Func<GridPos, int> _multiply;
        private readonly Func<GridPos, int> _disable;
        private readonly Func<GridPos, int> _ignoreCriteria;
        private readonly Func<GridPos, int> _enchant;

        public GameEffectMatrices(
            Func<GridPos, int> level, Func<GridPos, int> multiply, Func<GridPos, int> disable,
            Func<GridPos, int> ignoreCriteria, Func<GridPos, int> enchant)
        {
            _level = level;
            _multiply = multiply;
            _disable = disable;
            _ignoreCriteria = ignoreCriteria;
            _enchant = enchant;
        }

        public int Level(GridPos position) => _level(position);
        public int Multiply(GridPos position) => _multiply(position);
        public int Disable(GridPos position) => _disable(position);
        public int IgnoreCriteria(GridPos position) => _ignoreCriteria(position);
        public int Enchant(GridPos position) => _enchant(position);
    }

    public enum FixedEffectResidualStatus
    {
        Extracted,

        /// <summary>게임 행렬이 아직 정리되지 않아 뽑을 수 없다.</summary>
        Unsettled,

        /// <summary>우리 시뮬레이션이 게임에 없는 효과를 만들었다. 고정 효과로 설명할 수 없다.</summary>
        Overproduced,
    }

    public sealed class FixedEffectResidualResult
    {
        public FixedEffectResidualStatus Status { get; set; }
        public List<FixedEffectCell> Cells { get; set; } = new List<FixedEffectCell>();
        public string Reason { get; set; } = "";
    }

    /// <summary>
    /// 게임 행렬에서 우리가 설명할 수 있는 몫을 빼고 남는 칸 효과를 뽑는다.
    ///
    /// <para>
    /// 참가자 자리에서는 <c>GridInventory.fixedEngravingsOnServer</c>를 읽을 길이 아예 없다 -
    /// 그 목록은 동기화 대상이 아니고, 석판을 각인하면 석판 자체가 가방에서 사라지며
    /// (<c>CmdCreateFixedEngravingFromInventory</c>), 세피라이트 보상을 바로 각인하는 길은
    /// 석판이 가방에 들어오지도 않는다. 그래서 <b>원인을 읽는 대신 결과에서 되뺀다</b>.
    /// </para>
    ///
    /// <para>
    /// 되뺀 값은 정의상 행렬과 맞으므로 그것만으로는 진짜 고정 효과인지 우리 시뮬레이터의
    /// 오차인지 구분되지 않는다. 그 판정은 <see cref="FixedEffectTracker"/>가 맡는다.
    /// </para>
    ///
    /// 셈 순서는 <c>GridInventory.ReleasePermission</c>과 같다 -
    /// <c>(석판 몫 + 고정 효과 + 인챈트) × 배수</c>.
    /// </summary>
    public static class FixedEffectResidual
    {
        public static FixedEffectResidualResult Extract(
            GridSpec grid, SimulationResult observed, GameEffectMatrices game)
        {
            var cells = new List<FixedEffectCell>();
            for (var index = 0; index < grid.Storage; index++)
            {
                var position = grid.ToPosition(index);

                var multiplier = game.Multiply(position);
                var multiply = multiplier - observed.MultiplierAt(position);
                var disable = game.Disable(position) - observed.DisableAt(position);
                var ignoreCriteria = game.IgnoreCriteria(position) - observed.IgnoreCriteriaAt(position);

                // 고정 효과는 더하기만 한다. 음수는 남은 몫으로 덮을 일이 아니다.
                if (multiply < 0 || disable < 0 || ignoreCriteria < 0)
                {
                    return new FixedEffectResidualResult
                    {
                        Status = FixedEffectResidualStatus.Overproduced,
                        Reason = $"칸 {position} 의 배수·비활성·제한 해제가 게임보다 많습니다" +
                                 $" (배수 {multiply} 비활성 {disable} 제한 해제 {ignoreCriteria})",
                    };
                }

                var total = game.Level(position);
                if (multiplier != 0)
                {
                    if (total % multiplier != 0)
                    {
                        return new FixedEffectResidualResult
                        {
                            Status = FixedEffectResidualStatus.Unsettled,
                            Reason = $"칸 {position} 의 레벨 {total} 이 배수 {multiplier} 로 나누어떨어지지 않습니다",
                        };
                    }
                    total /= multiplier;
                }

                var level = total - observed.LevelAt(position) - game.Enchant(position);
                if (level == 0 && multiply == 0 && disable == 0 && ignoreCriteria == 0) continue;

                cells.Add(new FixedEffectCell
                {
                    Position = position,
                    Level = level,
                    Multiply = multiply,
                    Disable = disable,
                    IgnoreCriteria = ignoreCriteria,
                });
            }

            return new FixedEffectResidualResult
            {
                Status = FixedEffectResidualStatus.Extracted,
                Cells = cells,
            };
        }

        public static bool Same(
            IReadOnlyList<FixedEffectCell>? left, IReadOnlyList<FixedEffectCell>? right)
        {
            var a = left ?? Array.Empty<FixedEffectCell>();
            var b = right ?? Array.Empty<FixedEffectCell>();
            if (a.Count != b.Count) return false;

            var lookup = new Dictionary<GridPos, FixedEffectCell>(a.Count);
            foreach (var cell in a) lookup[cell.Position] = cell;
            foreach (var cell in b)
            {
                if (!lookup.TryGetValue(cell.Position, out var other)) return false;
                if (other.Level != cell.Level || other.Multiply != cell.Multiply ||
                    other.Disable != cell.Disable || other.IgnoreCriteria != cell.IgnoreCriteria) return false;
            }
            return true;
        }

        public static string Describe(IReadOnlyList<FixedEffectCell>? cells, int limit = 12)
        {
            if (cells == null || cells.Count == 0) return "없음";

            var text = new System.Text.StringBuilder();
            for (var i = 0; i < cells.Count && i < limit; i++)
            {
                if (i > 0) text.Append(' ');
                var cell = cells[i];
                text.Append(cell.Position).Append("=레벨 ").Append(cell.Level);
                if (cell.Multiply != 0) text.Append("/배수 ").Append(cell.Multiply);
                if (cell.Disable != 0) text.Append("/비활성 ").Append(cell.Disable);
                if (cell.IgnoreCriteria != 0) text.Append("/제한 해제 ").Append(cell.IgnoreCriteria);
            }
            if (cells.Count > limit) text.Append(" 외 ").Append(cells.Count - limit).Append('칸');
            return text.ToString();
        }
    }
}

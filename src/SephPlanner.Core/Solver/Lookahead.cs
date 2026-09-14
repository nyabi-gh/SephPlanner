using System.Collections.Generic;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 다음 칸이 열린 판. 되돌릴 수 없는 선택의 조언이 여기서 한 번 더 재어진다.
    ///
    /// <b>왜 조언만인가.</b> 배치는 폴링마다 다시 풀리고 <c>F8</c>이 옮겨 주므로 되돌릴 수 있다.
    /// 미래를 위해 지금 점수를 깎아 두면 잃는 것은 지금 전투력이고 얻는 것은 도구가 대신 하는
    /// 이동 횟수뿐이다. 반면 후보 획득·버리기·석판 합성은 한 번 하면 끝이라, 잠긴 칸으로 뻗는
    /// 석판이 "지금 가방에는 이득이 없다"는 이유로 밀리면 그 손해는 되돌릴 수 없다.
    /// 판단의 근거는 docs/notes/LOOKAHEAD-2026-09-14.md 에 있다.
    ///
    /// <b>재야 할 상수가 없다.</b> 칸은 인덱스 순서로 열리므로 다음 칸이 어디인지는 게임 사실이고,
    /// 한 칸씩 연다는 것도 게임 코드에서 읽은 것이다(<see cref="GridSpec.OpeningStep"/>).
    /// "지금 점수 얼마를 나중 가능성 얼마와 바꾸느냐"의 비율은 쓰지 않는다.
    ///
    /// <b>배치와 점수 표시는 건드리지 않는다.</b> 여기서 나오는 것은 줄 세우기에 쓰는 값과 화면에
    /// 함께 적는 숫자뿐이다. 아직 열리지 않은 칸에 무엇을 놓으라고 하는 일은 없어야 한다.
    ///
    /// <b>캐시는 저절로 갈린다.</b> <see cref="LayoutCache"/>의 열쇠가 <c>Grid.Storage</c>를 세므로
    /// 늘어난 판의 빔은 지금 판의 빔과 다른 칸에 들어간다. 계획 하나에 이 객체를 하나만 두면
    /// 조언 셋이 그 늘어난 판의 빔과 기준 배치를 나눠 쓴다.
    /// </summary>
    public sealed class Lookahead
    {
        private readonly PlacementProblem? _grown;

        public Lookahead(PlacementProblem problem)
        {
            var grid = problem.Grid.Grown(GridSpec.OpeningStep);
            _grown = grid.Storage == problem.Grid.Storage ? null : Regrid(problem, grid);
        }

        /// <summary>더 열릴 칸이 있는가. 가방이 이미 다 열렸으면 앞을 볼 것이 없다.</summary>
        public bool Available => _grown is not null;

        /// <summary>늘어난 판에서 아무것도 집지 않은 기준 배치. 증가분은 이것과 견줘야 뜻이 있다.</summary>
        public Arrangement? Baseline(LayoutCache layouts, SolverOptions options) =>
            _grown is null ? null : layouts.Baseline(_grown, options);

        /// <summary>
        /// 지금 판으로 지은 가상 배치를 늘어난 판으로 옮긴다. 무엇을 집었고 무엇이 밀려났는지는
        /// 그대로 둔다 - <b>그 선택은 지금 하는 것이고 되돌릴 수 없기 때문이다.</b> 가방이 차서
        /// 하나를 버려야 했다면, 칸이 하나 열린 뒤에도 버린 것은 돌아오지 않는다.
        /// </summary>
        public PlacementProblem? Grow(PlacementProblem trial) =>
            _grown is null ? null : Regrid(trial, _grown.Grid);

        /// <summary>
        /// 늘어난 판에서의 증가분. 풀지 못했거나 원래 가방의 활성 보호를 깨면 <c>null</c> 이다 -
        /// 그때는 부르는 쪽이 지금 가방의 값을 그대로 쓴다.
        /// </summary>
        /// <param name="yardstick">
        /// 견줄 배치 후보. <c>null</c> 이면 늘어난 판의 배치 후보를 새로 찾는다.
        ///
        /// <b>부르는 쪽은 지금 판에서 이미 찾아 둔 빔을 넘긴다.</b> 칸은 늘기만 하므로 그 자리들은
        /// 늘어난 판에서도 전부 유효하고, 앞보기의 핵심인 "잠긴 칸으로 뻗던 석판"은 <b>자리를
        /// 옮기지 않아도</b> 그 칸이 열리는 것만으로 값이 잡힌다. 빔은 배치 하나가 아니라 후보
        /// 백수십 개의 묶음이라 회전이 다른 자리들도 함께 들어 있다.
        ///
        /// 넘기지 않으면 조언마다 탐색이 한 번씩 더 돈다 - 합성 추천이 있는 판에서 재계산이
        /// 두 배 반이 됐다. 대신 잃는 것은 <b>늘어난 판에서만 좋은 자리가 지금 판의 빔에서
        /// 잘려 나간 경우</b>이고, 이는 짐작 단계가 이미 지고 있는 종류의 손실이다.
        /// </param>
        public double? GainOf(
            PlacementProblem trial, LayoutCache layouts, SolverOptions options,
            IReadOnlyList<List<TabletPlacement>>? yardstick = null)
        {
            var solved = Solve(trial, layouts, options, yardstick);
            var baseline = Baseline(layouts, options);
            return solved is null || baseline is null ? null : solved.Score - baseline.Score;
        }

        /// <summary>
        /// 늘어난 판에서 이 가상 배치를 푼다. 원래 가방의 활성 보호를 깨는 답은 돌려주지 않는다 -
        /// 지금 판에서 걸러 내는 것과 같은 잣대다.
        /// </summary>
        public Arrangement? Solve(
            PlacementProblem trial, LayoutCache layouts, SolverOptions options,
            IReadOnlyList<List<TabletPlacement>>? yardstick = null)
        {
            var grown = Grow(trial);
            var baseline = Baseline(layouts, options);
            if (grown is null || baseline is null) return null;

            var solved = PlacementSolver.EvaluateLayouts(
                grown, yardstick ?? layouts.Of(grown, options), options);
            return solved.UnretainedCharms.Count > 0 || !ActivationPolicy.AllowsTransition(baseline, solved)
                ? null
                : solved;
        }

        private static PlacementProblem Regrid(PlacementProblem problem, GridSpec grid)
        {
            var clone = OfferAdvisor.Clone(problem);
            clone.Grid = grid;
            return clone;
        }
    }
}

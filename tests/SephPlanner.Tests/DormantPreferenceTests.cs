using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 연동 무기를 안 들어 꺼져 있는 아티팩트에 강화 우선을 지정했을 때. 별은 우리가 추정한 값이
/// 아니라 사용자가 넣은 바깥 정보이므로, 꺼져 있어도 지정한 자리를 받는다.
/// </summary>
public class DormantPreferenceTests
{
    private static CharmDefinition Definition(int entityId) => new()
    {
        EntityId = entityId,
        MaxLevel = 3,
        Behavior = "Charm_StatusInstance",
        StatWorthCoverageKnown = true,
        StatWorthByLevel = { 1, 2, 3, 4 },
        StatBenefitByLevel = { 1, 2, 3, 4 },
        StatPenaltyByLevel = { 0, 0, 0, 0 },
    };

    /// <summary>낮은 칸 하나와 높은 칸 하나만 있는 판. 누가 높은 칸을 받는지가 전부다.</summary>
    private static PlacementProblem Board(double dormantWeight)
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(2, 1, 2),
            FixedEffects = { new FixedEffectCell { Position = new GridPos(1, 0), Level = 3 } },
        };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = Definition(1),
            IsDormant = true,
            Weight = dormantWeight,
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Definition(2) });
        return problem;
    }

    [Fact]
    public void AStarredDormantCharmTakesTheGoodCell()
    {
        var solved = PlacementSolver.Solve(Board(dormantWeight: 10));

        Assert.Equal(new GridPos(1, 0), solved.CharmPositions[1]);
        Assert.Contains(1, solved.InactiveCharms);
    }

    [Fact]
    public void WithoutAStarTheWorkingCharmKeepsTheGoodCell()
    {
        // 지정이 없으면 꺼진 것에 좋은 칸을 줄 이유가 없다. 지금 쓸 수 있는 쪽이 가져간다.
        var solved = PlacementSolver.Solve(Board(dormantWeight: 1));

        Assert.Equal(new GridPos(1, 0), solved.CharmPositions[2]);
    }

    [Fact]
    public void TheReportedScoreDoesNotCountWhatADormantCharmWouldGive()
    {
        // 자리는 잡아 주되 점수는 정직해야 한다. 지금 그 아티팩트가 주는 것은 여전히 0 이다.
        var starred = PlacementSolver.Solve(Board(dormantWeight: 10));
        var plain = PlacementSolver.Solve(Board(dormantWeight: 1));

        Assert.Equal(CharmWorth.Resolve(Definition(2)).At(0), starred.Score, 6);
        Assert.Equal(CharmWorth.Resolve(Definition(2)).At(3), plain.Score, 6);
    }
}

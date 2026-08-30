using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 아티팩트가 적으면 여러 석판이 똑같이 최대치를 뽑아내 증가분이 전부 같아진다. 실제 런에서
/// 차양·삼두·파도가 모두 +3으로 뜬 적이 있다. 그때 무엇이 다른지 알려줄 수 있어야 한다.
/// </summary>
public class OfferReachTests
{
    // 게임에서 그대로 가져온 질의다.
    private static OfferCandidate Shade() => Tablet("차양", "BOTTOM 1\n", "TOP PLACED");
    private static OfferCandidate Threeheaded() => Tablet("삼두", "UP 1\nLEFT 1\nRIGHT 1", "");
    private static OfferCandidate Wave() => Tablet("파도", "DIAUPRIGHT 3\nUP -1\nRIGHT -1", "");

    private static OfferCandidate Tablet(string name, string query, string condition) => new()
    {
        Kind = "tablet",
        Name = name,
        Tablet = new TabletDefinition { Id = name, Query = query, ConditionQuery = condition },
    };

    /// <summary>실제 상황과 같게 24칸에 아티팩트 셋.</summary>
    private static PlacementProblem Problem()
    {
        var problem = new PlacementProblem { Grid = GridSpec.WithStorage(24) };
        for (var i = 0; i < 3; i++)
            problem.Charms.Add(new CharmSlot { InstanceId = 10 + i, Definition = new CharmDefinition { MaxLevel = 5 } });
        return problem;
    }

    [Fact]
    public void ThreeVeryDifferentTabletsCanAllGainTheSame()
    {
        var problem = Problem();
        var advice = OfferAdvisor.Rank(
            problem, PlacementSolver.Solve(problem).Score, new[] { Threeheaded(), Wave(), Shade() }, gold: 0);

        // 증가분만 보면 우열이 없다. 아티팩트가 셋뿐이라 셋 다 +3 이 한계다.
        Assert.All(advice, entry => Assert.Equal(3, entry.Gain, 2));
    }

    [Fact]
    public void ReachTellsThemApartAndOrdersThem()
    {
        var problem = Problem();
        var advice = OfferAdvisor.Rank(
            problem, PlacementSolver.Solve(problem).Score, new[] { Threeheaded(), Wave(), Shade() }, gold: 0);

        // 차양은 여섯 칸을 올린다. 아티팩트가 늘면 계속 커지는 쪽이다.
        Assert.Equal("차양", advice[0].Candidate.Name);
        Assert.Equal(6, advice[0].Effect.RaisedCells);
        Assert.Equal(6, advice[0].Effect.RaisedTotal);

        // 삼두는 세 칸이 한계다.
        Assert.Equal("삼두", advice[1].Candidate.Name);
        Assert.Equal(3, advice[1].Effect.RaisedCells);

        // 파도는 한 칸에 몰아주고 두 칸을 깎는다. 여력이 가장 작다.
        Assert.Equal("파도", advice[2].Candidate.Name);
        Assert.Equal(3, advice[2].Effect.RaisedTotal);
        Assert.True(advice[2].Effect.LoweredCells > 0);
    }
}

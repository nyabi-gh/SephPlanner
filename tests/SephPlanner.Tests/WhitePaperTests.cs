using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class WhitePaperTests
{
    /// <summary>1x3 격자. 잉걸불 둘이 양끝에 있고 하얀 종이가 남는다.</summary>
    private static PlacementProblem Sandwich(bool withCombos)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(3, 1, 3) };

        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { MaxLevel = 5, Categories = { "EMBER" } },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition { MaxLevel = 5, Categories = { "EMBER" } },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 3,
            Definition = new CharmDefinition { MaxLevel = 5, Behavior = "Charm_WhitePaper" },
        });

        // 양끝을 지금 자리로 두어 안정 보너스가 그 자리를 지키게 한다. 종이는 가운데만 남는다.
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[2] = new GridPos(2, 0);

        if (withCombos)
        {
            problem.ComboCounts = new Dictionary<string, int> { ["EMBER"] = 2 };
            problem.Combos = id => id == "EMBER"
                ? new ComboDefinition { Id = "EMBER", Thresholds = { 2, 5, 8 } }
                : null;
        }
        return problem;
    }

    [Fact]
    public void APaperBetweenASharedCategoryPairIsWorthAComboStep()
    {
        var with = PlacementSolver.Solve(Sandwich(withCombos: true));
        var without = PlacementSolver.Solve(Sandwich(withCombos: false));

        // 게임의 Charm_WhitePaper 는 양옆이 공유하는 카테고리를 물려받아 콤보에 +1 을 보탠다.
        // 잉걸불 2개에서 3개째(다음 임계값 5를 향한 진행)가 되므로 진행 가치만큼 점수가 오른다.
        Assert.Equal(new GridPos(1, 0), with.CharmPositions[3]);
        Assert.Equal(Worth.ComboProgress, with.Score - without.Score, 3);
    }
}

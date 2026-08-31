using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 조화의 수정(<c>Charm_NearLevelDamage</c>)은 이웃 여덟 칸 아티팩트의 유효 레벨 합에 비례해
/// 전체 피해를 올린다. 자기 레벨만 보는 점수로는 어디에 두든 똑같아 보이므로, 자리 가치가
/// 배치에 실제로 반영되는지 고정해 둔다.
/// </summary>
public class NeighborValueTests
{
    private const int Crystal = 1;

    /// <summary>세 칸만 열린 가방. 가운데 칸만 이웃이 둘이라 우열이 분명하다.</summary>
    private static GridSpec ThreeCells => new(6, 7, 3);

    /// <summary>인챈트로 레벨을 준다. 석판 없이도 모든 칸의 유효 레벨이 같아져 자리 값만 남는다.</summary>
    private static CharmSlot Plain(int instanceId) => new()
    {
        InstanceId = instanceId,
        Enchant = 2,
        Definition = new CharmDefinition { Id = "C" + instanceId, MaxLevel = 5 },
    };

    private static CharmSlot HarmonyCrystal(bool modelled) => new()
    {
        InstanceId = Crystal,
        Enchant = 2,
        Definition = new CharmDefinition
        {
            Id = "Crystal",
            MaxLevel = 3,
            Behavior = "Charm_NearLevelDamage",
            // 게임 값 그대로: 자기 레벨로 색인하는 배수 표.
            NeighborLevelBonus = modelled ? new List<double> { 0, 1, 1, 2 } : new List<double>(),
        },
    };

    private static PlacementProblem Problem(bool modelled)
    {
        var problem = new PlacementProblem { Grid = ThreeCells };
        problem.Charms.Add(HarmonyCrystal(modelled));
        problem.Charms.Add(Plain(10));
        problem.Charms.Add(Plain(11));
        return problem;
    }

    [Fact]
    public void ItTakesTheCellWithMoreNeighboursAroundIt()
    {
        var arrangement = PlacementSolver.Solve(Problem(modelled: true));

        // 끝 칸은 이웃이 하나, 가운데는 둘이다. 나머지 값은 셋 다 같으므로 가운데여야 한다.
        Assert.Equal(new GridPos(1, 0), arrangement.CharmPositions[Crystal]);
    }

    [Fact]
    public void TheNeighbourSumIsWhatTheScoreGains()
    {
        var modelled = PlacementSolver.Solve(Problem(modelled: true)).Score;
        var ignored = PlacementSolver.Solve(Problem(modelled: false)).Score;

        // 자기 레벨 2 -> 배수 1, 이웃 둘의 유효 레벨 2씩 -> 합 4. 그만큼이 점수에 더 붙는다.
        Assert.Equal(1 * 4 * Worth.DamageBonus, modelled - ignored, 6);
    }

    [Fact]
    public void ItemsThatAreNotArtifactsDoNotCount()
    {
        // 게임은 이웃 칸에 Charm 이 붙어 있을 때만 센다. 잡템 옆자리는 값이 없다.
        var problem = Problem(modelled: true);
        problem.Charms[1].IsFiller = true;
        problem.Charms[2].IsFiller = true;

        var plain = Problem(modelled: false);
        plain.Charms[1].IsFiller = true;
        plain.Charms[2].IsFiller = true;

        Assert.Equal(
            PlacementSolver.Solve(plain).Score,
            PlacementSolver.Solve(problem).Score,
            6);
    }
}

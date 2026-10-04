using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 배치를 견주는 순서. 문서(docs/PLACEMENT-OBJECTIVE.md)와 코드가 같다는 것이 사람 눈에만 걸려 있었다.
/// 단계마다 위 단계는 같고 그 단계만 낫고 아래 단계는 전부 못한 둘을 견준다 - 그 단계가 아래를 모두
/// 이겨야 순서가 맞다.
/// </summary>
public class PlacementQualityTests
{
    private const double Step = 0.05;

    /// <summary>단계 이름, 나은 값, 못한 값. <see cref="PlacementQuality.CompareTo"/> 의 순서 그대로다.</summary>
    private static readonly (string Name, double Better, double Worse)[] Tiers =
    {
        ("사용 유지 실패", 0, 1),
        ("고정 실패", 0, 1),
        ("강화 대상 연결", 1, 0),
        ("콤보 우선 만족", 1, 0),
        ("콤보 우선 진행", 1, 0),
        ("활성 보호 실패", 0, 1),
        ("점수", 10, 0),
        ("감점 빈칸", 0, 1),
        ("초과 강화", 0, 1),
        ("자리 유지", 1, 0),
    };

    public static TheoryData<int> TierIndexes()
    {
        var data = new TheoryData<int>();
        for (var tier = 0; tier < Tiers.Length; tier++) data.Add(tier);
        return data;
    }

    [Theory]
    [MemberData(nameof(TierIndexes))]
    public void EachTierOutranksEverythingBelowIt(int tier)
    {
        var winner = new double[Tiers.Length];
        var loser = new double[Tiers.Length];
        for (var index = 0; index < Tiers.Length; index++)
        {
            winner[index] = index < tier ? Tiers[index].Better : index == tier ? Tiers[index].Better : Tiers[index].Worse;
            loser[index] = index < tier ? Tiers[index].Better : index == tier ? Tiers[index].Worse : Tiers[index].Better;
        }

        Assert.True(Quality(winner).CompareTo(Quality(loser)) > 0, Tiers[tier].Name);
        Assert.True(Quality(loser).CompareTo(Quality(winner)) < 0, Tiers[tier].Name);
    }

    [Fact]
    public void ScoresWithinOneStepTieAndFallThroughToTheNextTier()
    {
        var values = new double[Tiers.Length];
        for (var index = 0; index < Tiers.Length; index++) values[index] = Tiers[index].Better;
        var safer = (double[])values.Clone();
        var richer = (double[])values.Clone();
        richer[6] = 10 + Step * 0.9;
        richer[7] = 1;

        Assert.True(Quality(safer).CompareTo(Quality(richer)) > 0);
    }

    [Fact]
    public void PlanBonusSurvivesTheToleranceAndStaysBelowOneCurrentPosition()
    {
        Assert.True(PlacementSolver.PlanBonus > 10 * PlacementQuality.Tolerance);
        Assert.True(PlacementSolver.PlanBonus * 1000 < 1);

        var values = new double[Tiers.Length];
        var planned = (double[])values.Clone();
        planned[9] = PlacementSolver.PlanBonus;
        Assert.True(Quality(planned).CompareTo(Quality(values)) > 0);
    }

    private static PlacementQuality Quality(double[] tier) => new PlacementQuality(
        retentionFailures: (int)tier[0], activationFailures: (int)tier[5], holdFailures: (int)tier[1],
        comboMatches: (int)tier[3], comboProgress: tier[4], supportMatches: (int)tier[2], value: tier[6],
        unsafeEmpty: (int)tier[7], waste: (int)tier[8], familiarity: tier[9], scoreStep: Step);
}

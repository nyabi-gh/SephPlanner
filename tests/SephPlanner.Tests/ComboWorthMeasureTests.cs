using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 콤보 한 단계가 아티팩트 레벨 몇 개 값어치인지 재는 쪽. 아티팩트와 콤보가 같은 능력치 체계를
/// 쓴다는 점이 다리다.
/// </summary>
public class ComboWorthMeasureTests
{
    private static CharmStatTable Charm(int entityId, string stat, params int[] byLevel) => new()
    {
        EntityId = entityId,
        StatusId = stat,
        ValuesByLevel = byLevel.ToList(),
    };

    private static ComboStatGrant Grant(string category, int threshold, string stat, int value) => new()
    {
        CategoryId = category,
        Threshold = threshold,
        StatusId = stat,
        Value = value,
    };

    [Fact]
    public void ACombosStatIsPricedInLevelsOfTheArtifactsThatGiveTheSameStat()
    {
        var measurement = new StatMeasurement
        {
            // 레벨마다 방어력이 5 씩 오르는 아티팩트.
            CharmStats = { Charm(1, "DEFENSE", 0, 5, 10, 15) },
            // 방어력 20 을 주는 콤보 단계 -> 레벨 4 개 값어치.
            ComboStats = { Grant("STURDY", 2, "DEFENSE", 20) },
        };

        var report = ComboWorthMeasure.Run(measurement);

        Assert.Equal(5, report.PerLevel["DEFENSE"]);
        Assert.Equal(4, Assert.Single(report.Thresholds).Levels, 6);
        Assert.Equal(4, report.MedianLevels, 6);
    }

    [Fact]
    public void OneOutlierArtifactDoesNotDragTheMeasurement()
    {
        // 유별나게 센 아티팩트 하나가 기준을 끌어가면 콤보가 헐값이 된다. 그래서 중앙값을 쓴다.
        var measurement = new StatMeasurement
        {
            CharmStats =
            {
                Charm(1, "DEFENSE", 0, 5, 10),
                Charm(2, "DEFENSE", 0, 5, 10),
                Charm(3, "DEFENSE", 0, 100, 200),
            },
            ComboStats = { Grant("STURDY", 2, "DEFENSE", 20) },
        };

        var report = ComboWorthMeasure.Run(measurement);

        Assert.Equal(5, report.PerLevel["DEFENSE"]);
        Assert.Equal(4, report.Thresholds[0].Levels, 6);
    }

    [Fact]
    public void AStatNoArtifactGivesIsReportedRatherThanCountedAsZero()
    {
        // 못 옮긴 것을 0 으로 삼키면 그 콤보가 값어치 없는 것처럼 보인다. 남겨서 보이게 한다.
        var measurement = new StatMeasurement
        {
            CharmStats = { Charm(1, "DEFENSE", 0, 5) },
            ComboStats =
            {
                Grant("STURDY", 2, "DEFENSE", 10),
                Grant("STURDY", 2, "DROP_RATE", 3),
            },
        };

        var report = ComboWorthMeasure.Run(measurement);
        var threshold = Assert.Single(report.Thresholds);

        Assert.Equal(2, threshold.Levels, 6);
        Assert.Equal("DROP_RATE/3", Assert.Single(threshold.Unconverted));
    }

    [Fact]
    public void EachThresholdOfACategoryIsPricedOnItsOwn()
    {
        var measurement = new StatMeasurement
        {
            CharmStats = { Charm(1, "DEFENSE", 0, 5) },
            ComboStats =
            {
                Grant("STURDY", 2, "DEFENSE", 5),
                Grant("STURDY", 4, "DEFENSE", 15),
            },
        };

        var report = ComboWorthMeasure.Run(measurement);

        Assert.Equal(2, report.Thresholds.Count);
        Assert.Equal(1, report.Thresholds[0].Levels, 6);
        Assert.Equal(3, report.Thresholds[1].Levels, 6);
        Assert.Equal(2, report.MedianLevels, 6);
    }

    [Fact]
    public void NothingMeasuredIsNotAnAnswerOfZero()
    {
        var report = ComboWorthMeasure.Run(new StatMeasurement());

        Assert.Equal(0, report.ConvertedCount);
        Assert.Empty(report.Thresholds);
    }
}

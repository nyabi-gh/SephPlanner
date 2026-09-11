using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 점수의 눈금. 넷 다 게임 자료에서 재므로 게임이 균형을 고치면 함께 움직인다 - 예전처럼
/// 상수로 박아 두면 패치 뒤에 아무 소리 없이 틀린 값으로 점수를 매긴다.
/// </summary>
public class WorthScaleTests
{
    private static CharmStatTable Table(int entityId, string stat, params int[] byLevel) => new()
    {
        EntityId = entityId,
        StatusId = stat,
        ValuesByLevel = byLevel.ToList(),
    };

    private static CharmDefinition Charm(int entityId, int maxLevel) => new()
    {
        EntityId = entityId,
        MaxLevel = maxLevel,
        Behavior = "Charm_StatusInstance",
    };

    private static (StatMeasurement Measurement, List<CharmDefinition> Charms) Catalog()
    {
        var measurement = new StatMeasurement
        {
            CharmStats =
            {
                Table(1, "DEFENSE", 0, 5, 10),
                Table(2, "DEFENSE", 0, 5, 10),
                Table(3, "DEFENSE", 0, 5, 10),
            },
            ComboStats =
            {
                new ComboStatGrant { CategoryId = "STURDY", Threshold = 2, StatusId = "DEFENSE", Value = 10 },
                new ComboStatGrant { CategoryId = "STURDY", Threshold = 4, StatusId = "DEFENSE", Value = 30 },
            },
        };
        var charms = new List<CharmDefinition> { Charm(1, 2), Charm(2, 2), Charm(3, 2) };
        CharmStatWorth.Apply(charms, measurement);
        return (measurement, charms);
    }

    [Fact]
    public void TheComboStepIsTheMedianOfWhatTheThresholdsGive()
    {
        var (measurement, charms) = Catalog();

        var scale = WorthScale.Measure(measurement, charms);

        // 레벨 하나가 방어력 5 이므로 10 은 두 레벨, 30 은 여섯 레벨. 중앙값은 넷이다.
        Assert.Equal(4, scale.ComboThreshold, 3);
        Assert.Equal(4 * WorthScale.ProgressRatio, scale.ComboProgress, 3);
    }

    [Fact]
    public void TheScoreStepStaysUnderTheSmallestRealLevelStep()
    {
        var (measurement, charms) = Catalog();

        var scale = WorthScale.Measure(measurement, charms);
        var worth = CharmWorth.Resolve(charms[0]);
        var smallest = Math.Abs(worth.At(1) - worth.At(0));

        Assert.True(scale.ScoreStep < smallest, $"눈금 {scale.ScoreStep} 이 레벨 한 칸 {smallest} 을 삼킨다");
        Assert.Equal(smallest / 2, scale.ScoreStep, 6);
    }

    [Fact]
    public void WhatCannotBeMeasuredKeepsTheLastMeasuredValue()
    {
        // 콤보가 아무것도 안 주는 카탈로그다. 0 으로 두면 콤보가 통째로 값어치를 잃는다.
        var measurement = new StatMeasurement { CharmStats = { Table(1, "DEFENSE", 0, 5) } };

        var scale = WorthScale.Measure(measurement, new List<CharmDefinition> { Charm(1, 1) });

        Assert.Equal(WorthScale.Default.ComboThreshold, scale.ComboThreshold);
        Assert.Equal(WorthScale.Default.DamageBonus, scale.DamageBonus);
    }

    [Fact]
    public void ACatalogCarriesItsOwnScaleIntoTheSolver()
    {
        var (_, charms) = Catalog();
        var scale = new WorthScale { ComboThreshold = 9, ScoreStep = 0.25 };

        var catalog = new SephPlanner.Core.Planning.Catalog(
            Array.Empty<TabletDefinition>(), charms, Array.Empty<ComboDefinition>(), scale);

        Assert.Equal(9, catalog.Scale.ComboThreshold);
        Assert.Equal(0.25, catalog.Export().Scale!.ScoreStep);
    }
}

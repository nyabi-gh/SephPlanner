using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 아티팩트의 값어치. 오랫동안 "켜져 있으면 1, 레벨 하나에 1"에 레어도 배수만 곱한 값이었고,
/// 그래서 전투 효과가 센 아티팩트와 밋밋한 아티팩트가 같은 점수로 나왔다. 게임의 레벨별
/// 능력치 표에서 잰 값이 그 자리를 대신한다.
/// </summary>
public class CharmWorthTests
{
    private static CharmStatTable Table(int entityId, string stat, params int[] byLevel) => new()
    {
        EntityId = entityId,
        StatusId = stat,
        ValuesByLevel = byLevel.ToList(),
    };

    [Fact]
    public void ACharmIsPricedInLevelsOfTheCharmsThatGiveTheSameStat()
    {
        // 방어력은 레벨 하나에 5 가 표준(1, 2번이 그렇다). 3번은 한 걸음에 10 을 주므로
        // 레벨 하나가 표준의 두 배 값어치다.
        var measurement = new StatMeasurement
        {
            CharmStats =
            {
                Table(1, "DEFENSE", 0, 5, 10),
                Table(2, "DEFENSE", 0, 5, 10),
                Table(3, "DEFENSE", 10, 20, 30),
            },
        };

        var report = CharmStatWorth.Run(measurement);

        Assert.Equal(new[] { 0.0, 1.0, 2.0 }, report.ByEntity[1].ByLevel);
        Assert.Equal(new[] { 2.0, 4.0, 6.0 }, report.ByEntity[3].ByLevel);
    }

    [Fact]
    public void TheTableStopsAtTheCharmsLevelCap()
    {
        // 게임의 LevelToIdx 가 상한에서 자르므로 그 위로는 값어치가 늘지 않는다.
        var measurement = new StatMeasurement { CharmStats = { Table(1, "DEFENSE", 0, 5, 10, 15) } };

        var report = CharmStatWorth.Run(measurement, _ => 1);

        Assert.Equal(new[] { 0.0, 1.0 }, report.ByEntity[1].ByLevel);
    }

    [Fact]
    public void AStatOnlyOneCharmGivesIsMarkedAsCircular()
    {
        // 그 아티팩트만 주는 능력치는 환산율이 자기 자신에서 나온다. 값은 쓰되 그 사실을 남긴다.
        var measurement = new StatMeasurement
        {
            CharmStats =
            {
                Table(1, "DEFENSE", 0, 5),
                Table(2, "DEFENSE", 0, 5),
                Table(3, "DEFENSE", 0, 5),
                Table(9, "ODDBALL", 0, 7),
            },
        };

        var report = CharmStatWorth.Run(measurement);

        Assert.Equal(1.0, report.ByEntity[1].Confidence);
        Assert.Equal(0.0, report.ByEntity[9].Confidence);
    }

    [Fact]
    public void HandWrittenValuesWinOverMeasuredOnes()
    {
        var definition = new CharmDefinition
        {
            EntityId = 5,
            Behavior = "Charm_StatusInstance",
            StatWorthByLevel = { 1, 2, 3 },
        };

        var measured = CharmWorth.Resolve(definition);
        var curated = CharmWorth.Resolve(definition, new CharmValueEntry { Base = 4, PerLevel = 3 });

        Assert.Equal(CharmWorthSource.Measured, measured.Source);
        Assert.Equal(2, measured.At(1));

        Assert.Equal(CharmWorthSource.Curated, curated.Source);
        Assert.Equal(7, curated.At(1));
    }

    [Fact]
    public void ATierMeansTheMeasuredDistributionsQuantile()
    {
        // 등급은 사람이 매기지만 칸의 크기는 측정된 분포에서 왔다. 3 등급이 중앙값이다.
        var (middleBase, middlePerLevel) = CharmWorth.OfTier(3);
        var (topBase, topPerLevel) = CharmWorth.OfTier(5);

        Assert.True(topBase > middleBase);
        Assert.True(topPerLevel > middlePerLevel);

        var worth = CharmWorth.Resolve(new CharmDefinition(), new CharmValueEntry { Tier = 3 });
        Assert.Equal(middleBase + middlePerLevel * 2, worth.At(2), 6);
    }

    [Fact]
    public void ACharmWithEffectsBeyondItsStatsNeverScoresBelowTheRoughEstimate()
    {
        // 파생 클래스는 능력치 밖에 고유 효과가 더 있다. 표에는 그 몫이 없으므로, 표가 낮다고
        // 해서 낮게 볼 근거가 되지 못한다. 아래 한계로만 쓴다.
        var definition = new CharmDefinition
        {
            Rarity = Rarity.Legend,
            Behavior = "Charm_SummonGreenBat",
            StatWorthByLevel = { 0, 0 },
        };

        var worth = CharmWorth.Resolve(definition);

        Assert.Equal(CharmWorthSource.MeasuredFloor, worth.Source);
        Assert.Equal(Worth.OfRarity(Rarity.Legend) * 2, worth.At(1), 6);
    }

    [Fact]
    public void WithoutAnyDataItFallsBackToRarity()
    {
        var worth = CharmWorth.Resolve(new CharmDefinition { Rarity = Rarity.Rare });

        Assert.Equal(CharmWorthSource.Rarity, worth.Source);
        Assert.Equal(Worth.OfRarity(Rarity.Rare) * 3, worth.At(2), 6);
    }

    [Fact]
    public void ALookupPrefersTheEntityNumberButTheIdentifierSettlesADisagreement()
    {
        // 게임 패치로 번호가 밀리면 그 번호는 다른 아티팩트를 가리킨다. 식별자가 받아 준다.
        var book = new CharmValueBook(new CharmValueFile
        {
            Charms =
            {
                new CharmValueEntry { Id = "Alpha", EntityId = 100, Tier = 5 },
                new CharmValueEntry { Id = "Beta", EntityId = 200, Tier = 2 },
            },
        });

        Assert.Equal(5, book.Of(new CharmDefinition { Id = "Alpha", EntityId = 100 })?.Tier);
        Assert.Equal(2, book.Of(new CharmDefinition { Id = "Beta", EntityId = 100 })?.Tier);
        Assert.Null(book.Of(new CharmDefinition { Id = "Gamma", EntityId = 300 }));
    }

    /// <summary>
    /// 실제로 그런 아티팩트가 있다(도마뱀 판금 갑옷 - 레벨을 올릴수록 측정 값어치가 내려간다).
    /// 예전 모델은 레벨이 오르면 값도 오른다고만 알아서, 그런 아티팩트를 좋은 칸에 앉혔다.
    /// </summary>
    [Fact]
    public void ACharmThatGetsWorseWithLevelsGivesUpTheHighCell()
    {
        var arrangement = PlacementSolver.Solve(TwoCells());

        Assert.Equal(new GridPos(0, 0), arrangement.CharmPositions[20]);
        Assert.Equal(new GridPos(1, 0), arrangement.CharmPositions[10]);
    }

    /// <summary>왼쪽 칸만 레벨이 높다. 어느 아티팩트가 그 칸을 받는지만 남는다.</summary>
    private static PlacementProblem TwoCells()
    {
        var problem = new PlacementProblem
        {
            Grid = new GridSpec(6, 7, 2),
            FixedEffects = { new FixedEffectCell { Position = new GridPos(0, 0), Level = 3 } },
        };

        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition
            {
                Id = "Worsening",
                MaxLevel = 3,
                Behavior = "Charm_StatusInstance",
                StatWorthByLevel = { 3, 2, 1, 0 },
            },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 20,
            Definition = new CharmDefinition
            {
                Id = "Scaling",
                MaxLevel = 3,
                Behavior = "Charm_StatusInstance",
                StatWorthByLevel = { 1, 2, 3, 4 },
            },
        });
        return problem;
    }
}

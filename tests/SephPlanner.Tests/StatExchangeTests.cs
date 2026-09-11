using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 능력치를 레벨 단위로 옮기는 환산율. 아티팩트 하나가 레벨 하나로 능력치를 여럿 사므로,
/// 능력치마다 따로 세면 같은 레벨을 중복해서 세게 된다.
/// </summary>
public class StatExchangeTests
{
    private static CharmStatTable Table(int entityId, string stat, params int[] byLevel) => new()
    {
        EntityId = entityId,
        StatusId = stat,
        ValuesByLevel = byLevel.ToList(),
    };

    private static CharmStatProfile Profile(int entityId, int maxLevel) => new()
    {
        EntityId = entityId,
        MaxLevel = maxLevel,
        StatsAreEverything = true,
    };

    private static double LevelWorth(StatExchange exchange, params (string Stat, double Amount)[] stats)
    {
        double sum = 0;
        foreach (var (stat, amount) in stats)
        {
            Assert.True(exchange.TryConvert(stat, amount, out var levels), stat);
            sum += levels;
        }
        return sum;
    }

    [Fact]
    public void ALevelThatBuysTwoStatsIsNotCountedTwice()
    {
        // 2번은 레벨 하나로 둘을 함께 산다. 능력치마다 따로 세면 그 레벨이 1번의 두 배 값어치로
        // 나오는데, 산 것은 양쪽 다 레벨 하나다.
        var exchange = StatExchange.From(
            new[]
            {
                Table(1, "DEFENSE", 0, 10),
                Table(2, "DEFENSE", 0, 10),
                Table(2, "ATTACK", 0, 10),
            },
            new[] { Profile(1, 1), Profile(2, 1) });

        var one = LevelWorth(exchange, ("DEFENSE", 10));
        var two = LevelWorth(exchange, ("DEFENSE", 10), ("ATTACK", 10));

        Assert.Equal(1.5, exchange.Scale, 6);
        Assert.Equal(1, (one + two) / 2, 6);
        Assert.True(two < 2, $"묶음이 아직 중복해서 세어진다: {two}");
    }

    [Fact]
    public void AStatOnlyOneGenerousCharmGivesStaysFinite()
    {
        // 5번은 레벨당 값어치가 표준의 두 배다. DEFENSE 는 다른 넷이 붙잡고 있으므로 그 남는
        // 몫은 RARE 쪽으로 몰리는데, 수축이 없으면 RARE 의 환산율이 발산한다.
        var tables = new List<CharmStatTable>();
        var profiles = new List<CharmStatProfile>();
        for (var entity = 1; entity <= 4; entity++)
        {
            tables.Add(Table(entity, "DEFENSE", 0, 10));
            profiles.Add(Profile(entity, 1));
        }
        tables.Add(Table(5, "DEFENSE", 0, 10));
        tables.Add(Table(5, "RARE", 0, 10));
        profiles.Add(Profile(5, 1));

        var exchange = StatExchange.From(tables, profiles);

        Assert.True(exchange.TryConvert("RARE", 10, out var levels));
        Assert.InRange(levels, 0.01, 1);
        Assert.Equal(1, LevelWorth(exchange, ("DEFENSE", 10)), 6);
    }

    [Fact]
    public void AStandingBonusDoesNotInheritTheRateOfAStatThatBarelyRises()
    {
        // 도시락은 레벨 넷에 HP 흡수 +1 을 준다. 그것을 "레벨당 0.25" 로 읽으면 붙박이로 8 을
        // 주는 혈석 귀걸이가 32 레벨짜리가 된다. 붙박이는 레벨로 사는 것이 아니라 켜 두는 값이다.
        var tables = new List<CharmStatTable>
        {
            Table(1, "STEAL", 1, 1, 1, 1, 2),
            Table(2, "STEAL", 8, 8, 8, 8),
        };
        var profiles = new List<CharmStatProfile> { Profile(1, 4), Profile(2, 3) };
        for (var entity = 3; entity <= 5; entity++)
        {
            tables.Add(Table(entity, "DEFENSE", 0, 5));
            profiles.Add(Profile(entity, 1));
        }

        var exchange = StatExchange.From(tables, profiles);

        Assert.True(exchange.TryConvert("STEAL", 8, out var levels));
        Assert.InRange(levels, 0, 12);
    }

    [Fact]
    public void AStatThatOnlyRisesAboveTheLevelCapIsNotPriced()
    {
        // 상한이 0 이면 그 위의 표는 게임이 닿지 못하는 칸이다. 거기서 걸음을 재면 아무도
        // 살 수 없는 레벨로 환산율을 매기게 된다.
        var exchange = StatExchange.From(
            new[]
            {
                Table(1, "DEFENSE", 0, 10),
                Table(2, "LOCKED", 0, 10),
            },
            new[] { Profile(1, 1), Profile(2, 0) });

        Assert.True(exchange.TryConvert("DEFENSE", 10, out _));
        Assert.False(exchange.TryConvert("LOCKED", 10, out _));
    }

    [Fact]
    public void WithoutSampleCharmsTheScaleIsLeftAlone()
    {
        var exchange = StatExchange.From(new[] { Table(1, "DEFENSE", 0, 5, 10) });

        Assert.Equal(1, exchange.Scale);
        Assert.Equal(5, exchange.PerLevel["DEFENSE"]);
    }
}

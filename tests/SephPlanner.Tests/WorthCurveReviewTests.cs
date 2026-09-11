using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public class WorthCurveReviewTests
{
    /// <summary>
    /// 토끼마을 경비병 투구의 모양이다. 회피가 레벨 2에서 두 배가 되는데 피해는 선형으로 올라
    /// 순가치가 레벨 1보다 레벨 2에서 낮다. 제보가 온 자리이므로 고정 사례로 남긴다.
    /// </summary>
    [Fact]
    public void FindsTheLevelWhereADoubledPenaltyOutrunsTheGain()
    {
        var definition = Helmet();

        var drops = WorthCurveReview.Of(definition);

        var drop = Assert.Single(drops);
        Assert.Equal(1, drop.FromLevel);
        Assert.Equal(2, drop.ToLevel);
        Assert.True(drop.Delta < 0);
        Assert.Equal("EVASION", drop.Cause);
        Assert.Equal(-400, drop.CauseFromAmount);
        Assert.Equal(-800, drop.CauseToAmount);
    }

    [Fact]
    public void LeavesMonotonicCurvesAlone()
    {
        var definition = new CharmDefinition
        {
            MaxLevel = 3,
            Behavior = "Charm_StatusInstance",
            StatWorthCoverageKnown = true,
            StatWorthByLevel = { 1.5, 3.5, 6.0, 7.5 },
            StatBenefitByLevel = { 1.5, 3.5, 6.0, 7.5 },
            StatPenaltyByLevel = { 0, 0, 0, 0 },
        };

        Assert.Empty(WorthCurveReview.Of(definition));
    }

    /// <summary>
    /// 고유 효과가 있어 레어도 하한이 함께 걸리는 아티팩트는 하한이 곡선을 받쳐 준다. 내려가는
    /// 것처럼 보이는 표를 들고도 실제 배치에 쓰이는 값어치는 오르므로 목록에 들지 않아야 한다.
    /// </summary>
    [Fact]
    public void CountsTheFloorThatHoldsTheCurveUp()
    {
        var definition = Helmet();
        definition.Behavior = "Charm_Shieldmate";
        definition.Rarity = Rarity.Legend;

        Assert.Empty(WorthCurveReview.Of(definition));
    }

    [Fact]
    public void StopsAtTheLevelCap()
    {
        var definition = Helmet();
        definition.MaxLevel = 1;

        Assert.Empty(WorthCurveReview.Of(definition));
    }

    private static CharmDefinition Helmet() => new CharmDefinition
    {
        EntityId = 1157,
        Id = "RabbitVillageGuardHelmet",
        Rarity = Rarity.Rare,
        MaxLevel = 3,
        Behavior = "Charm_StatusInstance",
        StatWorthCoverageKnown = true,
        StatEffects =
        {
            new CharmStatEffect
            {
                StatusId = "FINAL_WEAPONDAMAGE",
                AmountByLevel = { 4, 8, 12, 16 },
                WorthPerUnit = 0.3076923076923077,
            },
            new CharmStatEffect
            {
                StatusId = "EVASION",
                AmountByLevel = { -400, -400, -800, -800 },
                WorthPerUnit = 0.005,
            },
        },
        StatWorthByLevel = { -0.769, 0.462, -0.308, 0.923 },
        StatBenefitByLevel = { 1.231, 2.462, 3.692, 4.923 },
        StatPenaltyByLevel = { -2, -2, -4, -4 },
    };
}

using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 칸 쪽지가 값어치의 근거를 얼마나 밝히는지. 잰 값이라는 것만 말하고 근거의 두께를 안 말하면
/// 짐작에 가까운 숫자가 실측과 같은 무게로 보인다.
/// </summary>
public class ExplainWorthTests
{
    private static CharmDefinition Measured(double confidence) => new()
    {
        EntityId = 1,
        Behavior = "Charm_StatusInstance",
        StatWorthCoverageKnown = true,
        StatWorthByLevel = { 1, 2 },
        StatWorthConfidence = confidence,
        StatEffects = { new CharmStatEffect { StatusId = "DEFENSE", AmountByLevel = { 0, 5 } } },
    };

    private static string Text(CharmDefinition definition) =>
        string.Join(" ", Explain.Charm(definition, CharmValueBook.Empty));

    [Fact]
    public void AStatOnlyThisCharmGivesIsCalledOut()
    {
        // 환산율이 자기 자신에서 나온 값은 다른 아티팩트와 견주는 근거가 못 된다.
        Assert.Contains("자기 자신에서 나왔습니다", Text(Measured(0)));
    }

    [Fact]
    public void AWellSampledConversionSaysNothingExtra()
    {
        Assert.DoesNotContain("자기 자신에서", Text(Measured(1)));
        Assert.DoesNotContain("근거가 얇은", Text(Measured(1)));
    }

    [Fact]
    public void AThinConversionIsCalledOutMoreGently()
    {
        var text = Text(Measured(0.2));

        Assert.Contains("근거가 얇은", text);
        Assert.DoesNotContain("자기 자신에서", text);
    }

    [Fact]
    public void BothSideBoundCharmsSayTheyKeepTheirSide()
    {
        // 게임에서 XIdx <= 2 로 편을 가르는 클래스는 둘뿐인데 오래도록 하나만 지켜 왔다.
        foreach (var behavior in new[] { "Charm_FireIce", "Charm_FireIceWeapon" })
        {
            var definition = new CharmDefinition { EntityId = 1, Behavior = behavior };

            Assert.True(ScalesPosition.IsSideBound(definition), behavior);
            Assert.Contains("지금 놓인 쪽을 유지합니다", Text(definition));
        }
    }
}

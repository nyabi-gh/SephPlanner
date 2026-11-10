using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 게임 1.1.0 의 콤보 능력치. <c>UNLIMITEDCOMBO</c> 는 최고 단계 너머의 개수에, <c>COMBOBONUSDAMAGE</c> 는
/// 발동한 콤보 수에 피해를 준다. 둘 다 없으면 콤보 한 걸음은 전과 같아야 한다.
/// </summary>
public class ComboStatBonusTests
{
    private static readonly ComboDefinition Combo = new() { Id = "EMBER", Thresholds = { 2, 4, 6 } };

    private static PlacementProblem Problem(ComboStatBonus? bonus) => new() { ComboBonus = bonus };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public void WithoutTheStatsAStepIsTheScaleAlone(int count)
    {
        var problem = Problem(null);
        var expected = problem.Scale.OfComboStep(Combo, count, out var completes, out var goal);

        Assert.Equal(expected, problem.ComboStep(Combo, count, out var actualCompletes, out var actualGoal));
        Assert.Equal(completes, actualCompletes);
        Assert.Equal(goal, actualGoal);
    }

    [Fact]
    public void NoStatsMeansNoBonus() => Assert.Null(ComboStatBonus.Of(0, 0));

    [Fact]
    public void UnlimitedComboValuesStepsBeyondTheTopStage()
    {
        var problem = Problem(ComboStatBonus.Of(3, 0));

        var beyond = problem.ComboStep(Combo, 6, out var completes, out _);

        Assert.True(completes);
        Assert.Equal(3 * problem.Scale.DamageBonus, beyond, 9);
        Assert.Equal(problem.Scale.OfComboStep(Combo, 4, out _, out _), problem.ComboStep(Combo, 4, out _, out _));
    }

    [Fact]
    public void ComboBonusDamageAddsToTheFirstStageOnly()
    {
        var problem = Problem(ComboStatBonus.Of(0, 5));
        var plain = problem.Scale;

        Assert.Equal(plain.OfComboStep(Combo, 1, out _, out _) + 5 * plain.DamageBonus,
            problem.ComboStep(Combo, 1, out _, out _), 9);
        Assert.Equal(plain.OfComboStep(Combo, 3, out _, out _), problem.ComboStep(Combo, 3, out _, out _));
        Assert.Equal(0, problem.ComboStep(Combo, 6, out var completes, out _));
        Assert.False(completes);
    }

    /// <summary>
    /// 신비 각인 판에서도 콤보 능력치 몫은 자리 의존 아티팩트의 값에 남는다. 게임은 신비도 콤보로 세어
    /// <c>COMBOBONUSDAMAGE</c> 를 준다. 빠지는 것은 칸 점수로 이미 센 단계 값뿐이다.
    /// </summary>
    [Fact]
    public void AMysticKeyKeepsTheComboStatShare()
    {
        var key = new CharmSlot { InstanceId = 1, Definition = new CharmDefinition { MaxLevel = 5, LineCategories = { "MYSTIC" } } };
        var problem = new PlacementProblem
        {
            ComboBonus = new ComboStatBonus { ComboBonusDamage = 7 },
            ComboEngraving = new ComboEngravingRule { Category = "MYSTIC", Query = "O MUL/2" },
            ComboCounts = new Dictionary<string, int> { ["MYSTIC"] = 1 },
            Combos = id => new ComboDefinition { Id = id, Thresholds = { 1, 3 } },
        };
        problem.Charms.Add(key);
        problem.CurrentCharms[1] = new GridPos(0, 0);

        var worth = PositionalWorth.ComboWorth(problem, key, new GridPos(0, 0), null);

        Assert.Equal(7 * problem.Scale.DamageBonus, worth, 9);
    }
}

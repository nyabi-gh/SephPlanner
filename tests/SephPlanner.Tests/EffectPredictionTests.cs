using SephPlanner.Core.Model;
using SephPlanner.DataTool.Prediction;

namespace SephPlanner.Tests;

public class EffectPredictionTests
{
    [Fact]
    public void SamePaperLayoutAndInitialStateCanHaveDifferentResults()
    {
        var left = PredictionProbe.PaperPair();
        var right = PredictionProbe.PaperPair();
        left.Refresh(new[] { 2, 3 });
        right.Refresh(new[] { 3, 2 });
        Assert.Empty(left.Categories(2));
        Assert.Empty(left.Categories(3));
        Assert.Equal(new[] { "EMBER" }, right.Categories(2));
        Assert.Equal(new[] { "EMBER" }, right.Categories(3));
        left.Refresh(new[] { 3, 2 });
        Assert.Empty(left.Categories(2));
    }

    [Fact]
    public void KnownEventSequenceReproducesTheSameState()
    {
        var one = PredictionProbe.PaperPair();
        var two = PredictionProbe.PaperPair();
        var sequence = new[] { 3, 2, 2, 3, 3, 2 };
        one.Refresh(sequence);
        two.Refresh(sequence);
        for (var id = 1; id <= 4; id++) Assert.Equal(one.Categories(id), two.Categories(id));
    }

    [Fact]
    public void SwapPreservesObservedStateUntilAnExplicitRefreshEvent()
    {
        var replay = PredictionProbe.PaperPair();
        replay.Swap(new(1, 0), new(5, 0));
        Assert.Equal(new[] { "EMBER" }, replay.Categories(2));
        replay.Refresh(2);
        Assert.Empty(replay.Categories(2));
        replay.Swap(new(5, 0), new(1, 0));
        replay.Refresh(new[] { 3, 2 });
        Assert.Empty(replay.Categories(2));
    }

    [Fact]
    public void KeyRefreshBeforeNeedleAndPaperPropagatesNewCategories()
    {
        var replay = new CategoryReplay(new[]
        {
            new CategoryReplay.Item { Id = 1, Position = new(0, 0), Attackable = true, Definition = new() { LineCategories = { "EMBER" } }, Categories = new() { "OLD" } },
            new CategoryReplay.Item { Id = 2, Position = new(0, 1), Definition = new() { DependencyBonusByLevel = { 1 }, DependencyOffsetY = -1 }, Categories = new() },
            new CategoryReplay.Item { Id = 3, Position = new(1, 1), Definition = new() { Behavior = "Charm_WhitePaper" }, Categories = new() },
            new CategoryReplay.Item { Id = 4, Position = new(2, 1), Definition = new(), Categories = new() { "EMBER" } },
        });
        replay.Refresh(new[] { 1, 2, 3 });
        Assert.Equal(new[] { "EMBER" }, replay.Categories(3));
    }

    [Fact]
    public void NeedleCycleClearsPreviousCategoriesWithoutRecursion()
    {
        var replay = new CategoryReplay(new[]
        {
            new CategoryReplay.Item { Id = 1, Position = new(0, 0), Definition = new() { DependencyBonusByLevel = { 1 }, DependencyOffsetX = 1 }, Categories = new() { "OLD" } },
            new CategoryReplay.Item { Id = 2, Position = new(1, 0), Definition = new() { DependencyBonusByLevel = { 1 }, DependencyOffsetX = -1 }, Categories = new() { "OLD" } },
        });
        replay.Refresh(new[] { 1, 2 });
        Assert.Empty(replay.Categories(1));
        Assert.Empty(replay.Categories(2));
    }

    [Theory]
    [InlineData(19, 0, 1)]
    [InlineData(19, 50, 2)]
    [InlineData(-1, 0, -1)]
    [InlineData(-11, 0, -2)]
    public void ConversionUsesAmplifiedSourceThenFloatFloor(int source, int amplification, int expected)
    {
        var state = new StatReplay(new() { ["DAMAGEREDUCTION"] = source }, new() { ["DAMAGEREDUCTION"] = amplification });
        state.Add(1, new() { Source = "DAMAGEREDUCTION", Divisor = 10, Amounts = new() { ["FIREDAMAGE"] = 1 } });
        state.Refresh(1);
        Assert.Equal(expected, state.Read("FIREDAMAGE"));
    }

    [Fact]
    public void RemovingASourceProviderRecomputesInsteadOfReusingTheObservedTotal()
    {
        var state = new StatReplay(new() { ["DAMAGEREDUCTION"] = 20 });
        state.Add(1, new() { Source = "DAMAGEREDUCTION", Divisor = 10, Amounts = new() { ["FIREDAMAGE"] = 2 }, Applied = new() { ["FIREDAMAGE"] = 4 }, LastSource = 20 });
        Assert.Equal(4, state.Read("FIREDAMAGE"));
        state.ChangeBase("DAMAGEREDUCTION", -10);
        state.Refresh(1);
        Assert.Equal(2, state.Read("FIREDAMAGE"));
        state.Refresh(1);
        Assert.Equal(2, state.Read("FIREDAMAGE"));
    }

    [Fact]
    public void InventoryRefreshAndTimerReadTheSourceAtDifferentTimes()
    {
        StatReplay Build()
        {
            var state = new StatReplay(new() { ["A"] = 10 });
            state.Add(1, new() { Source = "A", Divisor = 1, Amounts = new() { ["A"] = 1 }, Applied = new() { ["A"] = 10 }, LastSource = 10 });
            return state;
        }
        var refresh = Build();
        var timer = Build();
        refresh.Refresh(1);
        timer.Tick(1);
        Assert.Equal(20, refresh.Read("A"));
        Assert.Equal(30, timer.Read("A"));
    }

    [Fact]
    public void FeedbackDoesNotHaveToConvergeToAFiniteFixedPoint()
    {
        var state = PredictionProbe.Feedback();
        for (var pass = 1; pass <= 5; pass++)
        {
            state.Refresh(1);
            state.Refresh(2);
            Assert.Equal(10 * (pass + 1), state.Read("A"));
        }
    }

    [Fact]
    public void DisabledConversionRemovesItsPreviousContribution()
    {
        var state = new StatReplay(new() { ["A"] = 10 });
        state.Add(1, new() { Source = "A", Divisor = 1, Amounts = new() { ["B"] = 1 }, Applied = new() { ["B"] = 10 }, Enabled = false });
        state.Refresh(1);
        Assert.Equal(0, state.Read("B"));
        Assert.Empty(state.Applied(1));
    }

    [Fact]
    public void ElementConversionUsesStrongestDestinationAndDoesNotRecursivelyConvertIncomingDamage()
    {
        var state = new StatReplay(new()
        {
            ["FIREDAMAGE"] = 100,
            ["ICEDAMAGE"] = 30,
            ["FIRETOICE"] = 50,
            ["FIRETOLIGHTNING"] = 50,
            ["ICETOLIGHTNING"] = 100,
        });
        Assert.Equal(20, state.Read("FIREDAMAGE"));
        Assert.Equal(60, state.Read("ICEDAMAGE"));
        Assert.Equal(10, state.Read("LIGHTNINGDAMAGE"));
    }

    [Fact]
    public void InvalidDivisorFailsExplicitly()
    {
        var state = new StatReplay(new());
        Assert.Throws<ArgumentOutOfRangeException>(() => state.Add(1, new() { Source = "A", Divisor = 0, Amounts = new() }));
    }
}

using SephPlanner.Core.Combat;

namespace SephPlanner.Tests;

public class CombatStatStateTests
{
    [Fact]
    public void ReplacingAndRemovingAProviderRecomputesTheWholeStat()
    {
        var state = new CombatStatState();
        state.SetSource(new() { Id = "base", Stats = { ["PHYSICALDAMAGE"] = 100 } });
        state.SetSource(new() { Id = "charm", Stats = { ["PHYSICALDAMAGE"] = 20 }, Amplification = { ["PHYSICALDAMAGE"] = 50 } });
        Assert.Equal(180, state.Read("PHYSICALDAMAGE"));
        state.SetSource(new() { Id = "charm", Stats = { ["PHYSICALDAMAGE"] = 40 }, Amplification = { ["PHYSICALDAMAGE"] = 25 } });
        Assert.Equal(175, state.Read("PHYSICALDAMAGE"));
        Assert.True(state.RemoveSource("charm"));
        Assert.Equal(100, state.Read("PHYSICALDAMAGE"));
        Assert.False(state.RemoveSource("charm"));
        Assert.Equal(100, state.Read("PHYSICALDAMAGE"));
    }

    [Fact]
    public void MovingAConversionReleasesItsPreviousSource()
    {
        var state = new CombatStatState();
        state.SetSource(new() { Id = "base", Stats = { ["FIREDAMAGE"] = 100, ["ICEDAMAGE"] = 30 } });
        state.SetSource(new() { Id = "conversion", Stats = { ["FIRETOICE"] = 50 } });
        Assert.Equal(20, state.Read("FIREDAMAGE"));
        Assert.Equal(70, state.Read("ICEDAMAGE"));
        state.SetSource(new() { Id = "conversion", Stats = { ["ICETOFIRE"] = 100 } });
        Assert.Equal(110, state.Read("FIREDAMAGE"));
        Assert.Equal(20, state.Read("ICEDAMAGE"));
    }

    [Fact]
    public void TiedConversionsFollowGameElementOrderAndDoNotChain()
    {
        var state = new CombatStatState();
        state.SetSource(new()
        {
            Id = "base",
            Stats = { ["FIREDAMAGE"] = 100, ["ICEDAMAGE"] = 30, ["FIRETOICE"] = 50, ["FIRETOLIGHTNING"] = 50, ["ICETOLIGHTNING"] = 100 },
        });
        Assert.Equal(20, state.Read("FIREDAMAGE"));
        Assert.Equal(60, state.Read("ICEDAMAGE"));
        Assert.Equal(10, state.Read("LIGHTNINGDAMAGE"));
    }

    [Fact]
    public void CandidateCopiesAndExportedDataCannotChangeTheBaseline()
    {
        var state = new CombatStatState();
        var source = new CombatStatSource { Id = "charm", Stats = { ["DASHATTACKDAMAGEBONUS"] = 30 } };
        state.SetSource(source);
        source.Stats["DASHATTACKDAMAGEBONUS"] = 999;
        var candidate = state.Copy();
        candidate.RemoveSource("charm");
        state.Export()[0].Stats.Clear();
        Assert.Equal(30, state.Read("DASHATTACKDAMAGEBONUS"));
        Assert.Equal(0, candidate.Read("DASHATTACKDAMAGEBONUS"));
    }

    [Theory]
    [InlineData(-11, 50, -16)]
    [InlineData(19, 50, 28)]
    [InlineData(19, -100, 0)]
    public void AmplificationUsesGameIntegerTruncation(int amount, int amplification, int expected)
    {
        var state = new CombatStatState();
        state.SetSource(new() { Id = "source", Stats = { ["DAMAGEREDUCTION"] = amount }, Amplification = { ["DAMAGEREDUCTION"] = amplification } });
        Assert.Equal(expected, state.Read("DAMAGEREDUCTION"));
    }
}

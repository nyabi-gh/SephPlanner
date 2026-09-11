using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Plugin.Ui;

namespace SephPlanner.Tests;

public sealed class HudFrameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OpeningAndClosingMixerInvalidatesAnAlreadyExpandedHudWithoutANewPlan(bool hasOffers)
    {
        var plan = new Plan();
        if (hasOffers) plan.Offers.Add(new());
        var frame = new HudFrame
        {
            Snapshot = new(),
            Plan = plan,
            Catalog = new Catalog([], []),
            Prefs = new(),
            Values = CharmValueBook.Empty,
            Expanded = true,
            MixerOpen = false,
            Recommendations = true,
            MultiplayerAutoPlace = false,
            QueryVerified = true,
            RuntimeVerification = PlanVerificationStatus.Passed,
            RuntimeVerificationReason = "",
            Hint = "",
            HintIsPreview = false,
            PreviewKey = ""
        };
        var closed = new HudFrameKey(frame);
        Assert.True(closed.Matches(new HudFrameKey(frame)));
        frame.MixerOpen = true;
        var opened = new HudFrameKey(frame);
        Assert.False(closed.Matches(opened));
        Assert.True(opened.Matches(new HudFrameKey(frame)));
        frame.MixerOpen = false;
        Assert.False(opened.Matches(new HudFrameKey(frame)));
        Assert.True(closed.Matches(new HudFrameKey(frame)));
    }

    /// <summary>계획 객체가 그대로여도 낡음 표시가 바뀌면 다시 그려야 한다.</summary>
    [Fact]
    public void GoingStaleAndBackRedrawsEvenThoughThePlanIsTheSame()
    {
        var plan = new Plan();
        var frame = new HudFrame
        {
            Snapshot = new(),
            Plan = plan,
            Catalog = new Catalog([], []),
            Prefs = new(),
            Values = CharmValueBook.Empty,
            Expanded = true,
            RuntimeVerification = PlanVerificationStatus.Passed,
            RuntimeVerificationReason = "",
            Hint = "",
            PreviewKey = "",
            Stale = false,
        };

        var fresh = new HudFrameKey(frame);
        frame.Stale = true;
        var stale = new HudFrameKey(frame);

        Assert.False(fresh.Matches(stale));
        Assert.True(stale.Matches(new HudFrameKey(frame)));

        frame.Stale = false;
        Assert.False(stale.Matches(new HudFrameKey(frame)));
        Assert.True(fresh.Matches(new HudFrameKey(frame)));
    }
}

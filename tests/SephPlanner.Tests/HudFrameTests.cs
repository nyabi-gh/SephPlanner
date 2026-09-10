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
}

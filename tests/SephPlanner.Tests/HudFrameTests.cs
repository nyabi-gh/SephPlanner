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

    /// <summary>
    /// 인챈트 조언은 제단이 연 창이 떠 있을 때만 보인다. 그 상태가 열쇠에 없으면 창을 열어도
    /// 화면이 그대로여서 조언이 나타나지 않는다 - 합성기 창과 같은 자리다.
    /// </summary>
    [Fact]
    public void OpeningAndClosingTheEnchantWindowRedrawsEvenThoughThePlanIsTheSame()
    {
        var frame = new HudFrame
        {
            Snapshot = new(),
            Plan = new Plan(),
            Catalog = new Catalog([], []),
            Prefs = new(),
            Values = CharmValueBook.Empty,
            Expanded = true,
            RuntimeVerification = PlanVerificationStatus.Passed,
            RuntimeVerificationReason = "",
            Hint = "",
            PreviewKey = "",
            EnchantOpen = false,
        };

        var closed = new HudFrameKey(frame);
        frame.EnchantOpen = true;
        var opened = new HudFrameKey(frame);

        Assert.False(closed.Matches(opened));
        Assert.True(opened.Matches(new HudFrameKey(frame)));

        frame.EnchantOpen = false;
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

    /// <summary>
    /// 조언은 배치 뒤에 붙는다. 계획 객체가 그대로인 채 "조언 계산 중" 만 켜지고 꺼지므로,
    /// 그것이 열쇠에 없으면 화면이 "추천 조합 없음" 에 멈춰 있다.
    /// </summary>
    [Fact]
    public void TheAdviceCatchingUpRedrawsEvenThoughThePlanIsTheSame()
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
            AdviceBusy = true,
        };

        var busy = new HudFrameKey(frame);
        frame.AdviceBusy = false;
        var ready = new HudFrameKey(frame);

        Assert.False(busy.Matches(ready));
        Assert.True(ready.Matches(new HudFrameKey(frame)));
    }
}

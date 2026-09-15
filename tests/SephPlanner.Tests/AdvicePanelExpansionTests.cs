using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class AdvicePanelExpansionTests
{
    [Fact]
    public void MixerAloneOpensDetailsAndRepeatedFramesPreserveManualChoice()
    {
        var panel = new AdvicePanelExpansion();
        Assert.Null(panel.Update(false, false, false));
        Assert.True(panel.Update(false, true, false));
        Assert.Null(panel.Update(false, true, false));
        Assert.False(panel.Update(false, false, false));
        Assert.True(panel.Update(false, true, false));
    }

    [Fact]
    public void SwitchingFromMerchantToMixerDoesNotCollapseDetails()
    {
        var panel = new AdvicePanelExpansion();
        Assert.True(panel.Update(true, false, false));
        Assert.True(panel.Update(false, true, false));
        Assert.True(panel.Update(true, true, false));
        Assert.True(panel.Update(false, true, false));
        Assert.False(panel.Update(false, false, false));
    }

    /// <summary>
    /// 인챈트 제단이 연 창도 조언 칸을 펼친다. 합성기와 같은 자리이고, 둘 사이를 오갈 때
    /// 접혔다 펴지지 않아야 한다.
    /// </summary>
    [Fact]
    public void TheEnchantWindowOpensDetailsAndDoesNotCollapseWhenSwitching()
    {
        var panel = new AdvicePanelExpansion();
        Assert.True(panel.Update(false, false, true));
        Assert.Null(panel.Update(false, false, true));
        Assert.True(panel.Update(false, true, true));
        Assert.True(panel.Update(false, true, false));
        Assert.False(panel.Update(false, false, false));
    }
}

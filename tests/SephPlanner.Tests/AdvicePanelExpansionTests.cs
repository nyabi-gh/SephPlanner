using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class AdvicePanelExpansionTests
{
    [Fact]
    public void MixerAloneOpensDetailsAndRepeatedFramesPreserveManualChoice()
    {
        var panel = new AdvicePanelExpansion();
        Assert.Null(panel.Update(false, false));
        Assert.True(panel.Update(false, true));
        Assert.Null(panel.Update(false, true));
        Assert.False(panel.Update(false, false));
        Assert.True(panel.Update(false, true));
    }

    [Fact]
    public void SwitchingFromMerchantToMixerDoesNotCollapseDetails()
    {
        var panel = new AdvicePanelExpansion();
        Assert.True(panel.Update(true, false));
        Assert.True(panel.Update(false, true));
        Assert.True(panel.Update(true, true));
        Assert.True(panel.Update(false, true));
        Assert.False(panel.Update(false, false));
    }
}

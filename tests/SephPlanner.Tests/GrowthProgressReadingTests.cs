using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public sealed class GrowthProgressReadingTests
{
    [Theory]
    [InlineData("Charm_4213", "0/50", 4213, 0)]
    [InlineData("Charm_4213", "49/50", 4213, 49)]
    [InlineData("Charm_4213", "50/50", 4213, 50)]
    [InlineData("Charm_1", "7/30", 1, 7)]
    public void ReadsTheCounterAndInstanceFromWhatTheGameDisplays(
        string effectName, string value, int instanceId, int progress)
    {
        Assert.True(GrowthProgressReading.TryRead(effectName, value, out var readId, out var readProgress));
        Assert.Equal(instanceId, readId);
        Assert.Equal(progress, readProgress);
    }

    [Theory]
    [InlineData("GUARDBREAK", "1/2")]          // 성장과 무관한 다른 표시
    [InlineData("Charm_", "1/2")]
    [InlineData("Charm_abc", "1/2")]
    [InlineData("Charm_12", "")]
    [InlineData("Charm_12", "곧/50")]
    [InlineData("Charm_12", "-3/50")]
    [InlineData(null, "1/2")]
    [InlineData("Charm_12", null)]
    public void UnknownShapesFailInsteadOfReadingAsZero(string? effectName, string? value)
    {
        // 0 은 "아직 아무것도 못 채웠다"는 뜻이다. 못 읽은 것을 0 으로 적으면 둘을 구분할 수 없다.
        Assert.False(GrowthProgressReading.TryRead(effectName, value, out var instanceId, out var progress));
        Assert.Equal(0, instanceId);
        Assert.Equal(0, progress);
    }

    [Fact]
    public void ACounterWithoutAGoalStillReads()
    {
        // 게임이 목표 없이 숫자만 보내더라도 진행도는 알 수 있다.
        Assert.True(GrowthProgressReading.TryRead("Charm_9", "12", out var instanceId, out var progress));
        Assert.Equal(9, instanceId);
        Assert.Equal(12, progress);
    }
}

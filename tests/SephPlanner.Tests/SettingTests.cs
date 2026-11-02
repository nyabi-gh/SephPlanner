using SephPlanner.Plugin.Host;

namespace SephPlanner.Tests;

/// <summary>
/// BepInEx 의 <c>ConfigEntry</c> 를 갈음한 설정 값. 창작마당 판의 모드 옵션 저장소도 이 규칙에 기대므로, 0.5.x 의
/// ConfigEntry 가 해 주던 것(같은 값이면 알리지 않음, 범위 자르기)이 그대로인지 본다.
/// </summary>
public sealed class SettingTests
{
    [Fact]
    public void SameValueNeitherStoresNorNotifies()
    {
        var stored = new List<bool>();
        var setting = new Setting<bool>("Enabled", true, true, null, stored.Add);
        var changes = 0;
        setting.SettingChanged += (_, _) => changes++;

        setting.Value = true;
        Assert.Empty(stored);
        Assert.Equal(0, changes);

        setting.Value = false;
        Assert.Equal(new[] { false }, stored);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void RangeClampsBothStoredAndWrittenValues()
    {
        var stored = new List<float>();
        var setting = new Setting<float>("Opacity", 1f, 7f, value => SettingRange.Clamp(value, 0.1f, 1f), stored.Add);
        Assert.Equal(1f, setting.Value);

        setting.Value = 0f;
        Assert.Equal(0.1f, setting.Value);
        Assert.Equal(new[] { 0.1f }, stored);
    }

    [Fact]
    public void NotANumberFallsToTheLowerBound()
    {
        Assert.Equal(0.1f, SettingRange.Clamp(float.NaN, 0.1f, 5f));
        Assert.Equal(5f, SettingRange.Clamp(float.PositiveInfinity, 0.1f, 5f));
    }

    [Fact]
    public void ReloadNotifiesWithoutWritingBack()
    {
        var stored = new List<string>();
        var setting = new Setting<string>("Language", "Auto", "Auto", null, stored.Add);
        var changes = 0;
        setting.SettingChanged += (_, _) => changes++;

        setting.Reloaded("ko-KR");
        Assert.Equal("ko-KR", setting.Value);
        Assert.Equal(1, changes);
        Assert.Empty(stored);

        setting.Reloaded("ko-KR");
        Assert.Equal(1, changes);
    }
}

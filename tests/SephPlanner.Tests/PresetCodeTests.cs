using SephPlanner.Core.Planning;

namespace SephPlanner.Tests;

/// <summary>
/// 게임의 커스텀 로드아웃 공유 코드를 읽는 쪽. 코드는 커뮤니티가 빌드 공략에 붙여 공유하는
/// 물건이라, 남이 만든 코드를 받아 읽는 것이 전부다.
/// </summary>
public class PresetCodeTests
{
    /// <summary>게임 <c>BuildCompactPresetData</c>가 만드는 줄 순서 그대로다.</summary>
    private static string Plain(string extra = "") =>
        "AAP1\nW:503\nC:PinkRabbit\nS:\n" + extra;

    private static BuildPreset Parse(string plain)
    {
        Assert.True(PresetCode.TryParse(PresetCode.Encode(plain), out var preset, out var error), error);
        return preset;
    }

    [Fact]
    public void AFavouriteListBecomesTheArtifactsTheBuildWants()
    {
        var preset = Parse(Plain("F:1237,3002,3012\n"));

        Assert.Equal(503, preset.StartingWeaponId);
        Assert.Equal(new[] { 1237, 3002, 3012 }, preset.FavoriteCharms);
    }

    [Fact]
    public void FruitsOfTheSameCategoryAddUp()
    {
        // 과일 꼬치에는 같은 카테고리를 여러 개 꽂을 수 있고, 게임도 합친 값을 보여 준다.
        var preset = Parse(Plain("R:EMBER,2;EMBER,1;FLAMESWORD,-1\n"));

        Assert.Equal(3, preset.CategoryBias["EMBER"]);
        Assert.Equal(-1, preset.CategoryBias["FLAMESWORD"]);
    }

    [Fact]
    public void ACategoryNameIsUnescapedTheWayTheGameWroteIt()
    {
        var preset = Parse(Plain("R:MY%20CATEGORY,2\n"));

        Assert.Equal(2, preset.CategoryBias["MY CATEGORY"]);
    }

    [Fact]
    public void APresetWithNeitherFavouritesNorFruitsIsEmpty()
    {
        // 빈 프리셋을 받아들이되 빌드 신호가 없다는 것은 부르는 쪽이 알 수 있어야 한다.
        var preset = Parse(Plain());

        Assert.True(preset.IsEmpty);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("그냥 아무 글자")]
    [InlineData("AAF_PRESET_OBFZ|v1")]
    [InlineData("AAF_PRESET_OBFZ|v1NotBase64!!")]
    public void JunkIsRejectedWithSomethingToShowTheUser(string code)
    {
        Assert.False(PresetCode.TryParse(code, out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void AnotherFormatsPayloadIsRejectedRatherThanHalfRead()
    {
        Assert.False(PresetCode.TryParse(PresetCode.Encode("HELLO\nW:1\n"), out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void ATruncatedFavouriteListIsRejectedRatherThanPartlyKept()
    {
        // 절반만 읽어 들이면 남의 빌드를 잘못 읽은 채로 추천이 돌아간다.
        Assert.False(PresetCode.TryParse(PresetCode.Encode(Plain("F:1237,망가짐\n")), out var preset, out _));
        Assert.Empty(preset.FavoriteCharms);
    }
}

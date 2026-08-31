using SephPlanner.Core.Model;

namespace SephPlanner.Tests;

/// <summary>
/// 게임이 만들어 주는 효과 설명에는 TextMeshPro 서식이 섞여 있다. 오버레이는 그 문법을 모르고,
/// 색은 Theme 에서만 가져오기로 했으므로 걷어내고 문장만 남긴다.
/// </summary>
public class RichTextTests
{
    [Theory]
    [InlineData("<color=yellow>+3</color> 물리 피해", "+3 물리 피해")]
    [InlineData("블록 시 <sprite=\"Keyword\" name=\"Warning\"> 발동", "블록 시 발동")]
    [InlineData("<indent=1em>줄</indent>", "줄")]
    [InlineData("<b><i>겹친 태그</i></b>", "겹친 태그")]
    public void FormattingIsRemovedButTheSentenceStays(string raw, string expected)
    {
        Assert.Equal(expected, RichText.Strip(raw));
    }

    [Fact]
    public void TheGapLeftByATagDoesNotBecomeTwoSpaces()
    {
        Assert.Equal("가 나", RichText.Strip("가 <color=red></color> 나"));
    }

    [Fact]
    public void AComparisonSignIsNotMistakenForATag()
    {
        // 짝이 없는 부등호를 태그의 끝으로 읽으면 앞뒤 문장이 통째로 사라진다.
        Assert.Equal("레벨 5 > 이상이면", RichText.Strip("레벨 5 > 이상이면"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NothingInNothingOut(string? raw)
    {
        Assert.Equal("", RichText.Strip(raw));
    }
}

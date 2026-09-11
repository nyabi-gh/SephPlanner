using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 점수를 견줄 때의 해상도. 환산율에서 나온 점수는 잰 값이라 오차가 있고, 그 오차보다 작은
/// 차이가 아래 단계를 눌러서는 안 된다.
/// </summary>
public class ScoreResolutionTests
{
    /// <summary>이 검사의 눈금. 카탈로그에서 재어 온 값이 무엇이든 규칙은 같아야 한다.</summary>
    private const double Step = 0.05;

    private static PlacementQuality Quality(double value, int unsafeEmpty = 0, double familiarity = 0) =>
        new PlacementQuality(0, 0, 0, 0, 0, value, unsafeEmpty, 0, familiarity, Step);

    /// <summary>
    /// 눈금보다 작은 점수 차이는 감점 빈칸 정리를 이기지 못한다. 이 자리가 원래 문제였다 -
    /// 눈금이 없을 때는 소수점 아래 차이로도 빈칸 정리와 자리 유지가 전부 밀렸다.
    /// </summary>
    [Fact]
    public void ScoreNoiseNoLongerOutranksClearingPenaltyCells()
    {
        var noisierButTidy = Quality(10.01, unsafeEmpty: 0);
        var noisierAndMessy = Quality(10.04, unsafeEmpty: 1);

        Assert.True(noisierButTidy.CompareTo(noisierAndMessy) > 0);
    }

    /// <summary>
    /// 실제 레벨 한 칸의 차이는 눈금보다 크므로 그대로 이긴다. 눈금은 카탈로그에서 가장 작은
    /// 이웃 레벨 차이의 절반이라 언제나 그렇다.
    /// </summary>
    [Fact]
    public void ARealLevelStepStillWinsOverATidierBoard()
    {
        var better = Quality(10.01 + Step * 2, unsafeEmpty: 1);
        var tidier = Quality(10.01, unsafeEmpty: 0);

        Assert.True(better.CompareTo(tidier) > 0);
    }

    /// <summary>
    /// 눈금으로 끊는 이유. 허용 오차(<c>|a-b| &lt; 0.05</c>)였다면 a≈b, b≈c 인데 a&lt;c 가 되어
    /// 같은 후보 목록도 비교 순서에 따라 다르게 정렬된다.
    /// </summary>
    [Fact]
    public void TiesStayTransitive()
    {
        var a = Quality(10.01);
        var b = Quality(10.04);
        var c = Quality(10.07);

        Assert.Equal(0, a.CompareTo(b));
        Assert.True(b.CompareTo(c) < 0);
        Assert.True(a.CompareTo(c) < 0);
    }

    /// <summary>점수가 같은 칸에 들어가면 그다음 단계가 답을 낸다.</summary>
    [Fact]
    public void WithinOneStepTheLowerStagesDecide()
    {
        var stayPut = Quality(10.01, familiarity: 1);
        var moved = Quality(10.04, familiarity: 0);

        Assert.True(stayPut.CompareTo(moved) > 0);
    }
}

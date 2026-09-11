using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public sealed class GrowthWorthTests
{
    [Theory]
    [InlineData(null, 50, 0)]
    [InlineData(0, 50, 0)]
    [InlineData(9, 50, 0)]
    [InlineData(10, 50, 1)]
    [InlineData(25, 50, 2)]
    [InlineData(49, 50, 4)]
    [InlineData(50, 50, 5)]
    [InlineData(80, 50, 5)]
    public void ProgressFallsIntoFiveSteps(int? progress, int goal, int bucket) =>
        Assert.Equal(bucket, GrowthWorth.Bucket(progress, goal));

    [Fact]
    public void UnknownProgressAndAbsentGoalNeverCount()
    {
        // 참가자 자리에서 아직 표시값이 오지 않은 것을 다 키운 것처럼 쳐 주면 안 된다.
        Assert.Equal(0, GrowthWorth.Bucket(null, 50));
        Assert.Equal(0, GrowthWorth.Bucket(40, 0));
        Assert.Equal(0, GrowthWorth.Bucket(-3, 50));
    }

    [Fact]
    public void WorthClimbsTowardsWhatTheItemBecomes()
    {
        // 실제 1.0.31 값이다. 빛바랜 방패 문장 -> 철벽의 문장.
        var faded = Table(1.5384615384615385, 3.076923076923077, 4.615384615384615);
        var iron = Table(4.615384615384615, 6.153846153846154, 7.6923076923076925);

        Assert.Equal(faded.At(0), GrowthWorth.Project(faded, iron, 0, 2).At(0), 9);

        var half = GrowthWorth.Project(faded, iron, 2, 2);
        Assert.Equal(1.5384615384615385 + (4.615384615384615 - 1.5384615384615385) * 0.4, half.At(0), 9);

        var done = GrowthWorth.Project(faded, iron, GrowthWorth.Steps, 2);
        Assert.Equal(iron.At(0), done.At(0), 9);
        Assert.Equal(iron.At(2), done.At(2), 9);
    }

    [Fact]
    public void ProjectionNeverLowersTheCurrentWorth()
    {
        // 대상이 더 싸게 매겨졌더라도 지금 이 아이템인 것은 사실이다.
        var rich = Table(5, 6, 7);
        var poor = Table(1, 1, 1);
        var projected = GrowthWorth.Project(rich, poor, GrowthWorth.Steps, 2);
        for (var level = 0; level <= 2; level++) Assert.Equal(rich.At(level), projected.At(level), 9);
    }

    [Fact]
    public void EvidenceFollowsTheWeakerSide()
    {
        var measured = Table(1, 2, 3);
        measured.Source = CharmWorthSource.Measured;
        measured.Confidence = 1;
        var guessed = Table(4, 5, 6);
        guessed.Source = CharmWorthSource.Rarity;
        guessed.Confidence = 0;

        var projected = GrowthWorth.Project(measured, guessed, 3, 2);
        Assert.Equal(CharmWorthSource.Rarity, projected.Source);
        Assert.Equal(0, projected.Confidence);

        // 섞지 않았으면 원래 근거가 그대로 남는다.
        Assert.Equal(CharmWorthSource.Measured, GrowthWorth.Project(measured, guessed, 0, 2).Source);
    }

    private static CharmWorth Table(params double[] byLevel) =>
        new CharmWorth { ByLevel = byLevel, Source = CharmWorthSource.Measured, Confidence = 1 };
}

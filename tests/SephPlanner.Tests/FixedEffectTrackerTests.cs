using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 되뺀 층은 정의상 행렬과 맞으므로, 채택 여부는 "아이템을 옮겨도 그대로인가"로 가른다.
/// </summary>
public class FixedEffectTrackerTests
{
    private static FixedEffectResidualResult Extracted(params (int X, int Y, int Level)[] cells)
    {
        var result = new FixedEffectResidualResult { Status = FixedEffectResidualStatus.Extracted };
        foreach (var cell in cells)
            result.Cells.Add(new FixedEffectCell { Position = new GridPos(cell.X, cell.Y), Level = cell.Level });
        return result;
    }

    [Fact]
    public void TheFirstReadingIsAdopted()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((0, 0, 1)), sources: "t@0,0", arrangement: "a");

        Assert.True(tracker.Trustworthy);
        Assert.Equal("", tracker.Reason);
        Assert.Single(tracker.Layer);
    }

    [Fact]
    public void StayingTheSameAcrossArrangementsConfirmsIt()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");
        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "b");

        Assert.True(tracker.ConfirmedAcrossArrangements);
        Assert.True(tracker.Trustworthy);
        Assert.False(tracker.Contradicted);
    }

    [Fact]
    public void ChangingWithTheArrangementIsReportedAsOurError()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");
        tracker.Observe(Extracted((0, 0, 2)), "t@0,0", "b");
        Assert.False(tracker.Contradicted);

        tracker.Observe(Extracted((0, 0, 3)), "t@0,0", "c");

        Assert.True(tracker.Contradicted);
        Assert.False(tracker.Trustworthy);
        Assert.Contains("배치", tracker.Reason);
    }

    /// <summary>
    /// 한 번 튄 뒤 확정된 층으로 돌아오는 것은 두 번째 어긋남이 아니다. 예전에는 튄 값을 기준으로
    /// 삼아 돌아오는 것까지 세었다(제보 5642c7be).
    /// </summary>
    [Fact]
    public void OneGlitchThatComesBackIsNotAContradiction()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((1, 4, 2)), "t@0,0", "a");
        tracker.Observe(Extracted((1, 4, 1)), "t@0,0", "b");
        Assert.Contains("1/2", tracker.Note);
        tracker.Observe(Extracted((1, 4, 2)), "t@0,0", "c");

        Assert.False(tracker.Contradicted);
        Assert.True(tracker.Trustworthy);
        Assert.Contains("돌아왔", tracker.Note);
        Assert.Equal(2, Assert.Single(tracker.Layer).Level);

        // 돌아온 뒤에는 셈이 처음부터다.
        tracker.Observe(Extracted((1, 4, 1)), "t@0,0", "d");
        Assert.False(tracker.Contradicted);
    }

    /// <summary>
    /// 벗어난 값이 아이템을 옮긴 뒤에도 그대로면 새 고정 효과다. 보상 석판을 바로 각인한 것이
    /// 아이템 이동과 겹치면 이렇게 보인다.
    /// </summary>
    [Fact]
    public void ANewLayerThatHoldsAcrossAMoveIsConfirmed()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");
        tracker.Observe(Extracted((0, 0, 1), (1, 4, 2)), "t@0,0", "b");
        tracker.Observe(Extracted((0, 0, 1), (1, 4, 2)), "t@0,0", "c");

        Assert.False(tracker.Contradicted);
        Assert.True(tracker.ConfirmedAcrossArrangements);
        Assert.Contains("확정", tracker.Note);

        // 확정된 새 층에서 한 번 벗어나는 것은 다시 첫 번째다.
        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "d");
        Assert.False(tracker.Contradicted);
    }

    [Fact]
    public void AContradictionIsNotClearedByComingBack()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");
        tracker.Observe(Extracted((0, 0, 2)), "t@0,0", "b");
        tracker.Observe(Extracted((0, 0, 3)), "t@0,0", "c");
        Assert.True(tracker.Contradicted);

        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "d");

        Assert.True(tracker.Contradicted);
        Assert.False(tracker.Trustworthy);
    }

    [Fact]
    public void AnEngravedTabletChangesTheLayerWithoutBlame()
    {
        var tracker = new FixedEffectTracker();

        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");
        tracker.Observe(Extracted((0, 0, 1), (1, 0, 2)), sources: "", arrangement: "b");
        tracker.Observe(Extracted((0, 0, 1), (1, 0, 2)), sources: "", arrangement: "c");

        Assert.False(tracker.Contradicted);
        Assert.True(tracker.Trustworthy);
        Assert.Equal(2, tracker.Layer.Count);
    }

    [Fact]
    public void AMidTransactionReadingKeepsTheLayer()
    {
        var tracker = new FixedEffectTracker();
        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");

        tracker.Observe(
            new FixedEffectResidualResult { Status = FixedEffectResidualStatus.Unsettled, Reason = "정리 중" },
            "t@0,0", "b");

        Assert.True(tracker.Trustworthy);
        Assert.Single(tracker.Layer);
        Assert.Equal("정리 중", tracker.Reason);
    }

    [Fact]
    public void OverproductionIsNeverAbsorbed()
    {
        var tracker = new FixedEffectTracker();
        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");

        tracker.Observe(
            new FixedEffectResidualResult { Status = FixedEffectResidualStatus.Overproduced, Reason = "칸 (0, 0)" },
            "t@0,0", "b");

        Assert.True(tracker.Contradicted);
        Assert.False(tracker.Trustworthy);
    }

    [Fact]
    public void ANewRunStartsFromNothing()
    {
        var tracker = new FixedEffectTracker();
        tracker.Observe(Extracted((0, 0, 1)), "t@0,0", "a");

        tracker.Reset();

        Assert.Empty(tracker.Layer);
        Assert.False(tracker.Trustworthy);
    }
}

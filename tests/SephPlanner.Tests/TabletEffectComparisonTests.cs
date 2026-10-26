using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 게임은 레벨 더하기와 배수에서만 <c>levelParam</c> 을 읽는다. 나머지 종류의 수치는 행렬에 닿지 않는다.
/// </summary>
public class TabletEffectComparisonTests
{
    private static readonly GridPos Cell = new(5, 2);

    private static QueryCell[] Ours(string value) => new[] { new QueryCell(Cell, value) };

    private static AppliedTabletEffect[] Game(TabletEffectKind kind, int levelParam) =>
        new[] { new AppliedTabletEffect(Cell, kind, levelParam) };

    /// <summary>모드 석판이 <c>None</c> 칸 수치에 자기 번호를 심었다(제보 6c1abf75).</summary>
    [Fact]
    public void AModMarkerOnAnEffectThatReadsNoNumberIsNotADifference()
    {
        Assert.Null(TabletEffectComparison.FirstDifference(
            Ours("NEPR_TWIN"), Game(TabletEffectKind.None, 300045)));
        Assert.Null(TabletEffectComparison.FirstDifference(
            Ours("X"), Game(TabletEffectKind.Disable, 7)));
        Assert.Null(TabletEffectComparison.FirstDifference(
            Ours("IGNORECRITERIA"), Game(TabletEffectKind.IgnoreCriteria, 7)));
    }

    [Fact]
    public void NumbersTheGameAppliesMustStillMatch()
    {
        Assert.Equal("효과 [0] 수치 2 != 3", TabletEffectComparison.FirstDifference(
            Ours("2"), Game(TabletEffectKind.IncreaseConstLevel, 3)));
        Assert.Equal("효과 [0] 수치 2 != 3", TabletEffectComparison.FirstDifference(
            Ours("MUL/2"), Game(TabletEffectKind.MultiplyConstLevel, 3)));
        Assert.Null(TabletEffectComparison.FirstDifference(
            Ours("-2"), Game(TabletEffectKind.IncreaseConstLevel, -2)));
    }

    [Fact]
    public void KindCountAndPositionAreAlwaysCompared()
    {
        Assert.Equal("효과 [0] 종류 None != IncreaseConstLevel", TabletEffectComparison.FirstDifference(
            Ours("NEPR_TWIN"), Game(TabletEffectKind.IncreaseConstLevel, 0)));
        Assert.Equal("효과 칸 수 1 != 0", TabletEffectComparison.FirstDifference(
            Ours("1"), Array.Empty<AppliedTabletEffect>()));
        Assert.Equal("효과 [0] 위치 불일치", TabletEffectComparison.FirstDifference(
            Ours("1"), new[] { new AppliedTabletEffect(new GridPos(0, 0), TabletEffectKind.IncreaseConstLevel, 1) }));
    }
}

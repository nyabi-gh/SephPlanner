using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public class ComboCatalogBuilderTests
{
    [Fact]
    public void SpecialActivationChangesCompletionAtTheMissingThreshold()
    {
        var combo = ComboCatalogBuilder.Build("SPECIAL", new[] { 4, 6 }, () =>
            new[] { new ComboEffectLine { Threshold = 2, Text = "특수 효과" } });

        Assert.Equal(new[] { 2, 4, 6 }, combo.Thresholds);
        Assert.Equal(Worth.ComboThreshold, Worth.OfComboStep(combo, 1, out var completes, out var goal));
        Assert.True(completes);
        Assert.Equal(2, goal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void ATextlessSpecialOnlyComboStillHasAnActivationThreshold(string? text)
    {
        var combo = ComboCatalogBuilder.Build("SPECIAL_ONLY", Array.Empty<int>(), () =>
            new[] { new ComboEffectLine { Threshold = 3, Text = text! } });

        Assert.Equal(new[] { 3 }, combo.Thresholds);
        Assert.Empty(combo.Effects);
    }

    [Fact]
    public void SourcesMergeWithoutDuplicatingStagesOrLosingDescriptions()
    {
        var reads = 0;
        var combo = ComboCatalogBuilder.Build("MERGED", new[] { 6, 4, 4, 1, 0, -1 }, () =>
        {
            reads++;
            return new[]
            {
                new ComboEffectLine { Threshold = 6, Text = "마지막" },
                new ComboEffectLine { Threshold = 4, Text = "일반" },
                new ComboEffectLine { Threshold = 2, Text = "특수" },
                new ComboEffectLine { Threshold = 4, Text = "추가" },
                new ComboEffectLine { Threshold = 0, Text = "기본 설명" },
            };
        });

        Assert.Equal(1, reads);
        Assert.Equal(new[] { 1, 2, 4, 6 }, combo.Thresholds);
        Assert.Equal(new[] { "특수", "일반", "추가", "마지막" }, combo.Effects.Select(effect => effect.Text));
    }

    [Fact]
    public void LegacyOnlyCategoriesDoNotRequireAPrefabEffect()
    {
        var combo = ComboCatalogBuilder.Build("LEGACY", new[] { 2, 5 }, () => Array.Empty<ComboEffectLine>());
        Assert.Equal(new[] { 2, 5 }, combo.Thresholds);
    }

    [Fact]
    public void AFailedGameRequestDoesNotReturnAStaticOnlyCatalog()
    {
        var cause = new InvalidOperationException("게임 설명 생성 실패");
        var error = Assert.Throws<InvalidDataException>(() =>
            ComboCatalogBuilder.Build("FAILED", new[] { 4, 6 }, () => throw cause));

        Assert.Same(cause, error.InnerException);
        Assert.Contains("FAILED", error.Message);
    }

    [Fact]
    public void AnInterruptedEnumerationDoesNotReturnTheAlreadyReadStages()
    {
        Assert.Throws<InvalidDataException>(() =>
            ComboCatalogBuilder.Build("PARTIAL", new[] { 4 }, Interrupted));
    }

    private static IEnumerable<ComboEffectLine> Interrupted()
    {
        yield return new ComboEffectLine { Threshold = 2, Text = "일부만 읽힘" };
        throw new InvalidOperationException("나머지 읽기 실패");
    }
}

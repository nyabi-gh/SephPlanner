using SephPlanner.Core.Planning;
using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class PluginPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SephPlanner-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private PluginPreferences Load() => PluginPreferences.Load(_ => { }, SettingsPath);
    private static string Code(string categories) => PresetCode.Encode("AAP1\nW:503\nC:PinkRabbit\nS:\nR:" + categories + "\n");

    [Fact]
    public void ResetBuildPersistsDefaultsAndInvalidatesPreviousPlan()
    {
        var prefs = Load();
        Assert.True(prefs.TryImport(Code("FIRST,1;SECOND,1"), out _));
        prefs.TogglePriority("FIRST");
        prefs.TogglePriority("MANUAL");
        prefs.StepPin(1157, 1);
        prefs.StepPin(1168, -1);
        prefs.ToggleHold(1157);
        prefs.ToggleRetain(1168);
        prefs.PinnedCharms.Add(999);
        var revision = prefs.Revision;
        var before = SephPlanner.Core.Runtime.PlanFingerprint.PlanningContext(prefs.ToPreferences(true), "catalog");

        prefs.ResetBuild();

        Assert.Equal(revision + 1, prefs.Revision);
        var defaults = SephPlanner.Core.Runtime.PlanFingerprint.PlanningContext(new PluginPreferences().ToPreferences(true), "catalog");
        Assert.NotEqual(before, defaults);
        foreach (var actual in new[] { prefs, Load() })
        {
            Assert.Null(actual.PresetCode);
            Assert.Null(actual.Preset());
            Assert.Empty(actual.SuppressedPresetCategories);
            Assert.Empty(actual.PinnedCharms);
            Assert.Empty(actual.StorageMessage);
            Assert.Equal(defaults, SephPlanner.Core.Runtime.PlanFingerprint.PlanningContext(actual.ToPreferences(true), "catalog"));
        }

        Assert.True(prefs.TryImport(Code("FIRST,1;SECOND,1"), out _));
        Assert.True(prefs.IsPriority("FIRST"));
    }

    [Fact]
    public void RetentionPersistsAndInvalidatesPlanningContext()
    {
        var prefs = Load();
        var before = SephPlanner.Core.Runtime.PlanFingerprint.PlanningContext(prefs.ToPreferences(false), "catalog");
        prefs.ToggleRetain(1157);
        Assert.True(Load().IsRetained(1157));
        var after = SephPlanner.Core.Runtime.PlanFingerprint.PlanningContext(prefs.ToPreferences(false), "catalog");
        Assert.NotEqual(before, after);
        prefs.ToggleRetain(1157);
        Assert.False(Load().IsRetained(1157));
    }

    [Fact]
    public void ReplacingAndClearingPresetPreservesOnlyManualPriorities()
    {
        var prefs = Load();
        prefs.TogglePriority("MANUAL");
        Assert.True(prefs.TryImport(Code("FIRST,1;MANUAL,1"), out _));
        Assert.True(prefs.IsPriority("FIRST"));
        Assert.True(prefs.TryImport(Code("SECOND,1"), out _));
        Assert.False(prefs.IsPriority("FIRST"));
        Assert.True(prefs.IsPriority("SECOND"));
        Assert.True(prefs.IsPriority("MANUAL"));
        prefs.ClearPreset();
        Assert.Equal(new[] { "MANUAL" }, Load().EffectivePriorityCategories());
    }

    [Fact]
    public void TurningOffImportedCategorySurvivesReloadAndNewImportResetsIt()
    {
        var prefs = Load();
        prefs.TryImport(Code("FIRST,1"), out _);
        prefs.TogglePriority("FIRST");
        prefs = Load();
        Assert.False(prefs.IsPriority("FIRST"));
        prefs.TryImport(Code("FIRST,1"), out _);
        Assert.True(prefs.IsPriority("FIRST"));
    }

    [Fact]
    public void ExplicitlyReenablingCategoryMakesItManual()
    {
        var prefs = Load();
        prefs.TryImport(Code("FIRST,1"), out _);
        prefs.TogglePriority("FIRST");
        prefs.TogglePriority("FIRST");
        prefs.ClearPreset();
        Assert.True(Load().IsPriority("FIRST"));
    }

    [Fact]
    public void InvalidImportDoesNotReplaceCurrentPreset()
    {
        var prefs = Load();
        prefs.TryImport(Code("FIRST,1"), out _);
        Assert.False(prefs.TryImport("invalid", out _));
        Assert.True(prefs.IsPriority("FIRST"));
    }

    [Fact]
    public void UnsupportedNegativeBiasIsExplicitlyReported()
    {
        var prefs = Load();
        Assert.True(prefs.TryImport(Code("FIRST,-1"), out var message));
        Assert.Contains("미반영", message);
        Assert.False(prefs.IsPriority("FIRST"));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    public void UnreadableSettingsArePreservedBeforeSaving(string content)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, content);
        var prefs = Load();
        Assert.NotEmpty(prefs.StorageMessage);
        Assert.Equal(content, File.ReadAllText(SettingsPath));
        prefs.TogglePriority("NEW");
        Assert.Empty(prefs.StorageMessage);
        Assert.Equal(content, File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "*.bak"))));
        Assert.True(Load().IsPriority("NEW"));
    }

    [Fact]
    public void FailedSaveKeepsMemoryAndSuccessfulRetryClearsWarning()
    {
        Directory.CreateDirectory(SettingsPath);
        var prefs = Load();
        prefs.TogglePriority("FIRST");
        Assert.True(prefs.IsPriority("FIRST"));
        Assert.NotEmpty(prefs.StorageMessage);
        Directory.Delete(SettingsPath);
        prefs.TogglePriority("SECOND");
        Assert.Empty(prefs.StorageMessage);
        Assert.True(Load().IsPriority("FIRST"));
        Assert.True(Load().IsPriority("SECOND"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

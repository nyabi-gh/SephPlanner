using SephPlanner.Core.Planning;
using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class PluginPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SephPlanner-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private PluginPreferences Load() => PluginPreferences.Load(_ => { }, SettingsPath);

    [Fact]
    public void CombatSettingsRoundTripWithoutDuplicatingDefaultActionsAndFreezeForWorkers()
    {
        var prefs = Load();
        var scenario = prefs.Combat.Copy();
        scenario.WeaponSequence = new() { SephPlanner.Core.Combat.CombatActionKind.Dash, SephPlanner.Core.Combat.CombatActionKind.Basic };
        scenario.DurationSeconds = 45;
        scenario.TargetCount = 4;
        scenario.AdditionalTargetFraction = 0.3;
        scenario.ComparisonWindowSeconds = 8;
        scenario.PrioritizeBuild = true;
        scenario.AllowUnsupportedChanges = true;
        scenario.ScalesSide = SephPlanner.Core.Model.HorizontalSide.Right;
        scenario.MeasuredWeaponKey = "weapon:1:Basic:0";
        scenario.MeasuredActionSeconds["Basic"] = 0.8;
        scenario.MagicPriority.AddRange(new[] { 10, 20 });
        prefs.SetCombat(scenario);
        scenario.MagicPriority.Clear();
        var submitted = prefs.ToPreferences(false);
        var restored = Load();
        Assert.Equal(45, restored.Combat.DurationSeconds);
        Assert.Equal(4, restored.Combat.TargetCount);
        Assert.Equal(0.3, restored.Combat.AdditionalTargetFraction);
        Assert.Equal(8, restored.Combat.ComparisonWindowSeconds);
        Assert.True(restored.Combat.PrioritizeBuild);
        Assert.True(restored.Combat.AllowUnsupportedChanges);
        Assert.Equal(SephPlanner.Core.Model.HorizontalSide.Right, restored.Combat.ScalesSide);
        Assert.Equal(SephPlanner.Core.Model.HorizontalSide.Right, submitted.Combat.ScalesSide);
        Assert.Equal("weapon:1:Basic:0", restored.Combat.MeasuredWeaponKey);
        Assert.Equal(0.8, restored.Combat.MeasuredActionSeconds["Basic"]);
        Assert.Equal(2, restored.Combat.WeaponSequence.Count);
        Assert.Equal(new[] { 10, 20 }, restored.Combat.MagicPriority);
        restored.SetCombat(restored.Combat);
        Assert.Equal(2, Load().Combat.WeaponSequence.Count);
        prefs.Combat.MagicPriority.Clear();
        prefs.Combat.MeasuredActionSeconds.Clear();
        Assert.Equal(new[] { 10, 20 }, submitted.Combat.MagicPriority);
        Assert.Equal(0.8, submitted.Combat.MeasuredActionSeconds["Basic"]);
        restored.ResetBuild();
        Assert.Equal(30, Load().Combat.DurationSeconds);
        Assert.Single(Load().Combat.WeaponSequence);
        Assert.False(Load().Combat.PrioritizeBuild);
        Assert.False(Load().Combat.AllowUnsupportedChanges);
        Assert.Equal(SephPlanner.Core.Model.HorizontalSide.Automatic, Load().Combat.ScalesSide);
    }

    [Fact]
    public void ExistingSettingsKeepBuildSelectionsButDefaultToDpsAndRestrictedChanges()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(SettingsPath, "{\"PriorityCategories\":[\"FIRE\"],\"Combat\":{\"DurationSeconds\":30}}");
        var restored = Load();
        Assert.Contains("FIRE", restored.PriorityCategories);
        Assert.False(restored.Combat.PrioritizeBuild);
        Assert.False(restored.Combat.AllowUnsupportedChanges);
    }

    [Fact]
    public void DeactivationPermissionIsSeparateFromYieldAndResetsWithTheBuild()
    {
        var prefs = Load();
        prefs.StepPin(9, -1);
        Assert.False(prefs.IsDeactivationAllowed(9));
        prefs.ToggleDeactivation(9);
        var restored = Load();
        Assert.True(restored.IsDeactivationAllowed(9));
        Assert.Equal(-1, restored.PinLevel(9));
        Assert.Contains(9, restored.ToPreferences(false).DeactivationAllowed);
        restored.ResetBuild();
        Assert.False(Load().IsDeactivationAllowed(9));
    }
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

using Newtonsoft.Json.Linq;
using SephPlanner.Core.Planning;
using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class PluginPreferencesTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SephPlanner-tests-" + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(_directory, "settings.json");

    private PluginPreferences Load() => PluginPreferences.Load(_ => { }, SettingsPath);

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
    [Fact]
    public void TheLevelCapCyclesFromZeroToJustBelowTheMaximum()
    {
        var prefs = Load();
        var forward = new List<int?>();
        for (var i = 0; i < 5; i++)
        {
            prefs.StepLevelCap(3, 1, 4);
            forward.Add(prefs.LevelCap(3));
        }
        Assert.Equal(new int?[] { 0, 1, 2, 3, null }, forward);

        prefs.StepLevelCap(3, -1, 4);
        Assert.Equal(3, prefs.LevelCap(3));
        for (var i = 0; i < 3; i++) prefs.StepLevelCap(3, -1, 4);
        Assert.Equal(0, Load().LevelCap(3));
        Assert.Equal(0, Load().ToPreferences(false).LevelCaps[3]);
        prefs.StepLevelCap(3, -1, 4);
        Assert.Null(prefs.LevelCap(3));

        prefs.StepLevelCap(4, 1, 1);
        Assert.Equal(0, prefs.LevelCap(4));
        prefs.StepLevelCap(4, 1, 1);
        Assert.Null(prefs.LevelCap(4));
    }

    [Fact]
    public void ANewRunForgetsDesignationsForItemsNotInTheBagButKeepsTheBuild()
    {
        var prefs = Load();
        prefs.StepPin(1, 1);
        prefs.StepPin(2, -1);
        prefs.StepLevelCap(3, 1, 5);
        prefs.ToggleHold(4);
        prefs.ToggleSupportTarget(5);
        prefs.ToggleRetain(6);
        prefs.ToggleDeactivation(7);
        prefs.StepPin(8, 1);
        prefs.ToggleRetain(8);
        prefs.TogglePriority("MAGITECH");

        Assert.Equal(7, prefs.ForgetAbsent(new HashSet<int> { 8 }));

        var restored = Load();
        var planning = restored.ToPreferences(false);
        Assert.Equal(new Dictionary<int, int> { [8] = 1 }, planning.PinnedCharms);
        Assert.Equal(new HashSet<int> { 8 }, planning.RetainedCharms);
        Assert.Empty(planning.LevelCaps);
        Assert.Empty(planning.HeldCharms);
        Assert.Empty(planning.SupportTargets);
        Assert.Empty(planning.DeactivationAllowed);
        Assert.True(restored.IsPriority("MAGITECH"));
    }

    [Fact]
    public void ANewRunWithNothingToForgetDoesNotRewriteTheSettings()
    {
        var prefs = Load();
        prefs.StepPin(1, 1);
        var revision = prefs.Revision;

        Assert.Equal(0, prefs.ForgetAbsent(new HashSet<int> { 1 }));
        Assert.Equal(revision, prefs.Revision);
    }

    [Fact]
    public void ASupportTargetSurvivesAReloadAndClearsWithTheBuild()
    {
        var prefs = Load();
        Assert.False(prefs.IsSupportTarget(3002));
        prefs.ToggleSupportTarget(3002);

        var restored = Load();
        Assert.True(restored.IsSupportTarget(3002));
        Assert.Contains(3002, restored.ToPreferences(false).SupportTargets);

        restored.ToggleSupportTarget(3002);
        Assert.False(Load().IsSupportTarget(3002));

        restored.ToggleSupportTarget(3002);
        restored.ResetBuild();
        Assert.Empty(Load().ToPreferences(false).SupportTargets);
    }

    [Fact]
    public void NewerSettingsSurviveEditingAndResetWithoutAffectingLegacyPlanning()
    {
        Directory.CreateDirectory(_directory);
        const string original = """{"PinnedLevels":{"1114":2},"Combat":{"PreserveActivation":false,"ScalesSide":1},"MinimumLevels":{"1114":3},"FutureSetting":[1,null,"값"]}""";
        File.WriteAllText(SettingsPath, original);
        var source = JObject.Parse(original);
        var prefs = Load();
        Assert.Equal(2, prefs.ToPreferences(false).PinnedCharms[1114]);
        prefs.StepPin(1114, 1);
        Assert.Equal(3, Load().PinLevel(1114));
        foreach (var name in new[] { "Combat", "MinimumLevels", "FutureSetting" })
            Assert.True(JToken.DeepEquals(source[name], JObject.Parse(File.ReadAllText(SettingsPath))[name]));
        prefs.ResetBuild();
        Assert.Empty(Load().ToPreferences(false).PinnedCharms);
        foreach (var name in new[] { "Combat", "MinimumLevels", "FutureSetting" })
            Assert.True(JToken.DeepEquals(source[name], JObject.Parse(File.ReadAllText(SettingsPath))[name]));
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

using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.DataTool;

namespace SephPlanner.Tests;

public sealed class PlanChurnTests
{
    [Fact]
    public void ReplayChurnPreservesInputsAndRebuildsMovedStatsAndExternalComboCounts()
    {
        var replay = Capture();
        var original = JsonSerializer.Serialize(replay);
        var result = PlanChurn.Analyze(replay, 4);
        Assert.Equal(ChurnStatus.Converged, result.Status);
        Assert.Equal(2, result.Plans.Count);
        Assert.Equal(110, result.Plans[0].Current.Score);
        Assert.Equal(220, result.Plans[1].Current.Score);
        Assert.Equal(result.Plans[0].Best.Score, result.Plans[1].Best.Score);
        Assert.Empty(replay.Expected!.Differences(ReplayResult.From(result.Plans[0])));
        Assert.Equal(original, JsonSerializer.Serialize(replay));
    }

    [Theory]
    [InlineData(false, ChurnStatus.Blocked)]
    [InlineData(true, ChurnStatus.Converged)]
    public void ReplayChurnHonorsUnsupportedChangePermission(bool allowed, ChurnStatus expected)
    {
        var replay = Capture(unknown: true, allowUnsupported: allowed);
        var result = PlanChurn.Analyze(replay, 4);
        Assert.Equal(expected, result.Status);
        if (!allowed)
        {
            Assert.Single(result.Plans);
            Assert.Contains("미지원", result.Reason);
        }
    }

    [Fact]
    public void ReachingTheRoundLimitIsNotReportedAsConvergence()
    {
        var result = PlanChurn.Analyze(Capture(), 1);
        Assert.Equal(ChurnStatus.LimitReached, result.Status);
    }

    [Fact]
    public void InvalidObservedLevelsBlockTheOfflineApply()
    {
        var result = PlanChurn.Analyze(Capture(invalidLevels: true), 4);
        Assert.Equal(ChurnStatus.Blocked, result.Status);
        Assert.Single(result.Plans);
        Assert.False(result.Plans[0].Verification.Passed);
    }

    [Fact]
    public void ChangedReplayInputsStillFailFingerprintValidation()
    {
        var replay = Capture();
        replay.Preferences!.RetainedCharms!.Add(999);
        Assert.Throws<InvalidDataException>(() => PlanChurn.Analyze(replay, 4));
    }

    [Fact]
    public void ChurnCommandReportsConvergenceLimitsAndInvalidInputsWithDifferentExitCodes()
    {
        var path = Path.Combine(Path.GetTempPath(), "SephPlanner-churn-" + Guid.NewGuid().ToString("N") + ".replay");
        try
        {
            PlanReplayFile.Write(path, JsonSerializer.Serialize(Capture()));
            Assert.Equal(0, PlanChurn.Run(path, 4));
            Assert.Equal(2, PlanChurn.Run(path, 1));
            var changed = Capture();
            changed.Preferences!.Combat!.WeaponSequence = null!;
            PlanReplayFile.Write(path, JsonSerializer.Serialize(changed));
            Assert.Equal(1, PlanChurn.Run(path, 4));
            File.WriteAllText(path, "깨진 재현 자료");
            Assert.Equal(1, PlanChurn.Run(path, 4));
        }
        finally { File.Delete(path); }
    }

    private static PlanReplay Capture(bool unknown = false, bool allowUnsupported = false, bool invalidLevels = false)
    {
        var charm = new CharmDefinition
        {
            EntityId = 1,
            Id = "물리",
            MaxLevel = 1,
            Categories = { "TEST" },
            Combat = new() { Collected = true, Stats = { new() { Key = "PHYSICALDAMAGE", Values = new() { 0, 100 } } } }
        };
        if (unknown) charm.Combat.Unsupported.Add("미지원 동작");
        var data = new ReplayCatalog
        {
            Tablets = new(),
            Charms = new() { charm },
            Combos = new() { new() { Id = "TEST", Thresholds = { 3 }, Combat = new()
            {
                Collected = true, Stats = { new() { Key = "ALLDAMAGEBONUS", Values = new() { 10 }, Threshold = 3 } }
            } } }
        };
        var snapshot = new GameSnapshot
        {
            Run = new()
            {
                Combat = new()
                {
                    ObservedStats = new() { ["PHYSICALDAMAGE"] = 100, ["ALLDAMAGEBONUS"] = 10 },
                    WeaponAttacks = { new() { DamageKind = Core.Combat.CombatDamageKind.Weapon } }
                }
            },
            Inventory = new()
            {
                Width = 2,
                Height = 1,
                Storage = 2,
                ComboCounts = new() { ["TEST"] = 3 },
                FixedEffects = { new() { Position = new(1, 0), Level = 1 } },
                LevelMatrix = new() { ["1,0"] = invalidLevels ? 9 : 1 },
                Items = { new() { InstanceId = 1, DefinitionId = 1, Position = new(0, 0), IsActive = true } }
            }
        };
        var preferences = new PlanPreferences { Recommendations = false, Combat = new() { AllowUnsupportedChanges = allowUnsupported } };
        return new()
        {
            Version = PlanReplay.CurrentVersion,
            CatalogVersion = PlannerData.CatalogVersion,
            CoreBuild = PlanReplay.CurrentCoreBuild,
            CatalogGeneration = "synthetic",
            RequestedGeneration = 1,
            PublishedGeneration = 1,
            Snapshot = snapshot,
            Catalog = data,
            Preferences = ReplayPreferences.From(preferences),
            PreviousTargets = new(),
            RequestFingerprint = PlanFingerprint.Full(snapshot, preferences, "synthetic"),
            Expected = ReplayResult.From(PlanBuilder.Build(snapshot, data.Restore(), preferences)!)
        };
    }
}

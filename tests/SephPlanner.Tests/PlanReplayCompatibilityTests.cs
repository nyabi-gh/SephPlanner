using System.Text.Json;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public sealed class PlanReplayCompatibilityTests
{
    [Theory]
    [InlineData("v5-mono")]
    [InlineData("v5-net10")]
    public void LegacyRuntimeCapturesReplayWithExplicitModelComparison(string fixture)
    {
        var replay = Read(fixture);
        Assert.Equal(5, replay.Version);
        Assert.Throws<InvalidDataException>(() => replay.Rebuild());
        Assert.Empty(replay.Expected!.Differences(ReplayResult.From(replay.Rebuild(true))));
    }

    [Fact]
    public void MonoCanonicalCaptureMatchesDotNetIncludingFractionalSettingsAndCuratedValues()
    {
        var replay = Read("v6-mono");
        Assert.Equal(PlanReplay.CurrentVersion, replay.Version);
        Assert.Equal(replay.RequestFingerprint,
            PlanFingerprint.Full(replay.Snapshot!, replay.Preferences!.Restore(), replay.CatalogGeneration));
        Assert.Empty(replay.Expected!.Differences(ReplayResult.From(replay.Rebuild(true))));
    }

    [Theory]
    [InlineData("v5-mono")]
    [InlineData("v5-net10")]
    [InlineData("v6-mono")]
    public void CompatibilityDoesNotAcceptChangedCombatInputsOrSettings(string fixture)
    {
        var snapshotChange = Read(fixture);
        snapshotChange.Snapshot!.Run!.Combat!.ManaRecoveryDelay += 0.01;
        Assert.Throws<InvalidDataException>(() => snapshotChange.Rebuild(true));
        var scenarioChange = Read(fixture);
        scenarioChange.Preferences!.Combat!.MeasuredActionSeconds["Basic"] += 0.01;
        Assert.Throws<InvalidDataException>(() => scenarioChange.Rebuild(true));
        var valueChange = Read(fixture);
        valueChange.Preferences!.CharmValues!.Charms[0].Base += 0.01;
        Assert.Throws<InvalidDataException>(() => valueChange.Rebuild(true));
    }

    [Theory]
    [InlineData("v5-mono")]
    [InlineData("v5-net10")]
    public void NewFormatDoesNotFallBackToLegacyFingerprints(string fixture)
    {
        var replay = Read(fixture);
        replay.Version = PlanReplay.CurrentVersion;
        Assert.Throws<InvalidDataException>(() => replay.Rebuild(true));
    }

    [Fact]
    public void CanonicalFingerprintsDistinguishAdjacentDoublesButNormalizeZero()
    {
        var replay = Read("v6-mono");
        var preferences = replay.Preferences!.Restore();
        replay.Snapshot!.Run!.Combat!.ManaRecoveryDelay = Math.BitIncrement(replay.Snapshot.Run.Combat.ManaRecoveryDelay);
        Assert.NotEqual(replay.RequestFingerprint, PlanFingerprint.Full(replay.Snapshot, preferences, replay.CatalogGeneration));
        Assert.Equal(CombatFingerprint.Of(0d), CombatFingerprint.Of(-0d));
    }

    private static PlanReplay Read(string fixture)
    {
        using var stream = typeof(PlanReplayCompatibilityTests).Assembly.GetManifestResourceStream(
            "SephPlanner.Tests.Fixtures.replay-" + fixture + ".json")!;
        return JsonSerializer.Deserialize<PlanReplay>(stream)!;
    }
}

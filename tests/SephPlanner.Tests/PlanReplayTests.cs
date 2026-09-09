using System.Text.Json;
using Newtonsoft.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.DataTool;

namespace SephPlanner.Tests;

public sealed class PlanReplayTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SephPlanner-replay-" + Guid.NewGuid().ToString("N"));
    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public void PluginJsonReplaysPublishedInputsIncludingPreferencesCatalogAndAnchor()
    {
        var (catalog, snapshot, preferences) = Inputs();
        using var runner = new PlanRunner(catalog);
        runner.Submit(snapshot, preferences, "saved-catalog");
        Await(runner);
        var first = runner.State.Latest!;
        Assert.True(first.Verification.Passed);
        Assert.NotEmpty(first.Targets);
        var secondSnapshot = RoundTrip(snapshot);
        secondSnapshot.Offers.Add(new OfferedItem { DefinitionId = 4, Kind = "charm", Price = 2 });
        runner.Submit(secondSnapshot, preferences, "saved-catalog");
        Await(runner);
        var capture = runner.CaptureReplay()!;
        Assert.Equal(first.Targets.Count, capture.PreviousTargets!.Count);
        var path = PathOf("saved.replay");
        PlanReplayFile.Write(path, JsonConvert.SerializeObject(capture, Formatting.Indented));
        var restored = System.Text.Json.JsonSerializer.Deserialize<PlanReplay>(PlanReplayFile.Read(path))!;

        Assert.Equal(2, restored.PublishedGeneration);
        Assert.Equal(new GridPos(2, 0), restored.Snapshot!.Inventory!.Items[1].Position);
        Assert.Equal(4, restored.Catalog!.Charms!.Count);
        var prefs = restored.Preferences!.Restore();
        Assert.Equal(2, prefs.PinnedCharms[2]);
        Assert.Equal(-1, prefs.PinnedCharms[3]);
        Assert.Contains(3, prefs.HeldCharms);
        Assert.Contains(2, prefs.RetainedCharms);
        Assert.Contains(4, prefs.PresetCharms);
        Assert.Contains("GLACIER", prefs.PriorityCategories);
        Assert.True(prefs.Recommendations);
        Assert.Equal(7, prefs.CharmValues.Of(catalog.Charm(2)!)!.Base);
        Assert.Equal(PlanFingerprint.Full(secondSnapshot, preferences, "saved-catalog"), restored.RequestFingerprint);
        Assert.Equal(JsonConvert.SerializeObject(first.Targets), JsonConvert.SerializeObject(restored.PreviousTargets));

        // 원본 카탈로그가 바뀌어도 저장된 카탈로그만 사용한다.
        catalog.Charm(2)!.MaxLevel = 0;
        var replayed = restored.Rebuild();
        Assert.Empty(restored.Expected!.Differences(ReplayResult.From(replayed)));
        Assert.Equal(0, PlanReproduce.Run(path));
    }

    [Fact]
    public void CaptureWhileAnotherRequestRunsKeepsThePublishedSnapshotAndPreferences()
    {
        var (catalog, snapshot, preferences) = Inputs();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        PlanBuildOperation build = (GameSnapshot input, ICatalog source, PlanPreferences prefs,
            out PlanBlocker blocker, Plan? previous, CancellationToken cancellation) =>
        {
            if (Interlocked.Increment(ref calls) > 1)
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(10), CancellationToken.None));
            }
            return PlanBuilder.Build(input, source, prefs, out blocker, previous, cancellation);
        };
        using var runner = new PlanRunner(catalog, build);
        runner.Submit(snapshot, preferences, "saved");
        Await(runner);
        try
        {
            var changed = RoundTrip(snapshot);
            changed.Inventory!.Items[0].Enchant = 4;
            runner.Submit(changed, new PlanPreferences { Recommendations = false }, "saved");
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var capture = RoundTrip(runner.CaptureReplay()!);
            Assert.Equal(2, capture.RequestedGeneration);
            Assert.Equal(1, capture.PublishedGeneration);
            Assert.Equal(0, capture.Snapshot!.Inventory!.Items[0].Enchant);
            Assert.Equal(2, capture.Preferences!.PinnedCharms![2]);
            Assert.Empty(capture.Expected!.Differences(ReplayResult.From(capture.Rebuild())));
        }
        finally
        {
            release.Set();
        }
        Await(runner);
        runner.Dispose();
        Assert.Null(runner.CaptureReplay());
    }

    [Fact]
    public void FailedNewRequestKeepsTheIdentityOfTheLastSuccessfulPlan()
    {
        var (catalog, snapshot, preferences) = Inputs();
        PlanBuildOperation build = (GameSnapshot input, ICatalog source, PlanPreferences prefs,
            out PlanBlocker blocker, Plan? previous, CancellationToken cancellation) =>
        {
            blocker = PlanBlocker.None;
            if (input.Run!.Gold == 0) throw new InvalidOperationException("합성 실패");
            return PlanBuilder.Build(input, source, prefs, out blocker, previous, cancellation);
        };
        using var runner = new PlanRunner(catalog, build);
        runner.Submit(snapshot, preferences, "saved");
        Await(runner);
        var changed = RoundTrip(snapshot);
        changed.Run!.Gold = 0;
        changed.Offers.Add(new OfferedItem { Kind = "charm", DefinitionId = 4, Price = 1 });
        runner.Submit(changed, preferences, "saved");
        Assert.True(SpinWait.SpinUntil(() => runner.State.Error is not null, TimeSpan.FromSeconds(10)));
        var capture = RoundTrip(runner.CaptureReplay()!);
        Assert.Equal(1, capture.PublishedGeneration);
        Assert.Equal(2, capture.RequestedGeneration);
        Assert.Equal("합성 실패", capture.LatestError);
        Assert.Empty(capture.Expected!.Differences(ReplayResult.From(capture.Rebuild())));
    }

    [Fact]
    public void ComboChangeWithRecommendationsOffRecalculatesAndBlocksOldPlacement()
    {
        var (catalog, snapshot, preferences) = Inputs();
        preferences.Recommendations = false;
        preferences.PriorityCategories.Clear();
        using var runner = new PlanRunner(catalog);
        runner.Submit(snapshot, preferences, "saved");
        Await(runner);
        var before = runner.State;
        var changed = ReplayPreferences.From(preferences).Restore();
        changed.PriorityCategories.Add("GLACIER");
        var context = new AutoPlaceContext
        {
            Runner = before,
            CatalogVerified = true,
            CatalogGeneration = "saved",
            RuntimeVerification = PlanVerificationStatus.Passed,
            SessionActive = true,
            CurrentPlacementFingerprint = PlanFingerprint.Placement(snapshot, changed, "saved"),
        };
        Assert.Contains("최신 계획", AutoPlacePolicy.Evaluate(context).Reason);
        Assert.Equal(2, runner.Submit(snapshot, changed, "saved"));
        Await(runner);
        Assert.Equal(2, runner.State.Latest!.RequestGeneration);
        Assert.Equal(1, runner.State.Latest.Best.CharmPositions[11].Y);
    }

    [Fact]
    public void CuratedValueLookupAndFingerprintsSurviveConflictingIdentifiers()
    {
        var prefs = new PlanPreferences
        {
            CharmValues = new CharmValueBook(new CharmValueFile
            {
                Charms = new()
                {
                    new() { EntityId = 1, Id = "first", Base = 3 },
                    new() { EntityId = 1, Id = "second", Base = 7 },
                    new() { EntityId = 2, Id = "first", Base = 9 },
                },
            }),
        };
        var restored = RoundTrip(ReplayPreferences.From(prefs)).Restore();
        Assert.Equal(PlanFingerprint.PlanningContext(prefs, "saved"), PlanFingerprint.PlanningContext(restored, "saved"));
        foreach (var definition in new[]
                 {
                     new CharmDefinition { EntityId = 1, Id = "first" },
                     new CharmDefinition { EntityId = 1, Id = "second" },
                     new CharmDefinition { EntityId = 2, Id = "first" },
                 })
            Assert.Equal(prefs.CharmValues.Of(definition)!.Base, restored.CharmValues.Of(definition)!.Base);
    }

    [Fact]
    public void InvalidOrIncompleteInputsNeverFallBackToLocalDefaults()
    {
        var original = Capture();
        var missingSettings = RoundTrip(original);
        missingSettings.Preferences!.PinnedCharms = null;
        Assert.Throws<InvalidDataException>(() => missingSettings.Rebuild());
        var missingCatalog = RoundTrip(original);
        missingCatalog.Catalog!.Charms = null;
        Assert.Throws<InvalidDataException>(() => missingCatalog.Rebuild());
        var duplicateCatalog = RoundTrip(original);
        duplicateCatalog.Catalog!.Charms!.Add(duplicateCatalog.Catalog.Charms[0]);
        Assert.Throws<InvalidDataException>(() => duplicateCatalog.Rebuild());
        var changedInput = RoundTrip(original);
        changedInput.Preferences!.PriorityCategories!.Clear();
        Assert.Throws<InvalidDataException>(() => changedInput.Rebuild());
        var missingResults = RoundTrip(original);
        missingResults.Expected = null;
        Assert.Throws<InvalidDataException>(() => missingResults.Rebuild());
        var oldSchema = RoundTrip(original);
        oldSchema.Version = 0;
        Assert.Throws<InvalidDataException>(() => oldSchema.Rebuild(true));
        var oldCatalog = RoundTrip(original);
        oldCatalog.CatalogVersion--;
        Assert.Throws<InvalidDataException>(() => oldCatalog.Rebuild(true));
    }

    [Fact]
    public void DifferentModelRequiresExplicitComparisonAndStillChecksResults()
    {
        var capture = Capture();
        capture.CoreBuild = "another-build";
        Assert.Throws<InvalidDataException>(() => capture.Rebuild());
        Assert.Empty(capture.Expected!.Differences(ReplayResult.From(capture.Rebuild(true))));
        var path = PathOf("changed.replay");
        PlanReplayFile.Write(path, JsonConvert.SerializeObject(capture));
        Assert.Equal(1, PlanReproduce.Run(path));
        Assert.Equal(0, PlanReproduce.Run(path, true));
        capture.Expected.Scores["최선"] += 10;
        PlanReplayFile.Write(path, JsonConvert.SerializeObject(capture));
        Assert.Equal(2, PlanReproduce.Run(path, true));
    }

    [Fact]
    public void CorruptAndMalformedFilesReturnFailure()
    {
        var path = PathOf("corrupt.replay");
        PlanReplayFile.Write(path, JsonConvert.SerializeObject(Capture()));
        File.AppendAllText(path, " ");
        Assert.Throws<InvalidDataException>(() => PlanReplayFile.Read(path));
        Assert.Equal(1, PlanReproduce.Run(path));
        PlanReplayFile.Write(path, "{");
        Assert.Equal(1, PlanReproduce.Run(path));
        Assert.Equal(1, PlanReproduce.Run(PathOf("missing.replay")));
    }

    [Fact]
    public void ResultComparisonDetectsEqualScoreLayoutAndRecommendationChanges()
    {
        var replay = Capture();
        var plan = replay.Rebuild();
        var expected = ReplayResult.From(plan);
        var charm = plan.Best.CharmPositions.First();
        plan.Best.CharmPositions[charm.Key] = new GridPos(5, 1);
        Assert.Contains(expected.Differences(ReplayResult.From(plan)), text => text.Contains("아이템/"));
        plan.Best.CharmPositions[charm.Key] = charm.Value;
        plan.Offers.Add(new OfferAdvice { Candidate = new OfferCandidate { DefinitionId = 999, Kind = "charm" } });
        Assert.Contains(expected.Differences(ReplayResult.From(plan)), text => text.StartsWith("후보/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("--solve")]
    [InlineData("--prediction-probe")]
    [InlineData("--reproduce", "input.replay")]
    [InlineData("--reproduce", "input.replay", "--allow-model-change")]
    [InlineData("--churn", "snapshot.json", "3")]
    public void DocumentedCommandsAreAccepted(params string[] args) => Assert.Null(CommandLine.Error(args));

    [Theory]
    [InlineData("--unknown")]
    [InlineData("--help", "--solve")]
    [InlineData("--reproduce")]
    [InlineData("--reproduce", "--check")]
    [InlineData("--reproduce", "input.replay", "--values")]
    [InlineData("--allow-model-change")]
    [InlineData("--churn", "snapshot.json", "0")]
    [InlineData("--churn", "snapshot.json", "bad")]
    public void InvalidCommandsAreRejected(params string[] args) => Assert.NotNull(CommandLine.Error(args));

    private static PlanReplay Capture()
    {
        var (catalog, snapshot, preferences) = Inputs();
        using var runner = new PlanRunner(catalog);
        runner.Submit(snapshot, preferences, "saved");
        Await(runner);
        return RoundTrip(runner.CaptureReplay()!);
    }

    private static T RoundTrip<T>(T input) =>
        System.Text.Json.JsonSerializer.Deserialize<T>(JsonConvert.SerializeObject(input))!;

    private static void Await(PlanRunner runner) =>
        Assert.True(SpinWait.SpinUntil(() => runner.State.IsCurrent, TimeSpan.FromSeconds(10)), runner.State.Error);

    private static (Catalog Catalog, GameSnapshot Snapshot, PlanPreferences Preferences) Inputs()
    {
        var catalog = new Catalog(
            new[] { new TabletDefinition { EntityId = 2001, Id = "tablet", Query = "RIGHT 2", IsRotatable = true } },
            new[]
            {
                new CharmDefinition { EntityId = 1, Id = "key", Behavior = "Charm_3Elemental_ByRow", MaxLevel = 3, LineCategories = new() { "EMBER", "GLACIER" } },
                new CharmDefinition { EntityId = 2, Id = "wand", MaxLevel = 3, Categories = new() { "GLACIER" } },
                new CharmDefinition { EntityId = 3, Id = "item", MaxLevel = 3, Categories = new() { "EMBER" } },
                new CharmDefinition { EntityId = 4, Id = "offered", MaxLevel = 3, Categories = new() { "GLACIER" } },
            },
            new[]
            {
                new ComboDefinition { Id = "EMBER", Thresholds = new() { 2, 4 } },
                new ComboDefinition { Id = "GLACIER", Thresholds = new() { 2, 4 } },
            });
        var snapshot = new GameSnapshot
        {
            GameVersion = "합성 입력",
            Run = new RunState { Gold = 100 },
            Inventory = new InventoryState
            {
                Width = 6,
                Height = 2,
                Storage = 12,
                Items = new()
                {
                    new() { DefinitionId = 1, InstanceId = 11, Position = new(0, 0), IsActive = true },
                    new() { DefinitionId = 2, InstanceId = 12, Position = new(2, 0), EffectiveLevel = 2, IsActive = true },
                    new() { DefinitionId = 3, InstanceId = 13, Position = new(3, 0), IsActive = true },
                },
                Tablets = new() { new() { DefinitionId = 2001, InstanceId = 21, Position = new(1, 0), IsApplied = true, IsRotatable = true } },
                LevelMatrix = new() { ["2,0"] = 2 },
                ComboCounts = new() { ["EMBER"] = 2, ["GLACIER"] = 1 },
            },
        };
        var preferences = new PlanPreferences
        {
            PriorityCategories = new() { "GLACIER" },
            PinnedCharms = new() { [2] = 2, [3] = -1 },
            HeldCharms = new() { 3 },
            RetainedCharms = new() { 2 },
            PresetCharms = new() { 4 },
            CharmValues = new CharmValueBook(new CharmValueFile
            {
                Charms = new() { new() { Id = "wand", EntityId = 2, Base = 7, PerLevel = 2 } },
            }),
        };
        return (catalog, snapshot, preferences);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}

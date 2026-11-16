using System.Text.Json;
using SephPlanner.Core.Extensions;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Plugin;

namespace SephPlanner.Tests;

/// <summary>
/// 다른 모드가 판 상황에 맞춰 값어치 표를 덮는 통로. 확장이 무엇을 돌려주든 플래너는 기본 표로 계속 돌아야 하고,
/// 덮인 표는 재현 자료와 지문에 그대로 실려야 한다.
/// </summary>
public class PlanExtensionTests
{
    private sealed class FixedExtension : IPlanExtension
    {
        public FixedExtension(string id, Func<GameSnapshot, CharmValueFile?> values)
        {
            Id = id;
            _values = values;
        }

        private readonly Func<GameSnapshot, CharmValueFile?> _values;
        public int Calls { get; private set; }
        public string Id { get; }

        public CharmValueFile? Values(GameSnapshot snapshot)
        {
            Calls++;
            return _values(snapshot);
        }
    }

    private static CharmDefinition Charm(int entityId, string id) => new() { EntityId = entityId, Id = id, MaxLevel = 5 };

    private static CharmValueFile File(params CharmValueEntry[] entries) => new() { Version = 1, Charms = entries.ToList() };

    private static CharmValueBook Baseline() => new(File(
        new CharmValueEntry { Id = "a", EntityId = 1, Tier = 1 },
        new CharmValueEntry { Id = "b", EntityId = 2, Tier = 2 },
        new CharmValueEntry { Id = "c", EntityId = 3, Tier = 3 }));

    private static CharmValueBook Overlay(ExtensionRegistry registry, List<string>? warnings = null) =>
        registry.Overlay(new GameSnapshot(), Baseline(), message => warnings?.Add(message));

    [Fact]
    public void ALevelTableOverridesTierAndStraightLine()
    {
        var entry = new CharmValueEntry { Id = "a", Tier = 5, Base = 9, PerLevel = 9, ByLevel = new List<double> { 0.5, 2, 1 } };

        var worth = CharmWorth.Resolve(Charm(1, "a"), entry);

        Assert.Equal(CharmWorthSource.Curated, worth.Source);
        Assert.Equal(0.5, worth.At(0));
        Assert.Equal(2, worth.At(1));
        Assert.Equal(1, worth.At(2));
        Assert.Equal(1, worth.At(5));
    }

    [Fact]
    public void AnExtensionReplacesEntriesMatchedByEitherKeyAndKeepsTheRest()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => File(
            new CharmValueEntry { Id = "b", ByLevel = new List<double> { 7 } },
            new CharmValueEntry { EntityId = 3, ByLevel = new List<double> { 8 } })));

        var book = Overlay(registry);

        Assert.Equal(1, book.Of(Charm(1, "a"))!.Tier);
        Assert.Equal(new[] { 7.0 }, book.Of(Charm(2, "b"))!.ByLevel);
        Assert.Equal(new[] { 8.0 }, book.Of(Charm(3, "c"))!.ByLevel);
    }

    [Fact]
    public void TheLaterRegisteredExtensionWins()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("first", _ => File(new CharmValueEntry { EntityId = 1, ByLevel = new List<double> { 1 } })));
        registry.Register(new FixedExtension("second", _ => File(new CharmValueEntry { EntityId = 1, ByLevel = new List<double> { 2 } })));

        Assert.Equal(new[] { 2.0 }, Overlay(registry).Of(Charm(1, "a"))!.ByLevel);
    }

    [Fact]
    public void NoTableLeavesTheBaselineUntouched()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => null));
        var baseline = Baseline();

        Assert.Same(baseline, registry.Overlay(new GameSnapshot(), baseline, _ => { }));
    }

    [Fact]
    public void AFailingExtensionIsSwitchedOffUntilTheNextRun()
    {
        var registry = new ExtensionRegistry();
        var failing = new FixedExtension("broken", _ => throw new InvalidOperationException("boom"));
        var healthy = new FixedExtension("healthy", _ => File(new CharmValueEntry { EntityId = 1, ByLevel = new List<double> { 4 } }));
        registry.Register(failing);
        registry.Register(healthy);
        var warnings = new List<string>();

        var book = Overlay(registry, warnings);
        Overlay(registry, warnings);

        Assert.Equal(new[] { 4.0 }, book.Of(Charm(1, "a"))!.ByLevel);
        Assert.Equal(1, failing.Calls);
        Assert.Equal(2, healthy.Calls);
        Assert.Single(warnings);
        Assert.Contains("broken", warnings[0], StringComparison.Ordinal);
        Assert.True(registry.Stamps().Single(stamp => stamp.Id == "broken").Disabled);

        registry.Restore();
        Overlay(registry, warnings);

        Assert.Equal(2, failing.Calls);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ATableWithNonFiniteValuesIsRejected(double value)
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => File(new CharmValueEntry { EntityId = 1, ByLevel = new List<double> { 1, value } })));
        var warnings = new List<string>();

        var book = Overlay(registry, warnings);

        Assert.Null(book.Of(Charm(1, "a"))!.ByLevel);
        Assert.Single(warnings);
    }

    [Fact]
    public void AnEntryWithoutAnyKeyIsRejected()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => File(new CharmValueEntry { ByLevel = new List<double> { 1 } })));
        var warnings = new List<string>();

        Overlay(registry, warnings);

        Assert.Single(warnings);
    }

    [Fact]
    public void NamesMustBePresentAndUnique()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => null));

        Assert.Throws<ArgumentException>(() => registry.Register(new FixedExtension("ext", _ => null)));
        Assert.Throws<ArgumentException>(() => registry.Register(new FixedExtension(" ", _ => null)));
    }

    [Fact]
    public void ChangingTheReturnedTableLaterDoesNotReachTheBook()
    {
        // 계획은 다른 스레드에서 풀린다. 확장이 자기 표를 고쳐 쓰더라도 이미 넘긴 계획 입력은 그대로여야 한다.
        var table = new List<double> { 1, 2 };
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => File(new CharmValueEntry { EntityId = 1, ByLevel = table })));

        var book = Overlay(registry);
        table[1] = 99;

        Assert.Equal(new[] { 1.0, 2 }, book.Of(Charm(1, "a"))!.ByLevel);
    }

    [Fact]
    public void ALevelTableIsPartOfThePlanFingerprintOnlyWhenPresent()
    {
        PlanPreferences With(List<double>? table) => new()
        {
            CharmValues = new CharmValueBook(File(new CharmValueEntry { Id = "a", EntityId = 1, Tier = 2, ByLevel = table })),
        };

        var none = PlanFingerprint.PlanningContext(With(null), "catalog");
        var empty = PlanFingerprint.PlanningContext(With(new List<double>()), "catalog");
        var one = PlanFingerprint.PlanningContext(With(new List<double> { 1, 2 }), "catalog");
        var other = PlanFingerprint.PlanningContext(With(new List<double> { 1, 3 }), "catalog");

        Assert.Equal(none, empty);
        Assert.NotEqual(none, one);
        Assert.NotEqual(one, other);
    }

    [Fact]
    public void AnOverlaidTableSurvivesTheReplayRoundTrip()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ext", _ => File(new CharmValueEntry { EntityId = 2, ByLevel = new List<double> { 0.25, 1.5 } })));
        var preferences = new PlanPreferences { CharmValues = Overlay(registry) };

        var json = JsonSerializer.Serialize(ReplayPreferences.From(preferences));
        var restored = JsonSerializer.Deserialize<ReplayPreferences>(json)!.Restore();

        Assert.Equal(new[] { 0.25, 1.5 }, restored.CharmValues.Of(Charm(2, "b"))!.ByLevel);
        Assert.Equal(
            PlanFingerprint.PlanningContext(preferences, "catalog"),
            PlanFingerprint.PlanningContext(restored, "catalog"));
    }

    [Fact]
    public void TheDiagnosticReportNamesTheExtensions()
    {
        using var directory = new DiagnosticTestDirectory();
        var registry = new ExtensionRegistry();
        registry.Register(new FixedExtension("ilonika.helper", _ => null));

        var with = new DiagnosticCapture(new DiagnosticText(), _ => { }, directory.Path);
        with.Finish("검증용", ReplayPreferences.From(PlanPreferences.None), extensions: registry.Stamps());
        var without = new DiagnosticCapture(new DiagnosticText(), _ => { }, directory.Path);
        without.Finish("검증용", ReplayPreferences.From(PlanPreferences.None), extensions: new ExtensionRegistry().Stamps());

        using var report = JsonDocument.Parse(with.Files["report.json"]);
        var stamp = report.RootElement.GetProperty("Extensions")[0];
        Assert.Equal("ilonika.helper", stamp.GetProperty("Id").GetString());
        Assert.False(stamp.GetProperty("Disabled").GetBoolean());
        using var empty = JsonDocument.Parse(without.Files["report.json"]);
        Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("Extensions").ValueKind);
    }
}

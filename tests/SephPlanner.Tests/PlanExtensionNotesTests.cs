using System.Text.Json;
using SephPlanner.Core.Extensions;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Plugin;

namespace SephPlanner.Tests;

/// <summary>
/// 확장이 F3 에 설정 줄을 내놓고 HUD 에 글자를 얹는 통로. 확장이 무엇을 돌려주든 창과 HUD 는 그릴 수 있는 모양만
/// 받아야 하고, 확장이 실패하면 값어치 표와 똑같이 새 판까지 꺼져야 한다.
/// </summary>
public class PlanExtensionNotesTests
{
    private sealed class Extension : IPlanExtension, IPlanExtensionNotes, IPlanExtensionOptions
    {
        public Extension(string id) => Id = id;

        public string Id { get; }
        public string Title { get; set; } = "도우미";
        public Func<ExtensionNoteContext, ExtensionNotes?> MakeNotes { get; set; } = _ => null;
        public Func<IReadOnlyList<ExtensionOption>> MakeOptions { get; set; } = () => new List<ExtensionOption>();
        public int ValueCalls { get; private set; }
        public int NoteCalls { get; private set; }

        public CharmValueFile? Values(GameSnapshot snapshot)
        {
            ValueCalls++;
            return null;
        }

        public ExtensionNotes? Notes(GameSnapshot snapshot, ExtensionNoteContext context)
        {
            NoteCalls++;
            return MakeNotes(context);
        }

        public IReadOnlyList<ExtensionOption> Options() => MakeOptions();
    }

    private sealed class ValuesOnly : IPlanExtension
    {
        public string Id => "values.only";
        public CharmValueFile? Values(GameSnapshot snapshot) => null;
    }

    private static PlanNotes Notes(ExtensionRegistry registry, List<string>? warnings = null, ExtensionNoteContext? context = null) =>
        registry.Notes(new GameSnapshot(), context ?? ExtensionNoteContext.None, message => warnings?.Add(message));

    private static ExtensionNotes Lines(params string[] lines) => new() { Lines = lines.ToList() };

    private static ExtensionNotes Tag(int definitionId, string tag, string description = "") => new()
    {
        Candidates = new Dictionary<int, CandidateNote> { [definitionId] = new() { Tag = tag, Description = description } },
    };

    [Fact]
    public void LinesAreCutToThreeAndKeptOnOneLineEach()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ext") { MakeNotes = _ => Lines("첫\n줄", " ", "둘", "셋", "넷") });

        var notes = Notes(registry);

        Assert.Equal(new[] { "첫 줄", "둘", "셋" }, notes.Lines);
    }

    [Fact]
    public void ALongTagIsShortenedAndEmptyNotesAreDropped()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ext")
        {
            MakeNotes = _ => new ExtensionNotes
            {
                Candidates = new Dictionary<int, CandidateNote>
                {
                    [1] = new() { Tag = "[S] 아주 아주 긴 추천 꼬리표", Description = "만렙 DPS +38%" },
                    [2] = new() { Tag = " ", Description = "" },
                    [0] = new() { Tag = "번호 없음" },
                },
            },
        });

        var notes = Notes(registry);

        var tag = notes.Candidate(1)!.Tag;
        Assert.Equal(ExtensionRegistry.MaxTagLength, tag.Length);
        Assert.EndsWith("…", tag, StringComparison.Ordinal);
        Assert.Equal("만렙 DPS +38%", notes.Candidate(1)!.Description);
        Assert.Null(notes.Candidate(2));
        Assert.Null(notes.Candidate(0));
    }

    [Fact]
    public void LinesFollowRegistrationOrderAndTheLaterExtensionWinsATag()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("first") { MakeNotes = _ => new ExtensionNotes { Lines = { "가" }, Candidates = Tag(7, "앞").Candidates } });
        registry.Register(new Extension("second") { MakeNotes = _ => new ExtensionNotes { Lines = { "나" }, Candidates = Tag(7, "뒤").Candidates } });

        var notes = Notes(registry);

        Assert.Equal(new[] { "가", "나" }, notes.Lines);
        Assert.Equal("뒤", notes.Candidate(7)!.Tag);
    }

    [Fact]
    public void TheSameNotesKeepTheSameObjectSoTheHudDoesNotRedraw()
    {
        var line = "볼트 하나로";
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ext") { MakeNotes = _ => Lines(line) });

        var first = Notes(registry);
        var second = Notes(registry);
        line = "볼트 둘로";
        var third = Notes(registry);

        Assert.Same(first, second);
        Assert.NotSame(second, third);
        Assert.Equal("볼트 둘로", third.Lines.Single());
    }

    [Fact]
    public void NoNotesGiveTheEmptyNotes()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new ValuesOnly());
        registry.Register(new Extension("ext"));

        Assert.Same(PlanNotes.Empty, Notes(registry));
    }

    [Fact]
    public void AFailingNoteSwitchesTheExtensionOffUntilTheNextRun()
    {
        var registry = new ExtensionRegistry();
        var failing = new Extension("broken") { MakeNotes = _ => throw new InvalidOperationException("boom") };
        registry.Register(failing);
        var warnings = new List<string>();

        Notes(registry, warnings);
        Notes(registry, warnings);
        registry.Overlay(new GameSnapshot(), CharmValueBook.Empty, warnings.Add);

        Assert.Equal(1, failing.NoteCalls);
        Assert.Equal(0, failing.ValueCalls);
        Assert.Single(warnings);
        Assert.Contains("broken", warnings[0], StringComparison.Ordinal);
        Assert.True(registry.Stamps().Single().Disabled);

        registry.Restore();
        Notes(registry, warnings);

        Assert.Equal(2, failing.NoteCalls);
    }

    [Fact]
    public void ChangingTheReturnedNotesLaterDoesNotReachTheHud()
    {
        var returned = Lines("처음");
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ext") { MakeNotes = _ => returned });

        var notes = Notes(registry);
        returned.Lines[0] = "나중";

        Assert.Equal("처음", notes.Lines.Single());
        Assert.Equal("처음", registry.Stamps().Single().Notes!.Lines.Single());
    }

    [Fact]
    public void TheExtensionReadsThePlayersOwnChoices()
    {
        ExtensionNoteContext? seen = null;
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ext") { MakeNotes = context => { seen = context; return null; } });

        Notes(registry, context: new ExtensionNoteContext(true, new[] { "Flame" }));

        Assert.True(seen!.HasImportedBuild);
        Assert.Equal(new[] { "Flame" }, seen.PriorityCategories);
    }

    [Fact]
    public void TheDiagnosticReportKeepsWhatThePlayerSaw()
    {
        using var directory = new DiagnosticTestDirectory();
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ilonika.helper") { MakeNotes = _ => new ExtensionNotes { Lines = { "1볼트로 전환" }, Candidates = Tag(1095, "[S] 추천", "만렙 DPS +38%").Candidates } });
        Notes(registry);

        var capture = new DiagnosticCapture(new DiagnosticText(), _ => { }, directory.Path);
        capture.Finish("검증용", ReplayPreferences.From(PlanPreferences.None), extensions: registry.Stamps());

        using var report = JsonDocument.Parse(capture.Files["report.json"]);
        var notes = report.RootElement.GetProperty("Extensions")[0].GetProperty("Notes");
        Assert.Equal("1볼트로 전환", notes.GetProperty("Lines")[0].GetString());
        var candidate = notes.GetProperty("Candidates").GetProperty("1095");
        Assert.Equal("[S] 추천", candidate.GetProperty("Tag").GetString());
        Assert.Equal("만렙 DPS +38%", candidate.GetProperty("Description").GetString());
    }

    [Fact]
    public void OnlyExtensionsWithOptionsGetAGroup()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new ValuesOnly());
        registry.Register(new Extension("ext")
        {
            Title = "일로니카\n도우미",
            MakeOptions = () => new List<ExtensionOption>
            {
                new() { Label = "켜기", Choices = new[] { "꺼짐", "켜짐" } },
                new() { Label = "선택지 없음", Choices = Array.Empty<string>() },
            },
        });

        var groups = registry.OptionGroups(_ => { });

        var group = Assert.Single(groups);
        Assert.Equal("ext", group.Id);
        Assert.Equal("일로니카 도우미", group.Title);
        Assert.Equal("켜기", Assert.Single(group.Options).Label);
    }

    [Fact]
    public void OptionsAreReadAndWrittenThroughTheExtension()
    {
        var stored = 0;
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("ext")
        {
            MakeOptions = () => new List<ExtensionOption>
            {
                new() { Label = "기준 볼트", Choices = new[] { "자동", "파이어" }, Read = () => stored, Write = i => stored = i },
            },
        });

        var option = registry.OptionGroups(_ => { }).Single().Options.Single();
        option.Write(1);

        Assert.Equal(1, stored);
        Assert.Equal(1, option.Read());
    }

    [Fact]
    public void AFailingOptionSwitchesTheExtensionOffAndLeavesOnlyItsName()
    {
        var registry = new ExtensionRegistry();
        var extension = new Extension("broken")
        {
            MakeOptions = () => new List<ExtensionOption>
            {
                new() { Label = "켜기", Choices = new[] { "꺼짐", "켜짐" }, Write = _ => throw new IOException("disk") },
            },
        };
        registry.Register(extension);
        var warnings = new List<string>();

        registry.OptionGroups(warnings.Add).Single().Options.Single().Write(1);
        var after = registry.OptionGroups(warnings.Add).Single();
        Notes(registry, warnings);

        Assert.Single(warnings);
        Assert.True(after.Disabled);
        Assert.Empty(after.Options);
        Assert.Equal(0, extension.NoteCalls);
    }

    [Fact]
    public void ThrowingFromOptionsIsCaughtLikeTheOtherCalls()
    {
        var registry = new ExtensionRegistry();
        registry.Register(new Extension("broken") { MakeOptions = () => throw new InvalidOperationException("boom") });
        var warnings = new List<string>();

        var group = registry.OptionGroups(warnings.Add).Single();

        Assert.True(group.Disabled);
        Assert.Single(warnings);
    }
}

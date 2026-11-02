using System.Text.RegularExpressions;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Text;

namespace SephPlanner.Tests;

/// <summary>언어를 바꾸는 시험은 다른 시험과 함께 돌면 그쪽 문장을 바꿔 버린다.</summary>
[CollectionDefinition("Localization", DisableParallelization = true)]
public sealed class LocalizationGroup
{
}

[Collection("Localization")]
public sealed class LocalizationTests
{
    private static readonly Regex TrCall = new(@"\b(?:Tr|Phrase)\(\s*""((?:[^""\\]|\\.)*)""");
    private static readonly Regex TrNotLiteral = new(@"\b(?:Tr|Phrase)\(\s*(?!""|string korean)");
    private static readonly Regex Placeholder = new(@"\{(\d+)(?:\|[^}]*)?(?::[^}]*)?\}");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "SephPlanner.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("저장소 뿌리를 찾지 못했습니다.");
    }

    private static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                           !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"));

    private static string Unescape(string literal) => Regex.Unescape(literal);

    private static List<string> UsedKeys() =>
        SourceFiles().SelectMany(File.ReadAllLines)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .SelectMany(line => TrCall.Matches(line).Select(match => Unescape(match.Groups[1].Value)))
            .Distinct(StringComparer.Ordinal).ToList();

    private static SortedSet<int> Indices(string template) =>
        new(Placeholder.Matches(template).Select(match => int.Parse(match.Groups[1].Value)));

    [Fact]
    public void TrIsOnlyCalledWithAStringConstant()
    {
        // Phrase.cs 는 원문 없는 조각(Verbatim·Concat)을 제 생성자로 짓는다.
        var offenders = SourceFiles().Where(path => Path.GetFileName(path) != "Phrase.cs")
            .SelectMany(path => File.ReadAllLines(path).Select((line, i) => (path, line, i)))
            .Where(item => TrNotLiteral.IsMatch(item.line) && !item.line.TrimStart().StartsWith("//", StringComparison.Ordinal) &&
                           !item.line.TrimStart().StartsWith("///", StringComparison.Ordinal))
            .Select(item => $"{Path.GetFileName(item.path)}:{item.i + 1}: {item.line.Trim()}")
            .ToList();
        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryKeyHasEveryTranslation()
    {
        var table = Strings.All.ToDictionary(entry => entry.Korean, StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var key in UsedKeys())
        {
            if (!table.TryGetValue(key, out var entry))
            {
                missing.Add("표에 없음: " + key);
                continue;
            }
            foreach (var language in new[] { Language.English, Language.Japanese, Language.ChineseSimplified, Language.ChineseTraditional })
                if (entry.In(language).Length == 0) missing.Add($"{language} 빠짐: {key}");
        }
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryEntryIsUsed()
    {
        var used = new HashSet<string>(UsedKeys(), StringComparer.Ordinal);
        Assert.DoesNotContain(Strings.All, entry => !used.Contains(entry.Korean));
    }

    [Fact]
    public void KeysAreNotRepeated()
    {
        Assert.Empty(Strings.All.GroupBy(entry => entry.Korean, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key));
    }

    [Fact]
    public void TranslationsKeepThePlaceholdersAndDropParticles()
    {
        var wrong = new List<string>();
        foreach (var entry in Strings.All)
        {
            var expected = Indices(entry.Korean);
            foreach (var language in new[] { Language.English, Language.Japanese, Language.ChineseSimplified, Language.ChineseTraditional })
            {
                var text = entry.In(language);
                if (!Indices(text).SetEquals(expected)) wrong.Add($"{language} 자리 다름: {entry.Korean} → {text}");
                if (Placeholder.Matches(text).Any(match => match.Value.Contains('|'))) wrong.Add($"{language} 조사 표식: {text}");
            }
        }
        Assert.Empty(wrong);
    }

    [Fact]
    public void KoreanParticlesFollowTheLastSyllable()
    {
        using var _ = Localization.Use(Language.Korean);
        Assert.Equal("쌍성을 넣으세요", Localization.Format("{0|을/를} 넣으세요", ["쌍성"]));
        Assert.Equal("전진을 넣으세요", Localization.Format("{0|을/를} 넣으세요", ["전진"]));
        Assert.Equal("파이어 볼트를 넣으세요", Localization.Format("{0|을/를} 넣으세요", ["파이어 볼트"]));
        Assert.Equal("Fire Bolt을(를) 넣으세요", Localization.Format("{0|을/를} 넣으세요", ["Fire Bolt"]));
        Assert.Equal("석판 2개와 3", Localization.Format("석판 {0}개와 {1}", [2, 3]));
    }

    [Fact]
    public void UntranslatedTextStaysKorean()
    {
        using var _ = Localization.Use(Language.Japanese);
        Assert.Equal("표에 없는 문장 3", Localization.Tr("표에 없는 문장 {0}", 3));
        Assert.Equal("言語", Localization.Tr("언어"));
    }

    [Fact]
    public void PhraseIsKoreanForLogsAndTheCurrentLanguageOnScreen()
    {
        using var _ = Localization.Use(Language.Japanese);
        var phrase = new Phrase("{0} {1|과/와} {2} {3|을/를} 합칩니다. 재료 둘은 사라집니다.", "①", "쌍성", "②", "전진");

        Assert.Equal("① 쌍성과 ② 전진을 합칩니다. 재료 둘은 사라집니다.", phrase.Korean);
        Assert.Equal(Localization.Tr("{0} {1|과/와} {2} {3|을/를} 합칩니다. 재료 둘은 사라집니다.", "①", "쌍성", "②", "전진"),
            phrase.ToString());
        Assert.NotEqual(phrase.Korean, phrase.ToString());
    }

    [Fact]
    public void PhraseArgumentsFollowTheOuterLanguage()
    {
        using var _ = Localization.Use(Language.English);
        var phrase = new Phrase("진단 전송 실패: {0} · 로컬 보관: {1}", new Phrase("배포 파일이 너무 큽니다."), "C:/x");

        Assert.Equal("진단 전송 실패: 배포 파일이 너무 큽니다. · 로컬 보관: C:/x", phrase.Korean);
        Assert.DoesNotContain("배포", phrase.ToString());
    }

    [Fact]
    public void PhraseJoinSkipsEmptyParts()
    {
        var joined = Phrase.Join(" ", new Phrase("원래 배치로 되돌렸습니다."), null, Phrase.Verbatim(""), "끝");
        Assert.Equal("원래 배치로 되돌렸습니다. 끝", joined.Korean);
    }

    [Fact]
    public void ExceptionsKeepTheirTypeAndAKoreanMessage()
    {
        using var _ = Localization.Use(Language.English);
        var exception = new Phrase("배포 파일이 너무 큽니다.").As(message => new InvalidDataException(message));

        Assert.IsType<InvalidDataException>(exception);
        Assert.Equal("배포 파일이 너무 큽니다.", exception.Message);
        Assert.Equal(Localization.Tr("배포 파일이 너무 큽니다."), Phrase.Of(exception).ToString());
        Assert.Equal("boom", Phrase.Of(new InvalidOperationException("boom")).ToString());
    }

    [Fact]
    public void ReplayRemembersTheLanguageItWasPlannedIn()
    {
        var (catalog, snapshot, preferences) = PlanReplayTests.Inputs();
        PlanReplay capture;
        using (Localization.Use(Language.Japanese))
        {
            using var runner = new PlanRunner(catalog);
            runner.Submit(snapshot, preferences, "saved");
            PlanReplayTests.Await(runner);
            capture = PlanReplayTests.RoundTrip(runner.CaptureReplay()!);
        }

        Assert.Equal("ja-JP", capture.Language);
        Assert.Empty(capture.Expected!.Differences(ReplayResult.From(capture.Rebuild())));
        Assert.Equal(Language.Korean, Localization.Current);
    }

    [Fact]
    public void LanguageChangesThePlanningContextOnlyAwayFromKorean()
    {
        var preferences = new SephPlanner.Core.Planning.PlanPreferences();
        var korean = PlanFingerprint.PlanningContext(preferences, "g", Language.Korean);
        Assert.Equal(korean, PlanFingerprint.PlanningContext(preferences, "g"));
        Assert.NotEqual(korean, PlanFingerprint.PlanningContext(preferences, "g", Language.English));
    }

    [Theory]
    [InlineData("ko-KR", Language.Korean)]
    [InlineData("en-US", Language.English)]
    [InlineData("ja-JP", Language.Japanese)]
    [InlineData("zh-CN", Language.ChineseSimplified)]
    [InlineData("zh-TW", Language.ChineseTraditional)]
    [InlineData("de-DE", Language.English)]
    [InlineData("", Language.English)]
    public void GameLanguagesMapToOurs(string code, Language expected) =>
        Assert.Equal(expected, Localization.FromGame(code));
}

using System.Text;
using System.Text.Json;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public sealed class DiagnosticNoteTests
{
    [Fact]
    public void EmptyInputProducesNoNote()
    {
        Assert.True(DiagnosticNote.Create(null, null).IsEmpty);
        Assert.True(DiagnosticNote.Create("", "   \n\n  ").IsEmpty);
        Assert.True(DiagnosticNote.None.IsEmpty);
    }

    [Fact]
    public void UnknownCategoryIsDropped()
    {
        var note = DiagnosticNote.Create("보내기", "배치가 이상합니다");
        Assert.Equal("", note.Category);
        Assert.Equal("", note.CategoryLabel);
        Assert.Equal("배치가 이상합니다", note.Text);
    }

    [Fact]
    public void KnownCategorySurvivesWithoutText()
    {
        var note = DiagnosticNote.Create("autoplace", "");
        Assert.False(note.IsEmpty);
        Assert.Equal("autoplace", note.Category);
        Assert.Equal("자동 배치 실패", note.CategoryLabel);
        Assert.Equal("", note.Text);
    }

    [Fact]
    public void ControlCharactersBecomeSpacesAndNewlinesSurvive()
    {
        var note = DiagnosticNote.Create("other", "첫 줄\n둘째\t줄" + (char)7 + "끝");
        Assert.Equal("첫 줄\n둘째 줄 끝", note.Text);
    }

    [Fact]
    public void RunsOfBlankLinesCollapse()
    {
        var note = DiagnosticNote.Create("other", "가\n\n\n\n나");
        Assert.Equal("가\n\n나", note.Text);
    }

    [Fact]
    public void LongTextIsTruncatedToTheLimit()
    {
        var note = DiagnosticNote.Create("other", new string('가', DiagnosticNote.MaximumTextLength + 50));
        Assert.Equal(DiagnosticNote.MaximumTextLength, note.Text.Length);
    }

    [Fact]
    public void TruncationDoesNotSplitASurrogatePair()
    {
        // 제한 길이의 마지막 한 칸에 이모지의 앞쪽 절반이 걸리도록 맞춘다.
        var text = new string('가', DiagnosticNote.MaximumTextLength - 1) + "🙂끝";
        var note = DiagnosticNote.Create("other", text);
        Assert.Equal(DiagnosticNote.MaximumTextLength - 1, note.Text.Length);
        Assert.DoesNotContain(note.Text, character => char.IsSurrogate(character));
        // 잘린 결과가 올바른 UTF-16 이어야 UTF-8 로 옮길 때 예외가 나지 않는다.
        new UTF8Encoding(false, true).GetBytes(note.Text);
    }

    [Fact]
    public void EveryCategoryHasALabelAndIsLookedUpById()
    {
        Assert.NotEmpty(DiagnosticNote.Categories);
        foreach (var category in DiagnosticNote.Categories)
        {
            Assert.NotEmpty(category.Id);
            Assert.NotEmpty(category.Label);
            Assert.Equal(category.Label, DiagnosticNote.LabelOf(category.Id));
        }
    }

    [Fact]
    public void ArchiveReadsTheReportWithoutExpandingEverythingElse()
    {
        var files = new Dictionary<string, string>
        {
            ["report.json"] = "{\"Version\":1}",
            ["sephplanner.log"] = new string('로', 4096),
        };
        var archive = DiagnosticArchive.Create(files);
        using var stream = new MemoryStream(archive);
        Assert.Equal("{\"Version\":1}", DiagnosticArchive.ReadReport(stream));
    }

    [Fact]
    public void ArchiveWithoutAReportIsRejectedWhenReadingTheReport()
    {
        var archive = DiagnosticArchive.Create(new Dictionary<string, string> { ["report.json"] = "{}" });
        using var stream = new MemoryStream(archive);
        Assert.Equal("{}", DiagnosticArchive.ReadReport(stream));

        using var empty = new MemoryStream();
        using (var writer = new System.IO.Compression.ZipArchive(empty, System.IO.Compression.ZipArchiveMode.Create, true))
            writer.CreateEntry("sephplanner.log");
        empty.Position = 0;
        Assert.Throws<InvalidDataException>(() => DiagnosticArchive.ReadReport(empty));
    }

    [Fact]
    public void NoteSerialisesAsPlainJsonFields()
    {
        var note = DiagnosticNote.Create("score", "점수가 0으로 나옵니다");
        var json = JsonSerializer.Serialize(new { note.Category, note.CategoryLabel, note.Text });
        using var document = JsonDocument.Parse(json);
        Assert.Equal("score", document.RootElement.GetProperty("Category").GetString());
        Assert.Equal("점수·추천이 이상함", document.RootElement.GetProperty("CategoryLabel").GetString());
        Assert.Equal("점수가 0으로 나옵니다", document.RootElement.GetProperty("Text").GetString());
    }
}

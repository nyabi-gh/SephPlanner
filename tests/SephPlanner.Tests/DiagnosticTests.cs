using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Plugin;

namespace SephPlanner.Tests;

public sealed class DiagnosticTests
{
    [Fact]
    public void ArchiveKeepsReplayBytesAndRejectsUnapprovedFiles()
    {
        var files = new Dictionary<string, string> { ["report.json"] = "{}", ["plan.replay"] = "재현 자료\n그대로" };
        using var content = new MemoryStream(DiagnosticArchive.Create(files));
        Assert.Equal(files, DiagnosticArchive.Read(content));
        files["game.dll"] = "제외 대상";
        Assert.Throws<InvalidDataException>(() => DiagnosticArchive.Create(files));
    }

    [Theory]
    [InlineData("../report.json")]
    [InlineData("/report.json")]
    [InlineData("folder/report.json")]
    [InlineData("report.json")]
    public void ArchiveRejectsTraversalAndDuplicateEntries(string secondName)
    {
        using var content = new MemoryStream();
        using (var zip = new ZipArchive(content, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("report.json").Open())) writer.Write("{}");
            using (var writer = new StreamWriter(zip.CreateEntry(secondName).Open())) writer.Write("{}");
        }
        content.Position = 0;
        Assert.Throws<InvalidDataException>(() => DiagnosticArchive.Read(content));
    }

    [Fact]
    public void ArchiveRejectsExpandedSizeEvenWhenCompressedFileIsSmall()
    {
        using var content = new MemoryStream();
        using (var zip = new ZipArchive(content, ZipArchiveMode.Create, true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("report.json").Open());
            writer.Write(new string('x', DiagnosticArchive.MaximumExpandedBytes + 1));
        }
        Assert.True(content.Length < DiagnosticArchive.MaximumArchiveBytes);
        content.Position = 0;
        Assert.Throws<InvalidDataException>(() => DiagnosticArchive.Read(content));
    }

    [Fact]
    public void TextRedactionRemovesPrivateRootsAndCredentialsWithoutEditingSource()
    {
        const string original = "C:\\Users\\Someone\\게임\\file.cs\n/home/someone/log\nAuthorization: Bearer private-token\napi_key=private-key\n보석 갑옷 level=-1";
        var log = new DiagnosticText();
        log.Append(original);
        var scrubbed = log.Snapshot();
        Assert.DoesNotContain("Someone", scrubbed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("someone", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("private-token", scrubbed, StringComparison.Ordinal);
        Assert.DoesNotContain("private-key", scrubbed, StringComparison.Ordinal);
        Assert.Contains("보석 갑옷 level=-1", scrubbed, StringComparison.Ordinal);
        Assert.Contains("private-token", original, StringComparison.Ordinal);
        for (var i = 0; i < 100; i++) log.Append(new string('가', 5000));
        Assert.True(Encoding.UTF8.GetByteCount(log.Snapshot()) <= DiagnosticArchive.MaximumLogBytes);
    }

    [Fact]
    public void FailedCaptureNeverUsesAnOlderSnapshotAndKeepsSuccessfulParts()
    {
        using var directory = new DiagnosticTestDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "inventory-snapshot.json"), "old snapshot");
        var errors = new List<object>();
        var capture = new DiagnosticCapture(new DiagnosticText(), errors.Add, directory.Path);
        capture.Collect("inventory-snapshot.json", () => throw new IOException("이번 저장 실패"), legacy: true);
        capture.Collect("plan.replay", () => null!);
        capture.Collect("inventory-dump.txt", () =>
        {
            var path = Path.Combine(capture.DirectoryPath, "inventory-dump.txt");
            File.WriteAllText(path, "새 진단");
            return path;
        }, legacy: true);
        capture.Finish("검증용", ReplayPreferences.From(new PlanPreferences()));
        Assert.DoesNotContain("inventory-snapshot.json", capture.Files.Keys);
        Assert.DoesNotContain("plan.replay", capture.Files.Keys);
        Assert.Equal("새 진단", capture.Files["inventory-dump.txt"]);
        Assert.Single(errors);
        Assert.True(capture.HasFailures);
        using var metadata = JsonDocument.Parse(capture.Files["report.json"]);
        Assert.Contains("저장 실패", metadata.RootElement.GetProperty("Collection").GetProperty("inventory-snapshot.json").GetString(), StringComparison.Ordinal);
        Assert.Equal("old snapshot", File.ReadAllText(Path.Combine(directory.Path, "inventory-snapshot.json")));
        var next = new DiagnosticCapture(new DiagnosticText(), errors.Add, directory.Path);
        Assert.NotEqual(capture.DirectoryPath, next.DirectoryPath);
        Assert.Empty(next.Files);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0|https://sephplanner.nyabi.me/api/v1/reports")]
    [InlineData("1|https://another.example/api/v1/reports")]
    public async Task MissingOrDifferentConsentMakesNoRequest(string consent)
    {
        var handler = new DiagnosticHandler((_, _) => throw new InvalidOperationException("요청이 발생하면 안 됩니다."));
        using var client = new DiagnosticUploadClient(handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(new Uri(DiagnosticUploadClient.DefaultEndpoint), consent,
            Guid.NewGuid(), [1], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData("http://sephplanner.nyabi.me/api/v1/reports")]
    [InlineData("https://user:secret@sephplanner.nyabi.me/api/v1/reports")]
    [InlineData("https://sephplanner.nyabi.me/api/v1/reports?token=value")]
    public async Task UploadRejectsUnprotectedOrCredentialBearingDestinations(string address)
    {
        var handler = new DiagnosticHandler((_, _) => throw new InvalidOperationException("요청이 발생하면 안 됩니다."));
        using var client = new DiagnosticUploadClient(handler);
        var endpoint = new Uri(address);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(endpoint, DiagnosticUploadClient.ConsentKey(endpoint),
            Guid.NewGuid(), [1], CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, true)]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.Created, false)]
    [InlineData(HttpStatusCode.Redirect, true)]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    public async Task UploadRequiresAnAcceptedStatusAndMatchingReceipt(HttpStatusCode status, bool correctId)
    {
        var id = Guid.NewGuid();
        var archive = DiagnosticArchive.Create(new Dictionary<string, string> { ["report.json"] = "{}" });
        var handler = new DiagnosticHandler(async (request, token) =>
        {
            Assert.Equal(id.ToString("N"), Assert.Single(request.Headers.GetValues("X-SephPlanner-Report-Id")));
            Assert.Equal(DiagnosticArchive.Hash(archive), Assert.Single(request.Headers.GetValues("X-SephPlanner-SHA256")));
            Assert.Equal(archive, await request.Content!.ReadAsByteArrayAsync(token));
            return new HttpResponseMessage(status) { Content = new StringContent((correctId ? id : Guid.NewGuid()).ToString("N")) };
        });
        using var client = new DiagnosticUploadClient(handler);
        var endpoint = new Uri(DiagnosticUploadClient.DefaultEndpoint);
        var send = client.SendAsync(endpoint, DiagnosticUploadClient.ConsentKey(endpoint), id, archive, CancellationToken.None);
        if (status is not (HttpStatusCode.Created or HttpStatusCode.OK)) await Assert.ThrowsAsync<HttpRequestException>(() => send);
        else if (!correctId) await Assert.ThrowsAsync<InvalidDataException>(() => send);
        else Assert.Equal(id.ToString("N"), await send);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task CancellationStopsAnUploadWaitingForTheServer()
    {
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DiagnosticHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("취소를 지나면 안 됩니다.");
        });
        using var client = new DiagnosticUploadClient(handler);
        var endpoint = new Uri(DiagnosticUploadClient.DefaultEndpoint);
        var send = client.SendAsync(endpoint, DiagnosticUploadClient.ConsentKey(endpoint), Guid.NewGuid(), [1], cancellation.Token);
        await entered.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send);
    }
}

internal sealed class DiagnosticHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response) : HttpMessageHandler
{
    internal int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return response(request, cancellationToken);
    }
}

internal sealed class DiagnosticTestDirectory : IDisposable
{
    private static readonly string Parent = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SephPlannerDiagnosticTests");
    internal string Path { get; } = System.IO.Path.Combine(Parent, Guid.NewGuid().ToString("N"));
    internal DiagnosticTestDirectory() { Directory.CreateDirectory(Path); }
    public void Dispose()
    {
        if (!System.IO.Path.GetFullPath(Path).StartsWith(Parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            Directory.EnumerateFileSystemEntries(Path, "*", SearchOption.AllDirectories).Any(entry => (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0))
            throw new InvalidOperationException("검증한 테스트 폴더만 정리할 수 있습니다.");
        Directory.Delete(Path, true);
    }
}

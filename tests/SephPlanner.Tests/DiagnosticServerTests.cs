using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SephPlanner.Core.Runtime;
using SephPlanner.Diagnostics;

namespace SephPlanner.Tests;

public sealed class DiagnosticServerTests
{
    [Fact]
    public async Task ReportsArePrivateDurableIdempotentAndDeletable()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var id = Guid.NewGuid();
        var archive = Payload(id);
        using var first = await server.Upload(id, archive);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(id.ToString("N"), await first.Content.ReadAsStringAsync());
        Assert.Equal(archive, await File.ReadAllBytesAsync(Path.Combine(server.Options.StorageDirectory, id.ToString("N") + ".zip")));
        using var duplicate = await server.Upload(id, archive);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        using var conflict = await server.Upload(id, Payload(id, "다른 진단"));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        foreach (var path in new[] { "/admin/reports", "/admin/reports/" + id.ToString("N") })
        {
            using var denied = await server.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        }
        using var deniedDelete = await server.Client.DeleteAsync("/admin/reports/" + id.ToString("N"));
        Assert.Equal(HttpStatusCode.Unauthorized, deniedDelete.StatusCode);
        server.Authorize();
        using var list = await server.Client.GetAsync("/admin/reports");
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(id.ToString("N"), Assert.Single(json.RootElement.EnumerateArray()).GetProperty("reportId").GetString());
        Assert.Equal(archive, await server.Client.GetByteArrayAsync("/admin/reports/" + id.ToString("N")));
        using var deleted = await server.Client.DeleteAsync("/admin/reports/" + id.ToString("N"));
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var missing = await server.Client.GetAsync("/admin/reports/" + id.ToString("N"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(Directory.GetFiles(server.Options.StorageDirectory));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Version\":\"1\",\"ReportId\":null}")]
    [InlineData("not json")]
    public async Task InvalidMetadataIsRejectedWithoutLeavingFiles(string metadata)
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var archive = DiagnosticArchive.Create(new Dictionary<string, string> { ["report.json"] = metadata });
        using var response = await server.Upload(Guid.NewGuid(), archive);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(Directory.GetFiles(server.Options.StorageDirectory));
    }

    [Fact]
    public async Task WrongHashAndOversizedBodyAreRejected()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var id = Guid.NewGuid();
        using var wrong = await server.Upload(id, Payload(id), new string('0', 64));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        using var oversized = await server.Upload(id, new byte[DiagnosticArchive.MaximumArchiveBytes + 1]);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Empty(Directory.GetFiles(server.Options.StorageDirectory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyAnExplicitlyTrustedProxyCanAssertHttps(bool trusted)
    {
        await using var server = await DiagnosticTestServer.StartAsync(options =>
            options.TrustedProxies = [trusted ? "127.0.0.1" : "192.0.2.1"]);
        var id = Guid.NewGuid();
        using var forwarded = await server.Upload(id, Payload(id));
        Assert.Equal(trusted ? HttpStatusCode.Created : HttpStatusCode.BadRequest, forwarded.StatusCode);
        server.Client.DefaultRequestHeaders.Remove("X-Forwarded-Proto");
        using var plaintext = await server.Upload(Guid.NewGuid(), Payload(Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.BadRequest, plaintext.StatusCode);
        using var health = await server.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task CapacityAndRateLimitsRejectExcessWithoutReplacingExistingReport()
    {
        await using var server = await DiagnosticTestServer.StartAsync(options =>
        {
            options.MaximumReports = 1;
            options.UploadsPerMinute = 2;
        });
        var id = Guid.NewGuid();
        using var first = await server.Upload(id, Payload(id));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var secondId = Guid.NewGuid();
        using var full = await server.Upload(secondId, Payload(secondId));
        Assert.Equal(HttpStatusCode.InsufficientStorage, full.StatusCode);
        using var limited = await server.Upload(secondId, Payload(secondId));
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Single(Directory.GetFiles(server.Options.StorageDirectory));
    }

    /// <summary>
    /// Kestrel 은 소문자 메서드를 그대로 넘기고 라우팅은 대소문자 없이 맞춘다. 제한이 "POST" 만 세면 "post" 업로드가
    /// 빈도와 동시 처리 제한을 모두 비켜 간다. <see cref="HttpClient"/>는 메서드를 바꿔 보낼 수 있어 소켓으로 직접 보낸다.
    /// </summary>
    [Theory]
    [InlineData("post")]
    [InlineData("Post")]
    public async Task UploadLimitsDoNotDependOnTheCaseOfTheMethod(string method)
    {
        await using var server = await DiagnosticTestServer.StartAsync(options => options.UploadsPerMinute = 1);
        Assert.Equal(201, await server.UploadRaw(method));
        Assert.Equal(429, await server.UploadRaw(method));
        Assert.Equal(429, await server.UploadRaw("POST"));
    }

    /// <summary>값싼 토큰을 빽빽이 채운 설명도 받는다. 서버는 설명 전체를 펴지 않고 필요한 칸만 읽는다.</summary>
    [Fact]
    public async Task ADenseReportIsReadWithoutExpandingItIntoADocument()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var id = Guid.NewGuid();
        var report = new System.Text.StringBuilder($"{{\"Version\":{DiagnosticArchive.SchemaVersion},\"ReportId\":\"{id:N}\",\"Pad\":[0");
        while (report.Length < 4 * 1024 * 1024) report.Append(",0");
        report.Append("],\"Note\":{\"CategoryLabel\":\"기타\",\"Text\":\"끝에 있는 메모\"}}");
        using (var response = await server.Upload(id, DiagnosticArchive.Create(new Dictionary<string, string> { ["report.json"] = report.ToString() })))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        server.Authorize();
        using var json = JsonDocument.Parse(await server.Client.GetStringAsync("/admin/reports"));
        Assert.Equal("끝에 있는 메모", Assert.Single(json.RootElement.EnumerateArray()).GetProperty("note").GetString());
    }

    [Fact]
    public async Task ExpiredFilesAndInterruptedUploadsAreRemovedOnRestart()
    {
        using var directory = new DiagnosticTestDirectory();
        var options = new DiagnosticServerOptions { StorageDirectory = directory.Path };
        var id = Guid.NewGuid();
        var archive = Payload(id);
        using (var store = new DiagnosticStore(options))
        using (var input = new MemoryStream(archive))
            Assert.Equal(DiagnosticSaveResult.Created, await store.SaveAsync(id, DiagnosticArchive.Hash(archive), input, "127.0.0.1", CancellationToken.None));
        File.SetLastWriteTimeUtc(Path.Combine(directory.Path, id.ToString("N") + ".zip"), DateTime.UtcNow.AddDays(-15));
        File.WriteAllText(Path.Combine(directory.Path, Guid.NewGuid().ToString("N") + ".upload"), "미완료");
        using var restarted = new DiagnosticStore(options);
        await restarted.PruneAsync(CancellationToken.None);
        Assert.Empty(Directory.GetFiles(directory.Path));
        Assert.Null(await restarted.ReadAsync(id, CancellationToken.None));
    }

    internal static byte[] Payload(Guid id, string log = "합성 검증 자료") => DiagnosticArchive.Create(new Dictionary<string, string>
    {
        ["report.json"] = JsonSerializer.Serialize(new { Version = DiagnosticArchive.SchemaVersion, ReportId = id.ToString("N") }),
        ["sephplanner.log"] = log,
    });

    [Fact]
    public async Task ListingCarriesTheUserNoteAndSurvivesReportsWithout()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var withNote = Guid.NewGuid();
        var note = DiagnosticNote.Create("placement", "자물쇠가 F8 마다 자리를 옮깁니다");
        using (var response = await server.Upload(withNote, DiagnosticArchive.Create(new Dictionary<string, string>
        {
            ["report.json"] = JsonSerializer.Serialize(new
            {
                Version = DiagnosticArchive.SchemaVersion,
                ReportId = withNote.ToString("N"),
                Note = new { note.Category, note.CategoryLabel, note.Text },
            }),
        })))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var without = Guid.NewGuid();
        using (var response = await server.Upload(without, Payload(without)))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        server.Authorize();
        using var list = await server.Client.GetAsync("/admin/reports");
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        var rows = json.RootElement.EnumerateArray()
            .ToDictionary(row => row.GetProperty("reportId").GetString()!, row => row);

        Assert.Equal("배치가 이상함", rows[withNote.ToString("N")].GetProperty("category").GetString());
        Assert.Equal("자물쇠가 F8 마다 자리를 옮깁니다", rows[withNote.ToString("N")].GetProperty("note").GetString());
        Assert.Equal("", rows[without.ToString("N")].GetProperty("category").GetString());
        Assert.Equal("", rows[without.ToString("N")].GetProperty("note").GetString());
    }

    private static byte[] WithNote(Guid id, object note) => DiagnosticArchive.Create(new Dictionary<string, string>
    {
        ["report.json"] = JsonSerializer.Serialize(new { Version = DiagnosticArchive.SchemaVersion, ReportId = id.ToString("N"), Note = note }),
    });

    [Theory]
    [InlineData(DiagnosticNote.MaximumTextLength, HttpStatusCode.Created)]
    [InlineData(DiagnosticNote.MaximumTextLength + 1, HttpStatusCode.BadRequest)]
    public async Task TheServerHoldsTheNoteToTheClientLimit(int length, HttpStatusCode expected)
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var id = Guid.NewGuid();
        using var response = await server.Upload(id, WithNote(id, new { Category = "other", CategoryLabel = "기타", Text = new string('가', length) }));
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task ANoteThatIsNotTextIsRejected()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var id = Guid.NewGuid();
        using var response = await server.Upload(id, WithNote(id, new { Text = new[] { "가" } }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>제한 전에 받은 자료가 목록을 부풀리거나 관리자 터미널을 조작하지 못하게 읽을 때도 자른다.</summary>
    [Fact]
    public async Task ListingTrimsLongNotesAndControlCharactersAlreadyOnDisk()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var id = Guid.NewGuid();
        await File.WriteAllBytesAsync(Path.Combine(server.Options.StorageDirectory, id.ToString("N") + ".zip"),
            WithNote(id, new { CategoryLabel = "기타", Text = "\u001b[2J첫 줄\n" + new string('가', 100_000) }));

        server.Authorize();
        using var json = JsonDocument.Parse(await server.Client.GetStringAsync("/admin/reports"));
        var note = Assert.Single(json.RootElement.EnumerateArray()).GetProperty("note").GetString()!;
        Assert.Equal(DiagnosticNote.MaximumTextLength, note.Length);
        Assert.StartsWith(" [2J첫 줄\n가", note);
    }

    [Fact]
    public async Task OneSourceCannotFillTheArchiveForEveryoneElse()
    {
        await using var server = await DiagnosticTestServer.StartAsync(options => options.MaximumReportsPerSource = 2);
        foreach (var (from, expected) in new[]
                 {
                     ("203.0.113.9", HttpStatusCode.Created), ("203.0.113.9", HttpStatusCode.Created),
                     ("203.0.113.9", HttpStatusCode.TooManyRequests), ("198.51.100.7", HttpStatusCode.Created),
                     ("2001:db8:1:2::1", HttpStatusCode.Created), ("2001:db8:1:2::ffff", HttpStatusCode.Created),
                     ("2001:db8:1:2:abcd::1", HttpStatusCode.TooManyRequests), ("2001:db8:1:3::1", HttpStatusCode.Created),
                 })
        {
            var id = Guid.NewGuid();
            using var response = await server.Upload(id, Payload(id), from: from);
            Assert.True(expected == response.StatusCode, $"{from}: {response.StatusCode}");
        }
        Assert.Equal(6, Directory.GetFiles(server.Options.StorageDirectory).Length);
    }

    [Fact]
    public async Task ASlowUploadHoldsOnlyItsOwnSourceSlot()
    {
        await using var server = await DiagnosticTestServer.StartAsync(options => options.ConcurrentUploads = 1);
        var (stalled, pending) = await StartStalledUpload(server, "203.0.113.9");

        var probe = Guid.NewGuid();
        using var same = await server.Upload(probe, Payload(probe), from: "203.0.113.9");
        Assert.Equal(HttpStatusCode.TooManyRequests, same.StatusCode);

        var other = Guid.NewGuid();
        using var response = await server.Upload(other, Payload(other), from: "198.51.100.7");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        stalled.Release.SetResult();
        using var finished = await pending;
        Assert.Equal(HttpStatusCode.BadRequest, finished.StatusCode);
    }

    /// <summary>곳마다만 세면 주소를 여럿 쓰는 쪽이 업로드를 끝없이 겹친다. 서버 전체의 동시 처리에도 상한이 있다.</summary>
    [Fact]
    public async Task ManySourcesCannotExceedTheServerWideConcurrency()
    {
        await using var server = await DiagnosticTestServer.StartAsync(options => options.TotalConcurrentUploads = 1);
        var (stalled, pending) = await StartStalledUpload(server, "203.0.113.9");

        var other = Guid.NewGuid();
        using (var response = await server.Upload(other, Payload(other), from: "198.51.100.7"))
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);

        stalled.Release.SetResult();
        using (await pending) { }
        using var afterwards = await server.Upload(other, Payload(other), from: "198.51.100.7");
        Assert.Equal(HttpStatusCode.Created, afterwards.StatusCode);
    }

    /// <summary>
    /// 빈도는 곳마다만 센다. 서버 전체로 세면 몇 곳이 헤더만 틀린 값싼 요청으로 1분 몫을 다 써 정상 제보를 막는다.
    /// </summary>
    [Fact]
    public async Task CheapInvalidRequestsFromManySourcesDoNotBlockAValidReport()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        for (var source = 1; source <= 10; source++)
        {
            for (var attempt = 0; attempt < server.Options.UploadsPerMinute; attempt++)
            {
                var id = Guid.NewGuid();
                using var invalid = await server.Upload(id, Payload(id), new string('0', 64), from: "203.0.113." + source);
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            }
        }

        var valid = Guid.NewGuid();
        using var response = await server.Upload(valid, Payload(valid), from: "198.51.100.7");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// 기준보다 느리게 흘리는 업로드는 유예 뒤에 끊겨 서버 전체의 동시 처리 몫을 내놓는다. 그러지 않으면 몇 곳이
    /// 아주 느린 연결로 업로드 시간 제한 내내 모두를 막는다.
    /// </summary>
    [Fact]
    public async Task AnUploadBelowTheMinimumRateGivesUpItsServerWideSlot()
    {
        await using var server = await DiagnosticTestServer.StartAsync(options =>
        {
            options.TotalConcurrentUploads = 1;
            options.MinimumUploadRateGraceSeconds = 2;
        });
        // 초당 1KB. Kestrel 기본 최소 속도(초당 240바이트)는 넘고 서버의 기준(초당 4KB)에는 못 미친다.
        var (stalled, pending) = await StartStalledUpload(server, "203.0.113.9", bytesPerQuarterSecond: 256);

        var other = Guid.NewGuid();
        using (var blocked = await server.Upload(other, Payload(other), from: "198.51.100.7"))
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);

        // 시험 클라이언트의 시간 제한(10초)이 느린 요청을 먼저 끊으면 서버가 끊은 것과 구별되지 않는다. 유예 2초에
        // Kestrel 의 1초 점검 주기를 더해도 넉넉한 6초 안에 풀려야 한다.
        var accepted = false;
        var deadline = DateTime.UtcNow.AddSeconds(6);
        while (!accepted && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
            using var retry = await server.Upload(other, Payload(other), from: "198.51.100.7");
            accepted = retry.StatusCode == HttpStatusCode.Created;
        }
        Assert.True(accepted);

        stalled.Release.SetResult();
        // 끊긴 쪽은 응답 대신 연결 오류를 받을 수도 있다. 여기서 보는 것은 몫이 풀렸다는 것뿐이다.
        await Record.ExceptionAsync(async () => (await pending).Dispose());
    }

    /// <summary>본문을 조금 보낸 뒤 멈춘 업로드를 연다. 서버가 그 요청으로 동시 처리 몫을 쥔 뒤에 돌려준다.</summary>
    private static async Task<(StalledContent Stalled, Task<HttpResponseMessage> Pending)> StartStalledUpload(
        DiagnosticTestServer server, string from, int bytesPerQuarterSecond = 0)
    {
        var stalled = new StalledContent(bytesPerQuarterSecond);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports") { Content = stalled };
        request.Headers.Add("X-SephPlanner-Report-Id", Guid.NewGuid().ToString("N"));
        request.Headers.Add("X-SephPlanner-SHA256", new string('0', 64));
        request.Headers.Add("X-Forwarded-For", from);
        var pending = server.Client.SendAsync(request);
        await stalled.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        // 저장소가 임시 파일을 만들었으면 느린 요청이 한도를 지나 몫을 쥔 것이다. 그 전에 다른 요청을 보내면
        // 그쪽이 먼저 몫을 쥐어 느린 요청이 거절될 수 있다.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!Directory.EnumerateFiles(server.Options.StorageDirectory, "*.upload").Any() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.NotEmpty(Directory.EnumerateFiles(server.Options.StorageDirectory, "*.upload"));
        return (stalled, pending);
    }

    private sealed class StalledContent : HttpContent
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly int _trickle;

        /// <param name="bytesPerQuarterSecond">0 이면 멈추고, 아니면 풀릴 때까지 0.25초마다 이만큼 흘린다.</param>
        public StalledContent(int bytesPerQuarterSecond = 0)
        {
            _trickle = bytesPerQuarterSecond;
            Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(new byte[16]);
            await stream.FlushAsync();
            Started.SetResult();
            while (_trickle > 0 && !Release.Task.IsCompleted)
            {
                await stream.WriteAsync(new byte[_trickle]);
                await stream.FlushAsync();
                await Task.WhenAny(Release.Task, Task.Delay(250));
            }
            await Release.Task;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    [Fact]
    public async Task APruneFailureKeepsTheServiceRunningAndShowsOnHealth()
    {
        using var directory = new DiagnosticTestDirectory();
        var options = new DiagnosticServerOptions { StorageDirectory = Path.Combine(directory.Path, "reports") };
        using var store = new DiagnosticStore(options);
        var health = new DiagnosticHealth();
        Directory.Delete(options.StorageDirectory);
        using var retention = new DiagnosticRetention(store, health, NullLogger<DiagnosticRetention>.Instance);

        await retention.StartAsync(CancellationToken.None);
        for (var attempt = 0; attempt < 100 && health.Failure is null; attempt++) await Task.Delay(20);

        Assert.NotNull(health.Failure);
        Assert.False(retention.ExecuteTask!.IsCompleted);
        await retention.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task HealthReportsAFailedPrune()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        server.Health.Failure = "보관 기한이 지난 진단을 지우지 못했습니다.";
        using var failing = await server.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failing.StatusCode);
        server.Health.Failure = null;
        using var healthy = await server.Client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthy.StatusCode);
    }

    [Fact]
    public async Task OneReportThatIsNotUtf8DoesNotBreakTheListing()
    {
        await using var server = await DiagnosticTestServer.StartAsync();
        var good = Guid.NewGuid();
        using (var response = await server.Upload(good, Payload(good))) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var broken = Path.Combine(server.Options.StorageDirectory, Guid.NewGuid().ToString("N") + ".zip");
        using (var file = File.Create(broken))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry("report.json").Open())
            entry.Write([0x7b, 0xff, 0xfe, 0x7d]);

        server.Authorize();
        using var list = await server.Client.GetAsync("/admin/reports");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
        Assert.Equal(2, json.RootElement.GetArrayLength());
    }
}

internal sealed class DiagnosticTestServer : IAsyncDisposable
{
    private readonly DiagnosticTestDirectory _directory = new();
    private readonly string _token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private WebApplication? _app;
    internal HttpClient Client { get; private set; } = null!;
    internal DiagnosticServerOptions Options { get; private set; } = null!;
    internal DiagnosticHealth Health => _app!.Services.GetRequiredService<DiagnosticHealth>();

    internal static async Task<DiagnosticTestServer> StartAsync(Action<DiagnosticServerOptions>? configure = null)
    {
        var server = new DiagnosticTestServer();
        try
        {
            var tokenFile = Path.Combine(server._directory.Path, "admin-token");
            await File.WriteAllTextAsync(tokenFile, server._token);
            server.Options = new DiagnosticServerOptions
            {
                StorageDirectory = Path.Combine(server._directory.Path, "reports"),
                AdminTokenFile = tokenFile,
                TrustedProxies = ["127.0.0.1"],
            };
            configure?.Invoke(server.Options);
            server._app = DiagnosticServer.Build(["--urls", "http://127.0.0.1:0", "--environment", "Production", "--Logging:LogLevel:Default", "Warning"], server.Options);
            await server._app.StartAsync();
            var address = Assert.Single(server._app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses);
            // Expect: 100-continue 의 기다림은 기본 1초다. 그 안에 서버의 거절이 안 오면 클라이언트가
            // 본문을 보내기 시작하고, 거절된 큰 본문은 응답 대신 소켓 오류가 된다. 부하가 걸린
            // CI 에서는 1초를 넘길 수 있으므로 시험의 제한 시간 안에서는 기다리게 한다.
            var handler = new SocketsHttpHandler { Expect100ContinueTimeout = TimeSpan.FromSeconds(10) };
            server.Client = new HttpClient(handler) { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
            server.Client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
            return server;
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    internal void Authorize() => Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);

    internal async Task<HttpResponseMessage> Upload(Guid id, byte[] archive, string? hash = null, string? from = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports");
        if (from != null) request.Headers.Add("X-Forwarded-For", from);
        request.Headers.Add("X-SephPlanner-Report-Id", id.ToString("N"));
        request.Headers.Add("X-SephPlanner-SHA256", hash ?? DiagnosticArchive.Hash(archive));
        // 서버가 너무 큰 본문을 다 받기 전에 거절하면 보내던 연결이 끊겨 응답 대신 소켓 오류가
        // 난다. 헤더만 먼저 보내고 거절을 본문 앞에서 받는다.
        request.Headers.ExpectContinue = true;
        request.Content = new ByteArrayContent(archive);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return await Client.SendAsync(request);
    }

    /// <summary>메서드 글자를 그대로 보내는 업로드. 상태 코드만 돌려준다.</summary>
    internal async Task<int> UploadRaw(string method)
    {
        var id = Guid.NewGuid();
        var archive = DiagnosticArchive.Create(new Dictionary<string, string>
        {
            ["report.json"] = JsonSerializer.Serialize(new { Version = DiagnosticArchive.SchemaVersion, ReportId = id.ToString("N") }),
        });
        using var tcp = new System.Net.Sockets.TcpClient();
        await tcp.ConnectAsync(Client.BaseAddress!.Host, Client.BaseAddress.Port);
        var stream = tcp.GetStream();
        var head = $"{method} /api/v1/reports HTTP/1.1\r\nHost: localhost\r\nX-Forwarded-Proto: https\r\nContent-Type: application/zip\r\n" +
                   $"X-SephPlanner-Report-Id: {id:N}\r\nX-SephPlanner-SHA256: {DiagnosticArchive.Hash(archive)}\r\n" +
                   $"Content-Length: {archive.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(head));
        await stream.WriteAsync(archive);
        var status = (await new StreamReader(stream).ReadLineAsync())!.Split(' ')[1];
        return int.Parse(status, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
        _directory.Dispose();
    }
}

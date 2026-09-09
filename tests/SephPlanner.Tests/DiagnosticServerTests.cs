using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public async Task ExpiredFilesAndInterruptedUploadsAreRemovedOnRestart()
    {
        using var directory = new DiagnosticTestDirectory();
        var options = new DiagnosticServerOptions { StorageDirectory = directory.Path };
        var id = Guid.NewGuid();
        var archive = Payload(id);
        using (var store = new DiagnosticStore(options))
        using (var input = new MemoryStream(archive))
            Assert.Equal(DiagnosticSaveResult.Created, await store.SaveAsync(id, DiagnosticArchive.Hash(archive), input, CancellationToken.None));
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
}

internal sealed class DiagnosticTestServer : IAsyncDisposable
{
    private readonly DiagnosticTestDirectory _directory = new();
    private readonly string _token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    private WebApplication? _app;
    internal HttpClient Client { get; private set; } = null!;
    internal DiagnosticServerOptions Options { get; private set; } = null!;

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
            server.Client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(10) };
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

    internal async Task<HttpResponseMessage> Upload(Guid id, byte[] archive, string? hash = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/reports");
        request.Headers.Add("X-SephPlanner-Report-Id", id.ToString("N"));
        request.Headers.Add("X-SephPlanner-SHA256", hash ?? DiagnosticArchive.Hash(archive));
        request.Content = new ByteArrayContent(archive);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        return await Client.SendAsync(request);
    }

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        if (_app != null) await _app.DisposeAsync();
        _directory.Dispose();
    }
}

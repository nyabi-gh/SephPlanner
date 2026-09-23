using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public class UpdateTests
{
    [Fact]
    public void NormalizeDropsTheRevisionSoAssemblyAndTagVersionsCompareEqual()
    {
        Assert.Equal(new Version(0, 3, 9), UpdateClient.Normalize(new Version(0, 3, 9, 0)));
        Assert.Equal("0.3.9", UpdateClient.Format(new Version(0, 3, 9, 0)));
        var asset = OperatingSystem.IsMacOS() ? "SephPlanner-macos-v0.3.9.zip" : "SephPlanner-v0.3.9.zip";
        Assert.Equal(new Uri("https://github.com/nyabi-gh/SephPlanner/releases/download/v0.3.9/" + asset),
            UpdateClient.AssetOf(new Version(0, 3, 9, 0)));
    }

    [Theory]
    [InlineData("/nyabi-gh/SephPlanner/releases/tag/v0.3.10", "0.3.10")]
    [InlineData("https://github.com/nyabi-gh/SephPlanner/releases/tag/v0.4.0", "0.4.0")]
    [InlineData("/nyabi-gh/SephPlanner/releases/tag/v0.3.9", null)]
    [InlineData("/nyabi-gh/SephPlanner/releases/tag/v0.3.8", null)]
    public async Task CheckReportsOnlyAStableVersionNewerThanTheRunningOne(string location, string? expected)
    {
        var handler = new DiagnosticHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Head, request.Method);
            Assert.Equal(UpdateClient.LatestRelease, request.RequestUri);
            Assert.NotEmpty(request.Headers.UserAgent);
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        });
        using var client = new UpdateClient(handler);
        var latest = await client.CheckAsync(new Version(0, 3, 9, 0), CancellationToken.None);
        Assert.Equal(expected == null ? null : Version.Parse(expected), latest);
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "/nyabi-gh/SephPlanner/releases/tag/v0.3.10", typeof(HttpRequestException))]
    [InlineData(HttpStatusCode.Found, "https://example.com/nyabi-gh/SephPlanner/releases/tag/v0.3.10", typeof(InvalidDataException))]
    [InlineData(HttpStatusCode.Found, "/nyabi-gh/SephPlanner/releases", typeof(InvalidDataException))]
    [InlineData(HttpStatusCode.Found, "/nyabi-gh/SephPlanner/releases/tag/v1", typeof(InvalidDataException))]
    public async Task CheckRejectsAnswersThatAreNotAGitHubReleaseTag(HttpStatusCode status, string location, Type failure)
    {
        var handler = new DiagnosticHandler((_, _) =>
        {
            var response = new HttpResponseMessage(status);
            response.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
            return Task.FromResult(response);
        });
        using var client = new UpdateClient(handler);
        await Assert.ThrowsAsync(failure, () => client.CheckAsync(new Version(0, 3, 9), CancellationToken.None));
    }

    [Fact]
    public async Task DownloadFollowsTheAssetRedirectAndReturnsTheBytes()
    {
        var payload = Encoding.UTF8.GetBytes("zip bytes");
        var asset = UpdateClient.AssetOf(new Version(0, 3, 10));
        var storage = new Uri("https://release-assets.githubusercontent.com/some/path?token=x");
        var handler = new DiagnosticHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            if (request.RequestUri == asset)
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.Found);
                redirect.Headers.Location = storage;
                return Task.FromResult(redirect);
            }
            Assert.Equal(storage, request.RequestUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        });
        using var client = new UpdateClient(handler);
        Assert.Equal(payload, await client.DownloadAsync(new Version(0, 3, 10), CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }

    [Theory]
    [InlineData("http://release-assets.githubusercontent.com/some/path")]
    [InlineData("https://user:secret@release-assets.githubusercontent.com/some/path")]
    public async Task DownloadRefusesToFollowAnUnsafeRedirect(string location)
    {
        var handler = new DiagnosticHandler((_, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri(location);
            return Task.FromResult(redirect);
        });
        using var client = new UpdateClient(handler);
        await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(new Version(0, 3, 10), CancellationToken.None));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task DownloadGivesUpOnRedirectLoopsAndRejectsOtherStatuses()
    {
        var looping = new DiagnosticHandler((request, _) =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri(request.RequestUri + "/again");
            return Task.FromResult(redirect);
        });
        using (var client = new UpdateClient(looping))
            await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(new Version(0, 3, 10), CancellationToken.None));
        Assert.Equal(6, looping.Calls);

        var missing = new DiagnosticHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));
        using (var client = new UpdateClient(missing))
            await Assert.ThrowsAsync<HttpRequestException>(() => client.DownloadAsync(new Version(0, 3, 10), CancellationToken.None));
    }

    [Fact]
    public async Task DownloadRejectsAnAssetLargerThanTheCap()
    {
        var handler = new DiagnosticHandler((_, _) =>
        {
            var content = new ByteArrayContent([1]);
            content.Headers.ContentLength = UpdateClient.MaximumAssetBytes + 1L;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        using var client = new UpdateClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(new Version(0, 3, 10), CancellationToken.None));
    }

    [Fact]
    public void ExtractReturnsBothPluginFilesWhenTheManifestAgrees()
    {
        var plugin = Encoding.UTF8.GetBytes("plugin");
        var core = Encoding.UTF8.GetBytes("core");
        var files = UpdatePackage.Extract(Package("0.3.10", plugin, core), new Version(0, 3, 10, 0));
        Assert.Equal(plugin, files[UpdatePackage.PluginFile]);
        Assert.Equal(core, files[UpdatePackage.CoreFile]);
        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void ExtractRejectsAnotherVersionATamperedFileOrAMissingEntry()
    {
        var plugin = Encoding.UTF8.GetBytes("plugin");
        var core = Encoding.UTF8.GetBytes("core");
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(Package("0.3.9", plugin, core), new Version(0, 3, 10)));
        Assert.Throws<InvalidDataException>(() =>
            UpdatePackage.Extract(Package("0.3.10", plugin, core, coreHashOf: Encoding.UTF8.GetBytes("other")), new Version(0, 3, 10)));
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(Package("0.3.10", plugin, null), new Version(0, 3, 10)));
        Assert.Throws<InvalidDataException>(() => UpdatePackage.Extract(Package(null, plugin, core), new Version(0, 3, 10)));
    }

    [Fact]
    public void InstallSwapsTheFilesKeepsTheOldOnesAndCleanupReportsThemOnce()
    {
        using var directory = new DiagnosticTestDirectory();
        var targets = Targets(directory.Path);
        File.WriteAllText(targets[UpdatePackage.PluginFile], "old plugin");
        File.WriteAllText(targets[UpdatePackage.CoreFile], "old core");

        UpdateInstaller.Install(new Dictionary<string, byte[]>
        {
            [UpdatePackage.PluginFile] = Encoding.UTF8.GetBytes("new plugin"),
            [UpdatePackage.CoreFile] = Encoding.UTF8.GetBytes("new core"),
        }, targets);

        Assert.Equal("new plugin", File.ReadAllText(targets[UpdatePackage.PluginFile]));
        Assert.Equal("new core", File.ReadAllText(targets[UpdatePackage.CoreFile]));
        Assert.Equal("old plugin", File.ReadAllText(Path.Combine(directory.Path, "SephPlanner.Plugin.old")));
        Assert.Equal("old core", File.ReadAllText(Path.Combine(directory.Path, "SephPlanner.Core.old")));
        Assert.Equal(4, Directory.GetFiles(directory.Path).Length);

        Assert.True(UpdateInstaller.CleanRetired(targets.Values));
        Assert.Equal(2, Directory.GetFiles(directory.Path).Length);
        Assert.False(UpdateInstaller.CleanRetired(targets.Values));
    }

    [Fact]
    public void InstallRestoresTheFirstFileWhenTheSecondSwapFails()
    {
        using var directory = new DiagnosticTestDirectory();
        var targets = Targets(directory.Path);
        File.WriteAllText(targets[UpdatePackage.PluginFile], "old plugin");
        File.WriteAllText(targets[UpdatePackage.CoreFile], "old core");
        // 옛 파일이 갈 자리를 폴더가 막고 있으면 두 번째 교체가 실패한다.
        Directory.CreateDirectory(Path.Combine(directory.Path, "SephPlanner.Core.old"));

        Assert.ThrowsAny<Exception>(() => UpdateInstaller.Install(new Dictionary<string, byte[]>
        {
            [UpdatePackage.PluginFile] = Encoding.UTF8.GetBytes("new plugin"),
            [UpdatePackage.CoreFile] = Encoding.UTF8.GetBytes("new core"),
        }, targets));

        Assert.Equal("old plugin", File.ReadAllText(targets[UpdatePackage.PluginFile]));
        Assert.Equal("old core", File.ReadAllText(targets[UpdatePackage.CoreFile]));
        Assert.Equal(2, Directory.GetFiles(directory.Path).Length);
    }

    private static Dictionary<string, string> Targets(string directory) => new()
    {
        [UpdatePackage.PluginFile] = Path.Combine(directory, UpdatePackage.PluginFile),
        [UpdatePackage.CoreFile] = Path.Combine(directory, UpdatePackage.CoreFile),
    };

    /// <summary><c>make-release.ps1</c> 이 만드는 zip 의 모양. manifest 의 경로는 zip 루트 폴더 기준이다.</summary>
    private static byte[] Package(string? version, byte[] plugin, byte[]? core, byte[]? coreHashOf = null)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            Add(archive, UpdatePackage.PluginsEntry + UpdatePackage.PluginFile, plugin);
            if (core != null) Add(archive, UpdatePackage.PluginsEntry + UpdatePackage.CoreFile, core);
            Add(archive, "SephPlanner/설치안내.txt", Encoding.UTF8.GetBytes("guide"));
            if (version != null)
            {
                var manifest = "{\n  \"version\": \"" + version + "\",\n  \"commit\": \"abc\",\n  \"files\": [\n" +
                    "    {\n      \"path\": \"게임 폴더에 복사/BepInEx/plugins/SephPlanner.Plugin.dll\",\n      \"sha256\": \"" + DiagnosticArchive.Hash(plugin) + "\"\n    },\n" +
                    "    {\n      \"path\": \"게임 폴더에 복사/BepInEx/plugins/SephPlanner.Core.dll\",\n      \"sha256\": \"" + DiagnosticArchive.Hash(coreHashOf ?? core ?? []) + "\"\n    },\n" +
                    "    {\n      \"path\": \"설치안내.txt\",\n      \"sha256\": \"" + DiagnosticArchive.Hash(Encoding.UTF8.GetBytes("guide")) + "\"\n    }\n  ]\n}";
                Add(archive, UpdatePackage.ManifestEntry, Encoding.UTF8.GetBytes(manifest));
            }
        }
        return output.ToArray();
    }

    private static void Add(ZipArchive archive, string name, byte[] content)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(content, 0, content.Length);
    }
}

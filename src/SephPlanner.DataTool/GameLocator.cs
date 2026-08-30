using System.Text.RegularExpressions;

namespace SephPlanner.DataTool;

/// <summary>
/// Steam 라이브러리 폴더 목록을 읽어 세피리아 설치 경로를 찾는다.
/// 사용자마다 드라이브가 다르므로 경로를 하드코딩하지 않는다.
/// </summary>
public static class GameLocator
{
    private const string AppId = "2436940";

    public static string? Find()
    {
        var env = Environment.GetEnvironmentVariable("SEPHIRIA_DIR");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return env;

        foreach (var steam in SteamRoots())
        {
            var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            foreach (var lib in ParseLibraryPaths(File.ReadAllText(vdf)))
            {
                // 해당 라이브러리에 앱이 실제로 설치돼 있는지는 매니페스트로 확인한다.
                if (!File.Exists(Path.Combine(lib, "steamapps", $"appmanifest_{AppId}.acf"))) continue;

                var dir = Path.Combine(lib, "steamapps", "common", "Sephiria");
                if (Directory.Exists(dir)) return dir;
            }
        }
        return null;
    }

    private static IEnumerable<string> SteamRoots()
    {
        yield return @"C:\Program Files (x86)\Steam";
        yield return @"C:\Program Files\Steam";
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            yield return Path.Combine(d.Name, "Steam");
            yield return Path.Combine(d.Name, "SteamLibrary");
        }
    }

    private static IEnumerable<string> ParseLibraryPaths(string vdf)
    {
        // libraryfolders.vdf 는  "path"  "D:\\SteamLibrary"  형태다.
        const string pattern = "\"path\"\\s*\"([^\"]+)\"";
        foreach (Match m in Regex.Matches(vdf, pattern))
            yield return m.Groups[1].Value.Replace("\\\\", "\\");
    }

    public static string LocalizationDir(string gameDir) =>
        Path.Combine(gameDir, "Sephiria_Data", "StreamingAssets", "Localization");
}

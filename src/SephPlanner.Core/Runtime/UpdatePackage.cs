using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 배포 zip 에서 플러그인 DLL 둘을 꺼낸다. 로더(BepInEx)와 안내문은 건드리지 않는다 -
    /// README 의 업데이트 절차가 DLL 둘만 바꾸라고 하는 것과 같은 범위다.
    ///
    /// 검증은 zip 안의 <c>manifest.json</c> 으로 한다. <c>make-release.ps1</c> 이 파일마다 SHA-256 을
    /// 적어 두므로 받다가 깨진 파일이나 버전이 다른 zip 을 여기서 걸러낸다.
    /// </summary>
    public static class UpdatePackage
    {
        public const string PluginFile = "SephPlanner.Plugin.dll";
        public const string CoreFile = "SephPlanner.Core.dll";
        public const string PluginsEntry = "SephPlanner/게임 폴더에 복사/BepInEx/plugins/";
        public const string ManifestEntry = "SephPlanner/manifest.json";
        public static readonly IReadOnlyList<string> Files = new[] { PluginFile, CoreFile };
        private const int MaximumFileBytes = 16 * 1024 * 1024;

        // manifest 는 우리가 만든 파일이라 모양이 정해져 있다. 파서를 들이지 않고 항목만 집는다.
        private static readonly Regex FileHash = new Regex(
            "\"path\":\\s*\"(?<path>[^\"]+)\",\\s*\"sha256\":\\s*\"(?<hash>[0-9a-f]{64})\"", RegexOptions.CultureInvariant);
        private static readonly Regex ManifestVersion = new Regex(
            "\"version\":\\s*\"(?<version>[0-9.]+)\"", RegexOptions.CultureInvariant);

        /// <summary>파일 이름 → 내용. 기대한 버전이 아니거나 해시가 어긋나면 던진다.</summary>
        public static Dictionary<string, byte[]> Extract(byte[] zip, Version expected)
        {
            using var input = new MemoryStream(zip, false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, false);
            var manifest = Encoding.UTF8.GetString(ReadEntry(archive, ManifestEntry));
            var version = ManifestVersion.Match(manifest);
            if (!version.Success || !Version.TryParse(version.Groups["version"].Value, out var packaged) ||
                UpdateClient.Normalize(packaged) != UpdateClient.Normalize(expected))
                throw new InvalidDataException("배포 파일의 버전이 기대한 " + UpdateClient.Format(expected) + " 이 아닙니다.");

            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in FileHash.Matches(manifest))
                hashes[match.Groups["path"].Value] = match.Groups["hash"].Value;

            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var name in Files)
            {
                var path = PluginsEntry + name;
                var content = ReadEntry(archive, path);
                if (!hashes.TryGetValue(path.Substring("SephPlanner/".Length), out var hash) || DiagnosticArchive.Hash(content) != hash)
                    throw new InvalidDataException("배포 파일의 " + name + " 이 manifest 와 다릅니다.");
                files[name] = content;
            }
            return files;
        }

        private static byte[] ReadEntry(ZipArchive archive, string path)
        {
            var entry = archive.GetEntry(path) ?? throw new InvalidDataException("배포 파일에 " + path + " 가 없습니다.");
            if (entry.Length > MaximumFileBytes) throw new InvalidDataException("배포 파일의 " + path + " 가 너무 큽니다.");
            using var source = entry.Open();
            using var content = new MemoryStream((int)entry.Length);
            var buffer = new byte[16 * 1024];
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (content.Length + read > MaximumFileBytes) throw new InvalidDataException("배포 파일의 " + path + " 가 너무 큽니다.");
                content.Write(buffer, 0, read);
            }
            return content.ToArray();
        }
    }

    /// <summary>
    /// 받은 DLL 을 지금 로드된 파일의 자리에 놓는다.
    ///
    /// <b>로드된 DLL 은 덮어쓸 수도 지울 수도 없지만 이름은 바꿀 수 있다.</b> 게임을 켠 채로
    /// <c>Rename-Item</c> 이 되는 것을 실기에서 확인했다(2026-09-13). 그래서 옛 파일을 <c>.old</c> 로
    /// 밀어 두고 새 파일을 그 이름으로 옮긴다. 이번 실행은 옛 코드가 그대로 돌고 다음 실행부터
    /// 새 코드다. BepInEx 는 <c>*.dll</c> 만 올리므로 <c>.old</c> 는 올라오지 않는다.
    ///
    /// 확장자를 <c>.dll.old</c> 로 하지 않는 것은 윈도우의 짧은 이름 규칙 때문이다 - 확장자가
    /// 석 자인 패턴은 그보다 긴 확장자에도 맞을 수 있어(<c>*.dll</c> 이 <c>x.dll_</c> 을 잡는 식)
    /// 로더가 옛 파일을 다시 올릴 여지를 남긴다.
    /// </summary>
    public static class UpdateInstaller
    {
        public const string RetiredExtension = ".old";
        private const string StagedExtension = ".new";

        public static string Retired(string target) => Path.ChangeExtension(target, RetiredExtension);

        /// <summary>
        /// 이름 → 지금 파일 경로. 새 내용을 먼저 옆에 다 써 두고 나서 이름을 바꾸므로, 받다가
        /// 끊긴 파일이 자리를 차지하는 일이 없다. 도중에 실패하면 이미 옮긴 것을 되돌린다.
        /// </summary>
        public static void Install(IReadOnlyDictionary<string, byte[]> files, IReadOnlyDictionary<string, string> targets)
        {
            foreach (var name in UpdatePackage.Files)
            {
                if (!files.ContainsKey(name)) throw new InvalidDataException("받은 파일에 " + name + " 이 없습니다.");
                if (!targets.ContainsKey(name)) throw new InvalidOperationException(name + " 의 설치 위치를 모릅니다.");
            }

            var staged = new List<string>();
            var swapped = new List<string>();
            try
            {
                foreach (var name in UpdatePackage.Files)
                {
                    var path = Path.ChangeExtension(targets[name], StagedExtension);
                    File.WriteAllBytes(path, files[name]);
                    staged.Add(path);
                }
                foreach (var name in UpdatePackage.Files)
                {
                    var target = targets[name];
                    var retired = Retired(target);
                    if (File.Exists(retired)) File.Delete(retired);
                    File.Move(target, retired);
                    swapped.Add(target);
                    File.Move(Path.ChangeExtension(target, StagedExtension), target);
                }
            }
            catch
            {
                foreach (var target in swapped)
                {
                    try
                    {
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(Retired(target), target);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                foreach (var path in staged)
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                throw;
            }
        }

        /// <summary>
        /// 지난 실행이 밀어 둔 <c>.old</c> 를 지운다. 하나라도 있었으면 방금 새 버전으로 올라온
        /// 것이므로 참을 돌려준다 - 그것이 "업데이트됐다" 를 알리는 유일한 표시다.
        /// </summary>
        public static bool CleanRetired(IEnumerable<string> targets)
        {
            var found = false;
            foreach (var target in targets)
            {
                var retired = Retired(target);
                if (!File.Exists(retired)) continue;
                found = true;
                File.Delete(retired);
            }
            return found;
        }
    }
}

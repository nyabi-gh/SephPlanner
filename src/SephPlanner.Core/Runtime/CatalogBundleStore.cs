using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SephPlanner.Core.Runtime
{
    public enum CatalogRefreshStatus
    {
        Unavailable,
        Refreshing,
        Failed,
        Ready,
    }

    public sealed class CatalogBundleInfo
    {
        public string Generation { get; set; } = "";
        public string Directory { get; set; } = "";
        public string GameVersion { get; set; } = "";
        public string GameAssemblyId { get; set; } = "";
        public int Comparisons { get; set; }
        public int Mismatches { get; set; }

        public bool VerificationPassed => Comparisons > 0 && Mismatches == 0;
    }

    public sealed class CatalogBundleWriter
    {
        private readonly string _dataDirectory;
        private readonly string _gameVersion;
        private readonly string _gameAssemblyId;

        internal CatalogBundleWriter(
            string dataDirectory, string generation, string gameVersion, string gameAssemblyId)
        {
            _dataDirectory = dataDirectory;
            Generation = generation;
            _gameVersion = gameVersion;
            _gameAssemblyId = gameAssemblyId;
            GenerationDirectory = Path.Combine(
                dataDirectory, PlannerData.CatalogGenerationsDirectory, generation);
            Directory.CreateDirectory(GenerationDirectory);
        }

        public string Generation { get; }
        public string GenerationDirectory { get; }

        public void WriteText(string fileName, string content)
        {
            if (!PlannerData.IsCatalogFile(fileName))
                throw new ArgumentException("카탈로그 묶음에 허용되지 않은 파일입니다.", nameof(fileName));
            CatalogBundleStore.WriteAtomic(Path.Combine(GenerationDirectory, fileName), content);
        }

        public void Publish(int comparisons, int mismatches)
        {
            var files = new List<CatalogBundleStore.FileRecord>();
            foreach (var fileName in PlannerData.RequiredCatalogFiles)
            {
                var path = Path.Combine(GenerationDirectory, fileName);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidDataException("카탈로그 파일이 없거나 비었습니다: " + fileName);
                files.Add(CatalogBundleStore.DescribeFile(path, fileName));
            }

            var manifest = CatalogBundleStore.FormatManifest(
                Generation, _gameVersion, _gameAssemblyId, comparisons, mismatches, files);
            CatalogBundleStore.WriteAtomic(
                Path.Combine(GenerationDirectory, PlannerData.CatalogManifestFile), manifest);

            if (!CatalogBundleStore.TryReadGeneration(
                    _dataDirectory, Generation, _gameVersion, _gameAssemblyId,
                    out _, out var error))
                throw new InvalidDataException("게시 전 카탈로그 재검증 실패: " + error);

            CatalogBundleStore.WriteAtomic(
                Path.Combine(_dataDirectory, PlannerData.ActiveCatalogFile), Generation);
            CatalogBundleStore.MarkReady(_dataDirectory, Generation);
            CatalogBundleStore.PruneOtherGenerations(_dataDirectory, Generation);
        }
    }

    public static class CatalogBundleStore
    {
        internal sealed class FileRecord
        {
            public string Name = "";
            public long Length;
            public string Sha256 = "";
        }

        private const string ManifestHeader = "SEPHPLANNER-CATALOG-1";

        public static string NewGeneration() =>
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" +
            Guid.NewGuid().ToString("N");

        public static CatalogBundleWriter Begin(
            string dataDirectory, string generation, string gameVersion, string gameAssemblyId)
        {
            if (!IsGeneration(generation))
                throw new ArgumentException("카탈로그 generation 형식이 올바르지 않습니다.", nameof(generation));

            Directory.CreateDirectory(dataDirectory);
            MarkState(dataDirectory, CatalogRefreshStatus.Refreshing, generation, "");
            return new CatalogBundleWriter(dataDirectory, generation, gameVersion, gameAssemblyId);
        }

        public static void MarkFailed(string dataDirectory, string generation, string reason) =>
            MarkState(dataDirectory, CatalogRefreshStatus.Failed, generation, reason);

        public static bool TryGetActive(
            string dataDirectory, string? expectedGameVersion, string? expectedGameAssemblyId,
            out CatalogBundleInfo info, out string error)
        {
            try
            {
                return TryGetActiveCore(
                    dataDirectory, expectedGameVersion, expectedGameAssemblyId, out info, out error);
            }
            catch (Exception ex) when (
                ex is IOException || ex is UnauthorizedAccessException || ex is FormatException)
            {
                info = new CatalogBundleInfo();
                error = "카탈로그 묶음을 읽지 못했습니다: " + ex.Message;
                return false;
            }
        }

        private static bool TryGetActiveCore(
            string dataDirectory, string? expectedGameVersion, string? expectedGameAssemblyId,
            out CatalogBundleInfo info, out string error)
        {
            info = new CatalogBundleInfo();

            // 활성 카탈로그는 포인터와 그 세대의 manifest 만으로 정해진다. 갱신 상태 파일은 새
            // 세대의 진행 상황일 뿐이라 여기서 보지 않는다 - 보면 갱신을 시작하는 순간 멀쩡한
            // 이전 세대가 함께 죽고, 갱신이 실패하면 그 세션 내내 쓸 카탈로그가 없어진다.
            var pointer = Path.Combine(dataDirectory, PlannerData.ActiveCatalogFile);
            if (!File.Exists(pointer))
            {
                error = "활성 카탈로그가 없습니다.";
                return false;
            }

            return TryReadGeneration(
                dataDirectory, File.ReadAllText(pointer).Trim(),
                expectedGameVersion, expectedGameAssemblyId, out info, out error);
        }

        /// <summary>
        /// 마지막 갱신이 어디까지 갔는지. 활성 카탈로그와는 별개다 - 갱신이 실패해도 이전
        /// 카탈로그는 그대로 활성이고, 이 값은 화면에 "다시 시도하라"고 알리는 데만 쓴다.
        /// </summary>
        public static CatalogRefreshStatus ReadRefreshStatus(
            string dataDirectory, out string generation, out string reason)
        {
            generation = "";
            reason = "";
            try
            {
                return TryReadState(dataDirectory, out var state, out generation, out reason)
                    ? state
                    : CatalogRefreshStatus.Unavailable;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return CatalogRefreshStatus.Unavailable;
            }
        }

        /// <summary>
        /// 게시된 세대만 남기고 나머지 세대 디렉터리를 지운다. 없으면 F9 를 누를 때마다 한 벌씩
        /// 쌓인다. 지우지 못하는 것은 그냥 둔다 - 정리는 곁다리라 갱신을 실패시킬 이유가 없다.
        /// </summary>
        public static void PruneOtherGenerations(string dataDirectory, string keepGeneration)
        {
            var root = Path.Combine(dataDirectory, PlannerData.CatalogGenerationsDirectory);
            if (!Directory.Exists(root)) return;

            foreach (var directory in Directory.GetDirectories(root))
            {
                if (string.Equals(Path.GetFileName(directory), keepGeneration, StringComparison.Ordinal))
                    continue;
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                }
            }
        }

        internal static bool TryReadGeneration(
            string dataDirectory, string generation,
            string? expectedGameVersion, string? expectedGameAssemblyId,
            out CatalogBundleInfo info, out string error)
        {
            info = new CatalogBundleInfo();
            if (!IsGeneration(generation))
            {
                error = "카탈로그 generation 형식이 올바르지 않습니다.";
                return false;
            }

            var directory = Path.Combine(
                dataDirectory, PlannerData.CatalogGenerationsDirectory, generation);
            var manifestPath = Path.Combine(directory, PlannerData.CatalogManifestFile);
            if (!File.Exists(manifestPath) ||
                !TryParseManifest(File.ReadAllLines(manifestPath), out info, out var version, out var files))
            {
                error = "카탈로그 manifest를 읽을 수 없습니다.";
                return false;
            }
            info.Directory = directory;

            if (info.Generation != generation || version != PlannerData.CatalogVersion)
            {
                error = "카탈로그 generation 또는 형식 버전이 맞지 않습니다.";
                return false;
            }
            if (expectedGameVersion is not null && info.GameVersion != expectedGameVersion)
            {
                error = "게임 버전이 카탈로그 검증 버전과 다릅니다.";
                return false;
            }
            if (expectedGameAssemblyId is not null && info.GameAssemblyId != expectedGameAssemblyId)
            {
                error = "게임 어셈블리가 카탈로그 검증 대상과 다릅니다.";
                return false;
            }

            foreach (var required in PlannerData.RequiredCatalogFiles)
            {
                if (!files.TryGetValue(required, out var record))
                {
                    error = "manifest에 필요한 파일이 없습니다: " + required;
                    return false;
                }

                var path = Path.Combine(directory, required);
                if (!File.Exists(path) || new FileInfo(path).Length != record.Length ||
                    !string.Equals(HashFile(path), record.Sha256, StringComparison.Ordinal))
                {
                    error = "카탈로그 파일 검증에 실패했습니다: " + required;
                    return false;
                }
            }

            error = "";
            return true;
        }

        internal static FileRecord DescribeFile(string path, string name) => new FileRecord
        {
            Name = name,
            Length = new FileInfo(path).Length,
            Sha256 = HashFile(path),
        };

        internal static string FormatManifest(
            string generation, string gameVersion, string gameAssemblyId,
            int comparisons, int mismatches, IEnumerable<FileRecord> files)
        {
            var lines = new List<string>
            {
                ManifestHeader,
                "generation=" + generation,
                "catalogVersion=" + PlannerData.CatalogVersion.ToString(CultureInfo.InvariantCulture),
                "gameVersion=" + Encode(gameVersion),
                "gameAssembly=" + Encode(gameAssemblyId),
                "comparisons=" + comparisons.ToString(CultureInfo.InvariantCulture),
                "mismatches=" + mismatches.ToString(CultureInfo.InvariantCulture),
            };
            foreach (var file in files)
            {
                lines.Add("file=" + Encode(file.Name) + "|" +
                          file.Length.ToString(CultureInfo.InvariantCulture) + "|" + file.Sha256);
            }
            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        internal static void MarkReady(string dataDirectory, string generation) =>
            MarkState(dataDirectory, CatalogRefreshStatus.Ready, generation, "");

        public static void WriteAtomic(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, content);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }

        private static void MarkState(
            string dataDirectory, CatalogRefreshStatus state, string generation, string reason)
        {
            var content = state.ToString().ToLowerInvariant() + Environment.NewLine +
                          generation + Environment.NewLine + Encode(reason) + Environment.NewLine;
            WriteAtomic(Path.Combine(dataDirectory, PlannerData.CatalogRefreshStateFile), content);
        }

        private static bool TryReadState(
            string dataDirectory, out CatalogRefreshStatus state, out string generation, out string reason)
        {
            state = CatalogRefreshStatus.Unavailable;
            generation = "";
            reason = "";
            var path = Path.Combine(dataDirectory, PlannerData.CatalogRefreshStateFile);
            if (!File.Exists(path)) return false;

            var lines = File.ReadAllLines(path);
            if (lines.Length < 2 || !Enum.TryParse(lines[0], true, out state) ||
                !IsGeneration(lines[1]))
                return false;
            generation = lines[1];
            if (lines.Length >= 3)
            {
                try
                {
                    reason = Decode(lines[2]);
                }
                catch (FormatException)
                {
                    reason = "";
                }
            }
            return true;
        }

        private static bool TryParseManifest(
            string[] lines, out CatalogBundleInfo info, out int version,
            out Dictionary<string, FileRecord> files)
        {
            info = new CatalogBundleInfo();
            version = 0;
            files = new Dictionary<string, FileRecord>(StringComparer.Ordinal);
            if (lines.Length < 7 || lines[0] != ManifestHeader) return false;

            try
            {
                foreach (var line in lines)
                {
                    if (line.StartsWith("generation=", StringComparison.Ordinal))
                        info.Generation = line.Substring("generation=".Length);
                    else if (line.StartsWith("catalogVersion=", StringComparison.Ordinal))
                        version = int.Parse(line.Substring("catalogVersion=".Length), CultureInfo.InvariantCulture);
                    else if (line.StartsWith("gameVersion=", StringComparison.Ordinal))
                        info.GameVersion = Decode(line.Substring("gameVersion=".Length));
                    else if (line.StartsWith("gameAssembly=", StringComparison.Ordinal))
                        info.GameAssemblyId = Decode(line.Substring("gameAssembly=".Length));
                    else if (line.StartsWith("comparisons=", StringComparison.Ordinal))
                        info.Comparisons = int.Parse(line.Substring("comparisons=".Length), CultureInfo.InvariantCulture);
                    else if (line.StartsWith("mismatches=", StringComparison.Ordinal))
                        info.Mismatches = int.Parse(line.Substring("mismatches=".Length), CultureInfo.InvariantCulture);
                    else if (line.StartsWith("file=", StringComparison.Ordinal))
                    {
                        var parts = line.Substring("file=".Length).Split('|');
                        if (parts.Length != 3) return false;
                        var record = new FileRecord
                        {
                            Name = Decode(parts[0]),
                            Length = long.Parse(parts[1], CultureInfo.InvariantCulture),
                            Sha256 = parts[2],
                        };
                        if (!PlannerData.IsCatalogFile(record.Name) || !files.TryAdd(record.Name, record))
                            return false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return info.Generation.Length > 0 && info.GameVersion.Length > 0 &&
                   info.GameAssemblyId.Length > 0;
        }

        private static string HashFile(string path)
        {
            using var algorithm = SHA256.Create();
            using var stream = File.OpenRead(path);
            var hash = algorithm.ComputeHash(stream);
            var result = new StringBuilder(hash.Length * 2);
            foreach (var value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return result.ToString();
        }

        private static bool IsGeneration(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 96) return false;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_') return false;
            }
            return true;
        }

        private static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static string Decode(string value) =>
            Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}

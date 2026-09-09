using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SephPlanner.Core.Runtime
{
    public static class DiagnosticArchive
    {
        public const int SchemaVersion = 1;
        // 초기 운영 제한값이며 실측 최대 크기는 아니다. 서버에서는 더 낮은 운영 상한을 적용할 수 있다.
        public const int MaximumArchiveBytes = 8 * 1024 * 1024;
        public const int MaximumExpandedBytes = 16 * 1024 * 1024;
        public const int MaximumLogBytes = 256 * 1024;

        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
        { "report.json", "inventory-dump.txt", "inventory-snapshot.json", "plan.replay", "sephplanner.log" };

        public static byte[] Create(IReadOnlyDictionary<string, string> files)
        {
            if (!files.ContainsKey("report.json")) throw new InvalidDataException("진단 설명 파일이 없습니다.");
            using var output = new MemoryStream();
            long total = 0;
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var file in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!Names.Contains(file.Key)) throw new InvalidDataException("허용하지 않는 진단 파일입니다.");
                    var bytes = Encoding.UTF8.GetBytes(file.Value);
                    total += bytes.Length;
                    if (total > MaximumExpandedBytes) throw new InvalidDataException("진단 자료가 전송 크기 제한을 넘었습니다.");
                    using var stream = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open();
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            if (output.Length > MaximumArchiveBytes) throw new InvalidDataException("압축한 진단 자료가 전송 크기 제한을 넘었습니다.");
            return output.ToArray();
        }

        public static Dictionary<string, string> Read(Stream input)
        {
            if (!input.CanSeek || input.Length > MaximumArchiveBytes) throw new InvalidDataException("진단 압축 파일 크기가 올바르지 않습니다.");
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (archive.Entries.Count == 0 || archive.Entries.Count > Names.Count) throw new InvalidDataException("진단 파일 수가 올바르지 않습니다.");
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            long total = 0;
            var buffer = new byte[8192];
            foreach (var entry in archive.Entries)
            {
                if (!Names.Contains(entry.FullName) || files.ContainsKey(entry.FullName))
                    throw new InvalidDataException("중복되거나 허용하지 않는 진단 파일입니다.");
                if (entry.Length > MaximumExpandedBytes - total) throw new InvalidDataException("진단 내용이 크기 제한을 넘었습니다.");
                using var source = entry.Open();
                using var content = new MemoryStream();
                int read;
                while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
                {
                    total += read;
                    if (total > MaximumExpandedBytes) throw new InvalidDataException("진단 내용이 크기 제한을 넘었습니다.");
                    content.Write(buffer, 0, read);
                }
                files.Add(entry.FullName, new UTF8Encoding(false, true).GetString(content.ToArray()));
            }
            if (!files.ContainsKey("report.json")) throw new InvalidDataException("진단 설명 파일이 없습니다.");
            return files;
        }

        public static string Hash(byte[] content)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }
    }
}

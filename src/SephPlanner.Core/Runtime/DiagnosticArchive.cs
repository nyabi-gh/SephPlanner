using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SephPlanner.Core.Text;

namespace SephPlanner.Core.Runtime
{
    public static class DiagnosticArchive
    {
        public const int SchemaVersion = 1;
        // 초기 운영 제한값이며 실측 최대 크기는 아니다. 서버에서는 더 낮은 운영 상한을 적용할 수 있다.
        public const int MaximumArchiveBytes = 8 * 1024 * 1024;
        public const int MaximumExpandedBytes = 16 * 1024 * 1024;
        public const int MaximumLogBytes = 256 * 1024;
        public const int MaximumGameLogBytes = 64 * 1024;

        private static readonly HashSet<string> Names = new HashSet<string>(StringComparer.Ordinal)
        { "report.json", "inventory-dump.txt", "inventory-snapshot.json", "plan.replay", "sephplanner.log" };

        public static byte[] Create(IReadOnlyDictionary<string, string> files)
        {
            if (!files.ContainsKey("report.json")) throw new Phrase("진단 설명 파일이 없습니다.").As(message => new InvalidDataException(message));
            using var output = new MemoryStream();
            long total = 0;
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                foreach (var file in files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    if (!Names.Contains(file.Key)) throw new Phrase("허용하지 않는 진단 파일입니다.").As(message => new InvalidDataException(message));
                    var bytes = Encoding.UTF8.GetBytes(file.Value);
                    total += bytes.Length;
                    if (total > MaximumExpandedBytes) throw new Phrase("진단 자료가 전송 크기 제한을 넘었습니다.").As(message => new InvalidDataException(message));
                    using var stream = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open();
                    stream.Write(bytes, 0, bytes.Length);
                }
            }
            if (output.Length > MaximumArchiveBytes) throw new Phrase("압축한 진단 자료가 전송 크기 제한을 넘었습니다.").As(message => new InvalidDataException(message));
            return output.ToArray();
        }

        private static readonly UTF8Encoding Strict = new UTF8Encoding(false, true);

        public static Dictionary<string, string> Read(Stream input) =>
            ReadEntries(input).ToDictionary(pair => pair.Key, pair => Strict.GetString(pair.Value), StringComparer.Ordinal);

        /// <summary>
        /// 압축을 풀되 글자로 바꾸지 않는다. 서버는 업로드를 받는 동안 이것을 쥐므로, 풀린 크기의 두세 배가 되는
        /// UTF-16 문자열을 만들지 않는다. 모두 UTF-8 인지는 <see cref="Read"/>와 같이 검사한다.
        /// </summary>
        public static Dictionary<string, byte[]> ReadEntries(Stream input)
        {
            if (!input.CanSeek || input.Length > MaximumArchiveBytes) throw new InvalidDataException("진단 압축 파일 크기가 올바르지 않습니다.");
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (archive.Entries.Count == 0 || archive.Entries.Count > Names.Count) throw new InvalidDataException("진단 파일 수가 올바르지 않습니다.");
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            long total = 0;
            foreach (var entry in archive.Entries)
            {
                if (!Names.Contains(entry.FullName) || files.ContainsKey(entry.FullName))
                    throw new InvalidDataException("중복되거나 허용하지 않는 진단 파일입니다.");
                var content = Expand(entry, MaximumExpandedBytes - total);
                total += content.Length;
                Strict.GetCharCount(content);
                files.Add(entry.FullName, content);
            }
            if (!files.ContainsKey("report.json")) throw new InvalidDataException("진단 설명 파일이 없습니다.");
            return files;
        }

        /// <summary>
        /// 설명 파일 하나만 꺼낸다. 목록을 그릴 때 자료 전체를 메모리에 펴지 않으려는 것이다.
        /// 검사는 <see cref="Read"/>와 같다 - 허용한 이름과 크기 제한을 그대로 건다.
        /// </summary>
        public static byte[] ReadReport(Stream input)
        {
            if (!input.CanSeek || input.Length > MaximumArchiveBytes) throw new InvalidDataException("진단 압축 파일 크기가 올바르지 않습니다.");
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, true);
            var entry = archive.GetEntry("report.json") ?? throw new InvalidDataException("진단 설명 파일이 없습니다.");
            var content = Expand(entry, MaximumExpandedBytes);
            Strict.GetCharCount(content);
            return content;
        }

        // 머리글의 길이는 믿지 않는다. 풀면서 센 바이트로 한도를 지킨다.
        private static byte[] Expand(ZipArchiveEntry entry, long remaining)
        {
            if (entry.Length > remaining) throw new InvalidDataException("진단 내용이 크기 제한을 넘었습니다.");
            using var source = entry.Open();
            using var content = new MemoryStream((int)entry.Length);
            var buffer = new byte[8192];
            long total = 0;
            int read;
            while ((read = source.Read(buffer, 0, buffer.Length)) != 0)
            {
                total += read;
                if (total > remaining) throw new InvalidDataException("진단 내용이 크기 제한을 넘었습니다.");
                content.Write(buffer, 0, read);
            }
            // 머리글 길이가 맞으면 버퍼를 그대로 넘겨 풀린 크기만큼을 한 번 더 복사하지 않는다.
            return content.Length == content.Capacity ? content.GetBuffer() : content.ToArray();
        }

        public static string Hash(byte[] content)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(content)).Replace("-", "").ToLowerInvariant();
        }
    }
}

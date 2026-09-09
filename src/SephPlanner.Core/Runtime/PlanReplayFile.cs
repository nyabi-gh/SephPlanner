using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SephPlanner.Core.Runtime
{
    public static class PlanReplayFile
    {
        private const string Header = "SEPHPLANNER-REPLAY-1";

        public static void Write(string path, string json) =>
            CatalogBundleStore.WriteAtomic(path, Header + "\n" + Hash(json) + "\n" + json);

        public static string Read(string path)
        {
            using var reader = new StreamReader(path, Encoding.UTF8);
            if (reader.ReadLine() != Header) throw new InvalidDataException("재현 파일 형식이 올바르지 않습니다.");
            var expectedHash = reader.ReadLine();
            var json = reader.ReadToEnd();
            if (Hash(json) != expectedHash) throw new InvalidDataException("재현 파일의 SHA-256이 다릅니다. 파일이 손상되거나 변경되었습니다.");
            return json;
        }

        private static string Hash(string text)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }
}

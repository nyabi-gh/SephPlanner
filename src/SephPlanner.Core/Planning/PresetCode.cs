using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SephPlanner.Core.Planning
{
    /// <summary>
    /// 프리셋 코드에서 읽어낸 빌드. 게임이 저장하는 항목 중 추천에 쓸 수 있는 것만 담는다
    /// (코스튬·특성·차원 주머니는 배치나 선택지와 무관해 버린다).
    /// </summary>
    public sealed class BuildPreset
    {
        /// <summary>시작 무기 엔티티 번호. 무기 연동 아티팩트가 켜질지를 가른다.</summary>
        public int StartingWeaponId { get; set; }

        /// <summary>즐겨찾기로 표시된 아티팩트 엔티티 번호. 이 빌드가 노리는 것들이다.</summary>
        public List<int> FavoriteCharms { get; } = new List<int>();

        /// <summary>
        /// 과일 꼬치로 조절한 카테고리별 드롭 성향. 양수는 그 카테고리를 모으겠다는 뜻이고 음수는
        /// 피하겠다는 뜻이다. 키는 게임 <c>ItemCategoryEntity.id</c>로, 콤보 식별자와 같은 체계다.
        /// </summary>
        public Dictionary<string, int> CategoryBias { get; } =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public bool IsEmpty => FavoriteCharms.Count == 0 && CategoryBias.Count == 0;
    }

    /// <summary>
    /// 게임의 커스텀 로드아웃 공유 코드(<c>AAF_PRESET_OBFZ|v1…</c>)를 읽는다. 커뮤니티가 빌드
    /// 공략에 이 코드를 붙여 공유하므로, 추천이 빌드를 알게 하는 입력으로 쓴다.
    ///
    /// 게임 <c>UI_PresetPanel</c>의 역방향이다. 암호가 아니라 난독화라 게임 어셈블리 없이 그대로
    /// 재현할 수 있고, 우리는 읽기만 하므로 내보내기는 만들지 않는다(테스트용 <see cref="Encode"/> 제외).
    /// </summary>
    public static class PresetCode
    {
        public const string Prefix = "AAF_PRESET_OBFZ|v1";

        private const string Magic = "AAP1";
        private const string ObfuscationKey = "ActionAnimalFarmPresetShareKey";

        private static readonly string[] LineSeparators = { "\r\n", "\n" };

        public static bool TryParse(string? code, out BuildPreset preset, out string error)
        {
            preset = new BuildPreset();

            var trimmed = (code ?? "").Trim();
            if (trimmed.Length == 0)
            {
                error = "프리셋 코드를 붙여넣으세요.";
                return false;
            }
            if (!trimmed.StartsWith(Prefix, StringComparison.Ordinal))
            {
                error = "프리셋 코드가 아닙니다. 게임에서 복사한 코드를 그대로 붙여넣으세요.";
                return false;
            }

            var plain = Decode(trimmed.Substring(Prefix.Length).Trim());
            if (plain.Length == 0)
            {
                error = "프리셋 코드가 깨졌습니다. 코드 전체가 복사됐는지 확인하세요.";
                return false;
            }

            // 절반만 읽은 것을 돌려주면, 반환값을 지나친 쪽이 남의 빌드를 잘못 읽은 채로 돌아간다.
            var parsed = new BuildPreset();
            if (!TryParsePlain(plain, parsed, out error)) return false;

            preset = parsed;
            return true;
        }

        private static bool TryParsePlain(string plain, BuildPreset preset, out string error)
        {
            var lines = plain.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines[0].Trim() != Magic)
            {
                error = "프리셋 코드의 형식이 다릅니다. 게임 버전이 다를 수 있습니다.";
                return false;
            }

            // 같은 접두사가 두 번 나오면 뒤엣것을 쓴다. 게임의 사전 채우기와 같다.
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                var colon = line.IndexOf(':');
                if (colon > 0) fields[line.Substring(0, colon)] = line.Substring(colon + 1);
            }

            error = "프리셋 코드를 읽지 못했습니다. 게임 버전이 다를 수 있습니다.";

            // 게임이 반드시 써 넣는 줄들이다. 없으면 프리셋 코드가 아니거나 잘린 것이다.
            if (!fields.TryGetValue("W", out var weapon) || !int.TryParse(weapon, out var weaponId)) return false;
            if (!fields.TryGetValue("C", out var costume) || Unescape(costume).Trim().Length == 0) return false;
            if (!fields.ContainsKey("S")) return false;

            preset.StartingWeaponId = weaponId;

            if (fields.TryGetValue("F", out var favorites) && favorites.Trim().Length > 0)
            {
                foreach (var entry in favorites.Split(','))
                {
                    if (!int.TryParse(entry, out var entityId)) return false;
                    if (!preset.FavoriteCharms.Contains(entityId)) preset.FavoriteCharms.Add(entityId);
                }
            }

            if (fields.TryGetValue("R", out var fruits) && fruits.Trim().Length > 0)
            {
                foreach (var entry in fruits.Split(';'))
                {
                    var parts = entry.Split(',');
                    if (parts.Length != 2) return false;
                    if (!int.TryParse(parts[1], out var value)) return false;

                    var category = Unescape(parts[0]);
                    if (category.Trim().Length == 0) return false;

                    // 같은 카테고리 과일을 여러 개 꽂을 수 있고, 게임도 값을 합쳐서 보여 준다.
                    preset.CategoryBias.TryGetValue(category, out var sum);
                    preset.CategoryBias[category] = sum + value;
                }
            }

            error = "";
            return true;
        }

        /// <summary>
        /// 코드와 그 평문의 크기 상한. 게임이 만드는 프리셋은 수백 바이트이고 차원 주머니를 가득
        /// 채워도 몇 KB 라, 이보다 큰 것은 프리셋이 아니다. 클립보드에서 오는 남의 문자열이므로
        /// 상한 없이 풀면 작은 코드 하나가 GB 단위 할당을 일으킬 수 있다(gzip 폭탄).
        /// </summary>
        public const int MaxCodeLength = 64 * 1024;
        public const int MaxPlainBytes = 64 * 1024;

        /// <summary>Base64 → XOR → GZip. 어느 단계가 깨져도, 너무 커도 빈 문자열로 돌아온다.</summary>
        private static string Decode(string payload)
        {
            if (payload.Length == 0 || payload.Length > MaxCodeLength) return "";
            try
            {
                var bytes = Convert.FromBase64String(payload);
                Scramble(bytes);

                using var source = new MemoryStream(bytes);
                using var gzip = new GZipStream(source, CompressionMode.Decompress);
                var plain = new byte[MaxPlainBytes];
                var length = 0;
                while (length < plain.Length)
                {
                    var read = gzip.Read(plain, length, plain.Length - length);
                    if (read <= 0) break;
                    length += read;
                }

                // 상한까지 채웠는데 아직 남아 있으면 프리셋이 아니다. 잘라 쓰면 남의 빌드를
                // 반만 읽은 채로 돌아가므로 통째로 버린다.
                if (length == plain.Length && gzip.ReadByte() != -1) return "";

                return Encoding.UTF8.GetString(plain, 0, length);
            }
            catch (Exception)
            {
                return "";
            }
        }

        /// <summary>
        /// 테스트가 코드를 만들어 넣기 위한 역방향. 게임과 같은 알고리즘이라 왕복이 성립하는지로
        /// 디코딩을 검증할 수 있다. 현재는 테스트와 진단에서만 쓴다.
        /// </summary>
        public static string Encode(string plain)
        {
            using var compressed = new MemoryStream();
            using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                var raw = Encoding.UTF8.GetBytes(plain);
                gzip.Write(raw, 0, raw.Length);
            }

            var bytes = compressed.ToArray();
            Scramble(bytes);
            return Prefix + Convert.ToBase64String(bytes);
        }

        private static void Scramble(byte[] bytes)
        {
            var key = Encoding.UTF8.GetBytes(ObfuscationKey);
            for (var i = 0; i < bytes.Length; i++) bytes[i] ^= key[i % key.Length];
        }

        private static string Unescape(string value)
        {
            if (value.Length == 0) return "";
            try
            {
                return Uri.UnescapeDataString(value);
            }
            catch (Exception)
            {
                // 게임도 실패하면 원문을 그대로 쓴다. 여기서 막으면 게임은 받는 코드를 우리만 거른다.
                return value;
            }
        }
    }
}

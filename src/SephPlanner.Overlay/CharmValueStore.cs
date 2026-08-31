using System.IO;
using System.Reflection;
using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Overlay;

/// <summary>
/// 손으로 채운 아티팩트 가치를 읽어 둔다. 게임에서 나오는 데이터가 아니라 우리가 만드는
/// 데이터라서 실행 파일 안에 함께 들어 있다(<c>data/values/charms.json</c>).
/// </summary>
public static class CharmValueStore
{
    private const string ResourceName = "SephPlanner.Values.Charms.json";

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static CharmValueBook? _book;

    public static CharmValueBook Book => _book ??= Load();

    private static CharmValueBook Load()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream is null) return CharmValueBook.Empty;

            using var reader = new StreamReader(stream);
            var file = JsonSerializer.Deserialize<CharmValueFile>(reader.ReadToEnd(), Options);
            return new CharmValueBook(file);
        }
        catch (JsonException)
        {
            // 가치 데이터가 깨졌다고 오버레이가 뜨지 않으면 안 된다. 점수는 잰 값과 레어도로
            // 물러서고, 그 사실은 아래 IsLoaded 로 드러난다.
            return CharmValueBook.Empty;
        }
    }

    /// <summary>채워진 항목이 하나라도 있는지. 화면에 가치의 출처를 밝힐 때 쓴다.</summary>
    public static bool IsLoaded => Book.Count > 0;
}

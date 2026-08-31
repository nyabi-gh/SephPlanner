using System.IO;
using System.Text.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Planning;

namespace SephPlanner.Overlay;

/// <summary>
/// 오버레이를 껐다 켜도 유지할 사용자 선택. 빌드 우선 카테고리는 한 탐험 안에서 계속 쓰이고,
/// 강화 우선 아티팩트는 탐험이 바뀌어도 같은 아이템에 적용되도록 엔티티 번호로 기억한다.
/// </summary>
public sealed class UserSettings
{
    public List<string> PriorityCategories { get; set; } = new();
    public List<int> PinnedCharms { get; set; } = new();

    /// <summary>마지막으로 끌어다 둔 창 위치. 없으면 기본 위치를 쓴다.</summary>
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }

    /// <summary>오버레이 전체 투명도. 1이 불투명.</summary>
    public double Opacity { get; set; } = 1.0;

    /// <summary>격자와 후보 목록에 아이콘을 보여줄지. 아이콘이 아직 없으면 글자로 물러선다.</summary>
    public bool IconMode { get; set; } = true;

    /// <summary>
    /// 무엇을 집을지에 대한 조언(후보 목록·빌드 우선)을 켤지. 배치(정렬)는 손으로도 할 수 있는
    /// 일의 대행이지만 추천은 판단을 빌려주는 것이라, 도전을 지키고 싶은 사람은 끌 수 있어야 한다.
    /// </summary>
    public bool Recommendations { get; set; } = true;

    /// <summary>
    /// 가져온 빌드 프리셋 코드 원문. 해석 결과가 아니라 원문을 저장한다 - 카탈로그나 게임이
    /// 바뀌어도 다시 읽으면 되고, 사용자가 어떤 코드를 넣었는지 그대로 남는다.
    /// </summary>
    public string? PresetCode { get; set; }

    private string? _decodedFrom;
    private BuildPreset? _decoded;

    /// <summary>해석에 실패했거나 코드가 없으면 null. 같은 코드를 두 번 풀지 않는다.</summary>
    public BuildPreset? Preset()
    {
        if (string.IsNullOrWhiteSpace(PresetCode)) return null;
        if (_decodedFrom != PresetCode)
        {
            _decodedFrom = PresetCode;
            _decoded = SephPlanner.Core.Planning.PresetCode.TryParse(PresetCode, out var preset, out _)
                ? preset
                : null;
        }
        return _decoded;
    }

    private static string FilePath =>
        Path.Combine(IpcContract.DataDirectory, "overlay-settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath));
                if (loaded is not null) return loaded;
            }
        }
        catch (Exception)
        {
            // 설정이 깨졌거나 읽을 수 없으면 기본값으로 시작한다. Load 는 창 생성 시점에 불리므로
            // 권한 문제(UnauthorizedAccessException) 같은 예외 하나로 앱이 못 뜨면 안 된다.
        }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(IpcContract.DataDirectory);

            // 바로 덮어쓰면 쓰는 도중 크래시에 잘린 JSON 이 남아 설정 전체가 날아간다.
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(this));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception)
        {
            // 저장 실패로 오버레이가 죽으면 안 된다. 다음 조작 때 다시 시도된다.
        }
    }

    public PlanPreferences ToPreferences() => new()
    {
        PriorityCategories = new HashSet<string>(PriorityCategories),
        PinnedCharms = new HashSet<int>(PinnedCharms),
        Recommendations = Recommendations,

        // 프리셋이 알려 주는 것은 무엇을 집을지에 대한 조언이라, 추천을 끄면 함께 쉰다.
        PresetCharms = Recommendations && Preset() is { } preset
            ? new HashSet<int>(preset.FavoriteCharms)
            : new HashSet<int>(),
    };
}

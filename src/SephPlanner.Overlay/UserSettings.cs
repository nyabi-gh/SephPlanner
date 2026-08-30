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
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 설정이 깨졌으면 기본값으로 시작한다. 다음 저장 때 새로 쓰인다.
        }
        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(IpcContract.DataDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (IOException)
        {
        }
    }

    public PlanPreferences ToPreferences() => new()
    {
        PriorityCategories = new HashSet<string>(PriorityCategories),
        PinnedCharms = new HashSet<int>(PinnedCharms),
    };
}

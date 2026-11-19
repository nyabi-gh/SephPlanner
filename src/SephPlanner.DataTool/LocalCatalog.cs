using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 게임이 덤프한 카탈로그로 플러그인과 같은 <see cref="Catalog"/> 를 짓는다. 아티팩트의 값어치 표는 덤프에 이미 실려
/// 있고, 점수 눈금은 함께 덤프한 측정에서 다시 잰다(플러그인 <c>CatalogSource</c> 와 같다). 눈금을 빼면 1.0.31 에
/// 잰 기본값으로 풀려, 도구가 통과시킨 것이 실제 플러그인에서 재현되지 않는다.
/// </summary>
internal static class LocalCatalog
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static Catalog? Load()
    {
        var tablets = Read<List<TabletDefinition>>(PlannerData.TabletDbFile);
        var charms = Read<List<CharmDefinition>>(PlannerData.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine($"카탈로그가 없습니다. 게임을 한 번 실행해 {PlannerData.DataDirectory} 를 채우세요.");
            return null;
        }

        // 콤보를 빼고 만들면 플러그인과 다른 점수가 나온다. 콤보 파일은 없을 수 있어 선택이다.
        var combos = Read<List<ComboDefinition>>(PlannerData.ComboDbFile);
        var measurement = Read<StatMeasurement>(PlannerData.StatMeasurementFile);
        if (measurement is null)
            Console.WriteLine("측정 덤프가 없어 점수 눈금을 기본값으로 둡니다. 플러그인과 답이 다를 수 있습니다(게임에서 F9).");
        return new Catalog(tablets, charms, combos, measurement is null ? null : WorthScale.Measure(measurement, charms));
    }

    private static T? Read<T>(string fileName)
    {
        var path = PlannerData.ActiveDataFile(fileName);
        return path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            : default;
    }
}

using System.Text.Json;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 플러그인이 떠 온 능력치 표로 콤보 한 단계의 값어치를 재고, <c>Worth</c>의 상수와 견준다.
/// 게임을 켜지 않고 돌아가며, 덤프가 없으면 무엇을 해야 하는지 알린다.
/// </summary>
public static class ComboMeasure
{
    public static int Run()
    {
        var path = Path.Combine(PlannerData.DataDirectory, PlannerData.StatMeasurementFile);
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"측정 자료가 없습니다: {path}");
            Console.Error.WriteLine("게임을 한 번 켜서 카탈로그를 다시 덤프하세요(F9).");
            return 1;
        }

        var measurement = JsonSerializer.Deserialize<StatMeasurement>(File.ReadAllText(path));
        if (measurement is null)
        {
            Console.Error.WriteLine("측정 자료를 읽지 못했습니다.");
            return 1;
        }

        var report = ComboWorthMeasure.Run(measurement);

        Console.WriteLine($"아티팩트 능력치 표 {measurement.CharmStats.Count}건, "
                          + $"콤보 능력치 {measurement.ComboStats.Count}건");
        Console.WriteLine();

        Console.WriteLine("능력치별 레벨 하나당 증가분 (중앙값)");
        foreach (var pair in report.PerLevel.OrderByDescending(p => p.Value))
            Console.WriteLine($"  {pair.Key,-28} {pair.Value,8:0.##}");
        Console.WriteLine();

        Console.WriteLine("콤보 임계값별 값어치 (레벨 단위)");
        foreach (var entry in report.Thresholds)
        {
            var unconverted = entry.Unconverted.Count > 0
                ? "  못 옮김: " + string.Join(", ", entry.Unconverted)
                : "";
            Console.WriteLine($"  {entry.CategoryId,-14} {entry.Threshold,3}개 {entry.Levels,8:0.##}{unconverted}");
        }
        Console.WriteLine();

        Console.WriteLine($"환산된 임계값 {report.ConvertedCount}건, 못 한 것 {report.UnconvertedCount}건");
        Console.WriteLine($"중앙값 {report.MedianLevels:0.##} 레벨  (지금 Worth.ComboThreshold = {Worth.ComboThreshold})");

        if (report.ConvertedCount == 0)
        {
            Console.Error.WriteLine("환산된 임계값이 없습니다. 콤보가 주는 능력치를 아티팩트가 하나도 주지 않는 경우입니다.");
            return 1;
        }
        return 0;
    }
}

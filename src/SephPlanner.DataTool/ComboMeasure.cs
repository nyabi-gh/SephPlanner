using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 플러그인이 떠 온 능력치 표로 환산율과 콤보 한 단계의 값어치를 재고, <c>Worth</c>의 상수와
/// 견준다. 게임을 켜지 않고 돌아가며, 덤프가 없으면 무엇을 해야 하는지 알린다.
/// </summary>
public static class ComboMeasure
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    private static readonly double[] Weights = { 0.1, 0.3, 0.5, 1, 1.5, 2, 3, 5, 10, 30, 100 };

    public static int Run()
    {
        var path = PlannerData.ActiveDataFile(PlannerData.StatMeasurementFile);
        if (path is null || !File.Exists(path))
        {
            Console.Error.WriteLine($"측정 자료가 없습니다: {path}");
            Console.Error.WriteLine("게임을 한 번 켜서 카탈로그를 다시 덤프하세요(F9).");
            return 1;
        }

        var measurement = JsonSerializer.Deserialize<StatMeasurement>(File.ReadAllText(path), Read);
        if (measurement is null)
        {
            Console.Error.WriteLine("측정 자료를 읽지 못했습니다.");
            return 1;
        }

        // 환산율의 눈금은 "능력치 표가 값어치 전부인 아티팩트"에서 나오므로 아티팩트 정의가 있어야 한다.
        var charmPath = PlannerData.ActiveDataFile(PlannerData.CharmDbFile);
        if (charmPath is null || !File.Exists(charmPath))
        {
            Console.Error.WriteLine($"아티팩트 덤프가 없습니다: {charmPath}");
            Console.Error.WriteLine("게임을 한 번 켜서 카탈로그를 다시 덤프하세요(F9).");
            return 1;
        }
        var charms = JsonSerializer.Deserialize<List<CharmDefinition>>(File.ReadAllText(charmPath), Read);
        if (charms is null)
        {
            Console.Error.WriteLine("아티팩트 덤프를 읽지 못했습니다.");
            return 1;
        }
        var profiles = charms.Select(CharmStatWorth.Profile).ToList();

        var report = ComboWorthMeasure.Run(measurement, profiles);
        var exchange = report.Exchange;

        Console.WriteLine($"아티팩트 능력치 표 {measurement.CharmStats.Count}건, "
                          + $"콤보 능력치 {measurement.ComboStats.Count}건");
        Console.WriteLine();

        Console.WriteLine($"눈금을 정한 표본 {exchange.ScaleSamples}종(능력치 표가 값어치 전부인 아티팩트), "
                          + $"그중 따로 보정한 능력치 {exchange.FittedStats}개");
        Console.WriteLine($"능력치 하나만 떼어 세면 레벨당 값어치가 {exchange.Scale:0.###} 배로 부푼다 "
                          + "(정의상 1 이어야 하는 수다)");
        Console.WriteLine();

        Console.WriteLine("능력치별 레벨 하나당 증가분 - 떼어 센 값 -> 나눠 준 값");
        foreach (var pair in exchange.PerLevel.OrderByDescending(p => p.Value))
        {
            var seed = exchange.SeedPerLevel.TryGetValue(pair.Key, out var value) ? value : 0;
            var samples = exchange.Samples.TryGetValue(pair.Key, out var count) ? count : 0;
            Console.WriteLine($"  {pair.Key,-28} {seed,8:0.##} -> {pair.Value,8:0.##}   표본 {samples,2}"
                              + (exchange.IsReliable(pair.Key) ? "" : "  <- 표본이 적다"));
        }
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
        Console.WriteLine();

        // 눈금은 카탈로그를 지을 때 재어 실린다. 여기서는 그 값과, 잴 수 없을 때 쓰는 기본값을
        // 나란히 보여 준다 - 둘이 크게 벌어지면 기본값이 낡았다는 뜻이다.
        var scale = WorthScale.Measure(measurement, charms);
        Console.WriteLine("점수 눈금 - 이 카탈로그에서 잰 값 / 잴 수 없을 때 쓰는 기본값");
        Console.WriteLine($"  콤보 한 단계          {scale.ComboThreshold,8:0.####} / {WorthScale.Default.ComboThreshold:0.####}");
        Console.WriteLine($"  콤보 한 걸음          {scale.ComboProgress,8:0.####} / {WorthScale.Default.ComboProgress:0.####}");
        Console.WriteLine($"  전체 피해 보너스 1점  {scale.DamageBonus,8:0.####} / {WorthScale.Default.DamageBonus:0.####}");
        Console.WriteLine($"  점수 비교 눈금        {scale.ScoreStep,8:0.####} / {WorthScale.Default.ScoreStep:0.####}");
        Console.WriteLine();

        Shrinkage(measurement, profiles);

        if (report.ConvertedCount == 0)
        {
            Console.Error.WriteLine("환산된 임계값이 없습니다. 콤보가 주는 능력치를 아티팩트가 하나도 주지 않는 경우입니다.");
            return 1;
        }
        return 0;
    }

    /// <summary><see cref="StatExchange.ShrinkageWeight"/>가 나온 자리.</summary>
    private static void Shrinkage(StatMeasurement measurement, List<CharmStatProfile> profiles)
    {
        var trials = StatExchange.CrossValidate(measurement.CharmStats, profiles, Weights);
        if (trials.Count == 0) return;

        var best = trials.MinBy(trial => trial.CrossValidated)!;
        var threshold = best.CrossValidated + best.StandardError;

        Console.WriteLine("수축 세기별 오차 - 겹을 빼고 맞춘 뒤 그 겹에서 잰다");
        Console.WriteLine("  세기      표본 밖   표준오차    표본 안");
        foreach (var trial in trials)
            Console.WriteLine($"  {trial.Weight,6:0.###}   {trial.CrossValidated,8:0.000}   "
                              + $"{trial.StandardError,8:0.000}   {trial.InSample,8:0.000}"
                              + (trial.Weight == StatExchange.ShrinkageWeight ? "  <- 지금 쓰는 값" : ""));
        Console.WriteLine($"  표본 밖이 가장 낮은 세기 {best.Weight:0.###}, "
                          + $"표준오차 한 칸 규칙이 고르는 세기 "
                          + $"{trials.Where(t => t.CrossValidated <= threshold).Max(t => t.Weight):0.###}");
        Console.WriteLine();
    }
}

using System.Text.Json;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

public static class PlanReproduce
{
    private static readonly JsonSerializerOptions Read = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string path, bool allowModelChange = false)
    {
        try
        {
            var replay = JsonSerializer.Deserialize<PlanReplay>(PlanReplayFile.Read(path)) ??
                throw new InvalidDataException("재현 입력이 비었습니다.");
            Console.WriteLine($"저장: {replay.CapturedUtc}, {replay.Producer}");
            Console.WriteLine($"게시 요청 {replay.PublishedGeneration}, 마지막 요청 {replay.RequestedGeneration}");
            if (replay.RequestedGeneration != replay.PublishedGeneration || replay.LatestError.Length > 0)
                Console.WriteLine("마지막으로 게시된 계획을 재생합니다. F10 시점의 최신 상태와 다를 수 있습니다. " + replay.LatestError);
            if (replay.CoreBuild != PlanReplay.CurrentCoreBuild)
                Console.WriteLine($"계산 코드 차이: 저장={replay.CoreBuild}, 현재={PlanReplay.CurrentCoreBuild}");
            if (allowModelChange) Remeasure(replay);
            if (!replay.AdviceComplete)
                Console.WriteLine("조언이 아직 붙지 않은 계획을 잡은 자료입니다. 배치만 견줍니다.");
            var plan = replay.Rebuild(allowModelChange);
            var differences = replay.Expected!.Differences(ReplayResult.From(plan), replay.AdviceComplete);
            Console.WriteLine($"점수 {plan.Current.Score:0.########} → {plan.Best.Score:0.########}");
            foreach (var difference in differences) Console.WriteLine(difference);
            Console.WriteLine(differences.Count == 0
                ? "저장된 배치·점수·추천 요약과 일치합니다. 실제 게임 효과의 정확성 검증과는 별개입니다."
                : $"저장된 결과와 {differences.Count}곳이 다릅니다.");
            return differences.Count == 0 ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is JsonException ||
                                   ex is ArgumentException || ex is InvalidOperationException || ex is NullReferenceException)
        {
            Console.Error.WriteLine("계획 재현 실패: " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// 재현 자료는 제보자 기계에서 이미 계산된 값어치 표를 싣고 온다. 그래서 환산율을 고쳐도
    /// 재현은 옛 표로 풀리고 변경이 보이지 않는다. 여기 덤프가 있으면 다시 재어 얹는다.
    /// </summary>
    private static void Remeasure(PlanReplay replay)
    {
        var charms = replay.Catalog?.Charms;
        if (charms is null || charms.Count == 0) return;

        var path = PlannerData.ActiveDataFile(PlannerData.StatMeasurementFile);
        if (path is null || !File.Exists(path))
        {
            Console.WriteLine("값어치를 다시 잴 덤프가 없어 재현 자료의 표를 그대로 씁니다.");
            return;
        }

        var measurement = JsonSerializer.Deserialize<StatMeasurement>(File.ReadAllText(path), Read);
        if (measurement is null || measurement.CharmStats.Count == 0) return;

        var before = charms.Count(charm => charm.StatWorthByLevel.Count > 0);
        CharmStatWorth.Apply(charms, measurement);
        var after = charms.Count(charm => charm.StatWorthByLevel.Count > 0);
        Console.WriteLine($"값어치를 지금 덤프로 다시 쟀습니다: 능력치 표가 있는 아티팩트 {before}종 → {after}종");
    }
}

using System.Diagnostics;
using System.Text.Json;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 재현 자료 한 판의 풀이 비용을 잰다. cold 는 새 <see cref="LayoutCache"/>, warm 은 같은 캐시와 직전
/// 계획을 넘긴 것이라 플러그인이 계획 사이에 하는 일과 같다. 값은 이 기계의 .NET 값이며 게임 안의
/// Mono 값이 아니다 - 고치기 전후를 같은 기계에서 견주는 데만 쓴다.
/// </summary>
public static class SolverBench
{
    private const int WarmRounds = 3;

    public static int Run(string path, string? offers)
    {
        try
        {
            var replay = JsonSerializer.Deserialize<PlanReplay>(PlanReplayFile.Read(path)) ??
                throw new InvalidDataException("재현 입력이 비었습니다.");
            var snapshot = replay.Snapshot ?? throw new InvalidDataException("재현 자료에 스냅샷이 없습니다.");
            var inventory = snapshot.Inventory ?? throw new InvalidDataException("재현 자료에 가방이 없습니다.");
            var preferences = (replay.Preferences ?? throw new InvalidDataException("재현 자료에 설정이 없습니다.")).Restore();
            var catalog = (replay.Catalog ?? throw new InvalidDataException("재현 자료에 카탈로그가 없습니다.")).Restore();
            if (offers is not null) snapshot.Offers = ParseOffers(offers);

            Console.WriteLine($"{inventory.Width}x{inventory.Height} 열린 칸 {inventory.Storage}, 석판 {inventory.Tablets.Count}, " +
                              $"아이템 {inventory.Items.Count}, 후보 {snapshot.Offers.Count}, 콤보 우선 {preferences.PriorityCategories.Count}");

            var layouts = new LayoutCache();
            var previous = new Plan { Targets = replay.PreviousTargets ?? new List<PlanTarget>() };
            for (var round = 0; round <= WarmRounds; round++)
            {
                var collections = GC.CollectionCount(0);
                var (placement, placementMs, placementBytes) = Measure(() =>
                    PlanBuilder.BuildPlacement(snapshot, catalog, preferences, out _, previous, layouts));
                if (placement is null) throw new InvalidDataException("배치를 풀지 못했습니다.");
                var (_, adviceMs, adviceBytes) = Measure(() =>
                    PlanBuilder.BuildAdvice(placement, snapshot, catalog, preferences, layouts));
                Console.WriteLine($"{(round == 0 ? "cold " : $"warm{round}")}  배치 {placementMs,6:0} ms {placementBytes / 1048576,5} MB" +
                                  $"  조언 {adviceMs,6:0} ms {adviceBytes / 1048576,5} MB" +
                                  $"  0세대 {GC.CollectionCount(0) - collections,3}  탐색 누계 {layouts.Searches}");
                previous = placement;
            }
            return 0;
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is JsonException ||
                                   ex is UnauthorizedAccessException || ex is FormatException)
        {
            Console.Error.WriteLine("측정 실패: " + ex.Message);
            return 1;
        }
    }

    private static (T Result, double Milliseconds, long Bytes) Measure<T>(Func<T> work)
    {
        var allocated = GC.GetTotalAllocatedBytes(true);
        var watch = Stopwatch.StartNew();
        var result = work();
        watch.Stop();
        return (result, watch.Elapsed.TotalMilliseconds, GC.GetTotalAllocatedBytes(true) - allocated);
    }

    /// <summary>F10 순간에 세피라이트 창이 닫혀 있던 판에도 후보 조언 비용을 잴 수 있게 후보를 얹는다.</summary>
    private static List<OfferedItem> ParseOffers(string text) =>
        text.Split(',').Select((entry, slot) =>
        {
            var parts = entry.Split(':');
            if (parts.Length != 2 || parts[0] is not ("charm" or "tablet") || !int.TryParse(parts[1], out var id))
                throw new FormatException($"후보 형식은 charm:번호 또는 tablet:번호 입니다: {entry}");
            return new OfferedItem { Kind = parts[0], DefinitionId = id, SlotIndex = slot };
        }).ToList();
}

using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.DataTool;

/// <summary>
/// 저장된 스냅샷을 순서대로 다시 풀어 목표 배치가 바뀌는 순간과 풀이 시간을 표시한다.
/// </summary>
public static class SnapshotReplay
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>플러그인의 PlanRunner 처럼 직전 계획을 앵커로 잇는다. 다르게 하면 진단이 실제와 어긋난다.</summary>
    private static Plan? _previous;

    /// <summary>녹화해 둔 스냅샷들을 순서대로 다시 풀어 본다. 솔버를 고친 뒤 같은 세션으로 검증하는 용도다.</summary>
    public static int Run(string dir)
    {
        var catalog = LoadCatalog();
        if (catalog is null) return 1;

        _previous = null;
        Dictionary<int, (GridPos To, int Rotation)>? lastTargets = null;
        var watch = new System.Diagnostics.Stopwatch();
        var total = 0L;
        var count = 0;

        foreach (var path in Directory.GetFiles(dir, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            count++;
            watch.Restart();
            lastTargets = Describe(count, File.ReadAllText(path), catalog, lastTargets);
            watch.Stop();
            total += watch.ElapsedMilliseconds;
        }

        Console.WriteLine($"스냅샷 {count}장, 풀이 평균 {(count > 0 ? total / count : 0)}ms, 합계 {total}ms");
        return 0;
    }

    private static Catalog? LoadCatalog()
    {
        var tablets = Load<List<TabletDefinition>>(PlannerData.TabletDbFile);
        var charms = Load<List<CharmDefinition>>(PlannerData.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine($"카탈로그가 없습니다. 게임을 한 번 실행해 {PlannerData.DataDirectory} 를 채우세요.");
            return null;
        }
        return new Catalog(tablets, charms, Load<List<ComboDefinition>>(PlannerData.ComboDbFile));
    }

    private static Dictionary<int, (GridPos To, int Rotation)>? Describe(
        int index, string json, Catalog catalog, Dictionary<int, (GridPos To, int Rotation)>? lastTargets)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss.fff");
        GameSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<GameSnapshot>(json, Options);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"{index:00000} {stamp} 해석 실패: {ex.Message}");
            return null;
        }

        if (snapshot?.Inventory is null)
        {
            Console.WriteLine($"{index:00000} {stamp} 인벤토리 없음");
            return null;
        }

        var plan = PlanBuilder.Build(snapshot, catalog, null, _previous);
        _previous = plan;
        if (plan is null)
        {
            Console.WriteLine($"{index:00000} {stamp} 계획 없음");
            return null;
        }

        var targets = plan.Targets.ToDictionary(t => t.InstanceId, t => (t.To, t.Rotation));
        var changed = new List<string>();
        if (lastTargets is not null)
        {
            foreach (var pair in lastTargets)
            {
                if (targets.TryGetValue(pair.Key, out var now) && now != pair.Value)
                    changed.Add($"{pair.Key}: {pair.Value.To}r{pair.Value.Rotation} -> {now.To}r{now.Rotation}");
            }
        }

        // 자리가 바뀌는 항목 수. 이동 목록은 가방이 꽉 차면 비므로 그것만으로는 안 보인다.
        var relocated = plan.Targets.Count(t =>
            t.From != t.To || t.IsTablet && t.FromRotation != t.Rotation);

        Console.WriteLine(
            $"{index:00000} {stamp} 점수 {plan.Current.Score:0.##} -> {plan.Best.Score:0.##} " +
            $"옮길 자리 {relocated}건 이동 {plan.Moves.Count}건 어긋난칸 {plan.LevelMismatches}" +
            (changed.Count > 0 ? $"  *** 목표 변경 {changed.Count}건: {string.Join(", ", changed)}" : ""));

        return targets;
    }

    private static T? Load<T>(string fileName)
    {
        var path = PlannerData.ActiveDataFile(fileName);
        return path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            : default;
    }
}

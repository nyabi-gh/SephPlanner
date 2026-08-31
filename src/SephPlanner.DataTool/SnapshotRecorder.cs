using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.DataTool;

/// <summary>
/// 게임이 흘려보내는 스냅샷을 전부 받아 저장하면서, 스냅샷마다 계획을 다시 풀어 목표 배치가
/// 바뀌는 순간을 표시한다. "제안을 따라가는데 제안이 바뀐다"처럼 간헐적으로만 나오는 문제는
/// 게임 안에서 재현을 기다리는 대신 이 기록을 재생해서 잡는다.
///
/// 오버레이와 같은 파이프를 쓰므로 함께 켤 수 없다. 읽기만 하며 게임에는 아무것도 보내지 않는다.
/// </summary>
public static class SnapshotRecorder
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    /// <summary>플러그인의 PlanRunner 처럼 직전 계획을 앵커로 잇는다. 다르게 하면 진단이 실제와 어긋난다.</summary>
    private static Plan? _previous;

    /// <summary>녹화해 둔 스냅샷들을 순서대로 다시 풀어 본다. 솔버를 고친 뒤 같은 세션으로 검증하는 용도다.</summary>
    public static int Replay(string dir)
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
        var tablets = Load<List<TabletDefinition>>(IpcContract.TabletDbFile);
        var charms = Load<List<CharmDefinition>>(IpcContract.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine($"카탈로그가 없습니다. 게임을 한 번 실행해 {IpcContract.DataDirectory} 를 채우세요.");
            return null;
        }
        return new Catalog(tablets, charms, Load<List<ComboDefinition>>(IpcContract.ComboDbFile));
    }

    public static int Run(string outDir)
    {
        var catalog = LoadCatalog();
        if (catalog is null) return 1;

        Directory.CreateDirectory(outDir);
        Console.WriteLine($"저장 위치: {outDir}");

        var index = 0;
        Dictionary<int, (GridPos To, int Rotation)>? lastTargets = null;

        while (true)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", IpcContract.PipeName, PipeDirection.In);
                pipe.Connect(2000);
                Console.WriteLine("게임에 연결됐습니다. 스냅샷을 기다립니다...");

                using var reader = new StreamReader(pipe, Encoding.UTF8);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    index++;
                    File.WriteAllText(Path.Combine(outDir, $"{index:00000}.json"), line);
                    lastTargets = Describe(index, line, catalog, lastTargets);
                }
                Console.WriteLine("연결이 끊겼습니다. 다시 기다립니다...");
            }
            catch (TimeoutException)
            {
                // 게임이 아직 안 떠 있거나 다른 클라이언트(오버레이)가 붙어 있다. 조용히 재시도한다.
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                Console.WriteLine("오류: " + ex.Message);
                Thread.Sleep(1000);
            }
        }
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

        Console.WriteLine(
            $"{index:00000} {stamp} 점수 {plan.Current.Score:0.##} -> {plan.Best.Score:0.##} " +
            $"이동 {plan.Moves.Count}건 어긋난칸 {plan.LevelMismatches}" +
            (changed.Count > 0 ? $"  *** 목표 변경 {changed.Count}건: {string.Join(", ", changed)}" : ""));

        return targets;
    }

    private static T? Load<T>(string fileName)
    {
        var path = Path.Combine(IpcContract.DataDirectory, fileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
    }
}

using System.Text.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.DataTool;

/// <summary>
/// 플러그인이 덤프해 둔 데이터로 솔버를 게임 없이 돌려본다. 결과의 좋고 나쁨이 아니라
/// 파이프라인이 끝까지 도는지, 점수가 말이 되는지를 본다.
/// </summary>
public static class SolverSmokeTest
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static int Run(int tabletCount, int charmCount)
    {
        var tablets = Load<List<TabletDefinition>>(IpcContract.TabletDbFile);
        var charms = Load<List<CharmDefinition>>(IpcContract.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine(
                $"덤프 데이터가 없습니다. 게임을 한 번 실행해 {IpcContract.DataDirectory} 를 채우세요.");
            return 1;
        }

        var problem = new PlacementProblem
        {
            Grid = GridSpec.WithStorage(24),
            Tablets = tablets.Where(t => !t.IsCustom && t.Query.Length > 0)
                             .Take(tabletCount)
                             .Select((t, i) => new TabletSlot { Definition = t, InstanceId = 1000 + i })
                             .ToList(),
            Charms = charms.Take(charmCount)
                           .Select((c, i) => new CharmSlot { Definition = c, InstanceId = i })
                           .ToList(),
        };

        Console.WriteLine($"격자 {problem.Grid.Width}x{problem.Grid.Height}, 열린 칸 {problem.Grid.Storage}");
        Console.WriteLine($"석판 {problem.Tablets.Count}개, 아티팩트 {problem.Charms.Count}개");
        foreach (var slot in problem.Tablets)
            Console.WriteLine($"  석판 {slot.Definition.Id}: {slot.Definition.Query.Replace("\n", " / ")}");

        var started = System.Diagnostics.Stopwatch.StartNew();
        var arrangement = PlacementSolver.Solve(problem);
        started.Stop();

        Console.WriteLine($"\n점수 {arrangement.Score:0.##}  ({started.ElapsedMilliseconds}ms)");
        foreach (var placement in arrangement.Tablets)
            Console.WriteLine($"  석판 {placement.Definition.Id} -> {placement.Position} 회전 {placement.Rotation}");

        Console.WriteLine($"  배치된 아티팩트 {arrangement.CharmPositions.Count}개, " +
                          $"효과 꺼진 아티팩트 {arrangement.InactiveCharms.Count}개");
        PrintGrid(problem, arrangement);
        RankOffers(problem, arrangement.Score, tablets, charms);
        return 0;
    }

    private static void RankOffers(
        PlacementProblem problem, double baseScore,
        List<TabletDefinition> tablets, List<CharmDefinition> charms)
    {
        var candidates = new List<OfferCandidate>();
        foreach (var tablet in tablets.Where(t => !t.IsCustom && t.Query.Length > 0).Skip(3).Take(3))
            candidates.Add(new OfferCandidate { DefinitionId = tablet.EntityId, Kind = "tablet", Name = tablet.Id, Tablet = tablet });
        foreach (var charm in charms.Skip(12).Take(3))
            candidates.Add(new OfferCandidate { DefinitionId = charm.EntityId, Kind = "charm", Name = charm.Id, Charm = charm });

        var started = System.Diagnostics.Stopwatch.StartNew();
        var advice = OfferAdvisor.Rank(problem, baseScore, candidates);
        started.Stop();

        Console.WriteLine();
        Console.WriteLine($"선택지 {candidates.Count}개 평가 ({started.ElapsedMilliseconds}ms)");
        foreach (var entry in advice)
            Console.WriteLine($"  {entry.Candidate.Kind,-7} {entry.Candidate.Name,-24} {entry.Gain,6:+0.#;-0.#;0}");
    }

    private static void PrintGrid(PlacementProblem problem, Arrangement arrangement)
    {
        var tabletCells = arrangement.Tablets.ToDictionary(t => t.Position, t => t);
        Console.WriteLine();
        for (var y = 0; y < problem.Grid.Height; y++)
        {
            var row = new List<string>();
            for (var x = 0; x < problem.Grid.Width; x++)
            {
                var cell = new GridPos(x, y);
                if (problem.Grid.ToIndex(x, y) >= problem.Grid.Storage) row.Add("    ");
                else if (tabletCells.ContainsKey(cell)) row.Add("  T ");
                else if (arrangement.Levels.TryGetValue(cell, out var level)) row.Add($"{level,3} ");
                else row.Add("  . ");
            }
            Console.WriteLine(string.Join("", row));
        }
    }

    private static T? Load<T>(string fileName)
    {
        var path = Path.Combine(IpcContract.DataDirectory, fileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
    }
}

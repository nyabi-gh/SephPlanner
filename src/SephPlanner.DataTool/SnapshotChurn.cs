using System.Text.Json;
using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

/// <summary>
/// 스냅샷 한 장에서 F8 을 연달아 누르는 것을 흉내 낸다. 계획을 풀고, 그 목표대로 스냅샷을 옮기고,
/// 레벨은 우리 시뮬레이션 값으로 갈아 끼운 뒤(게임과 어긋난 칸이 0 인 판에서는 게임이 다시 계산할
/// 값과 같다) 다시 푼다. 적용한 뒤에도 계획이 또 바뀌면 그것이 "F8 이 계속 된다" 의 정체다 -
/// 점수가 오르며 수렴하는지, 같은 배치 사이를 도는지, 옮길 자리가 몇 개씩인지를 판마다 찍는다.
/// </summary>
public static class SnapshotChurn
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string snapshotPath, int rounds)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"스냅샷 파일이 없습니다: {snapshotPath}");
            return 1;
        }

        var snapshot = JsonSerializer.Deserialize<GameSnapshot>(File.ReadAllText(snapshotPath), Options);
        var inventory = snapshot?.Inventory;
        if (snapshot is null || inventory is null)
        {
            Console.Error.WriteLine("스냅샷에 인벤토리가 없습니다.");
            return 1;
        }

        var tablets = Load<List<TabletDefinition>>(PlannerData.TabletDbFile);
        var charms = Load<List<CharmDefinition>>(PlannerData.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine($"카탈로그가 없습니다. 게임을 한 번 실행해 {PlannerData.DataDirectory} 를 채우세요.");
            return 1;
        }
        var catalog = new Catalog(tablets, charms, Load<List<ComboDefinition>>(PlannerData.ComboDbFile));
        var preferences = Preferences();

        var recounted = Recount(inventory, catalog);
        var mismatch = recounted.Where(p => (inventory.ComboCounts.TryGetValue(p.Key, out var game) ? game : 0) != p.Value)
            .Concat(inventory.ComboCounts.Where(p => !recounted.ContainsKey(p.Key) && p.Value != 0))
            .Select(p => p.Key).Distinct().ToList();
        Console.WriteLine(mismatch.Count == 0
            ? "콤보 개수: 우리 셈이 게임과 같다."
            : "콤보 개수: 게임과 다른 카테고리 - " + string.Join(", ", mismatch) + " (이 판에서는 다시 센 값이 게임과 어긋난다)");

        Plan? previous = null;
        var seen = new Dictionary<string, int>();
        for (var round = 1; round <= rounds; round++)
        {
            var plan = PlanBuilder.Build(snapshot, catalog, preferences, previous);
            if (plan is null)
            {
                Console.WriteLine($"{round:00} 계획 없음");
                return 0;
            }
            previous = plan;

            var relocated = plan.Targets.Count(t =>
                t.From != t.To || t.IsTablet && t.FromRotation != t.Rotation);
            var signature = Signature(plan);
            var again = seen.TryGetValue(signature, out var firstRound) ? $"  *** {firstRound}번째와 같은 배치" : "";
            seen[signature] = round;

            Console.WriteLine("   콤보 " + string.Join(" ", inventory.ComboCounts.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));
            Console.WriteLine(
                $"{round:00} 점수 {plan.Current.Score:0.##} -> {plan.Best.Score:0.##} " +
                $"옮길 자리 {relocated}건 어긋난칸 {plan.LevelMismatches} " +
                $"검증 {(plan.Verification.Passed ? "통과" : plan.Verification.Reason)}{again}");

            foreach (var target in plan.Targets)
            {
                if (target.From == target.To && (!target.IsTablet || target.FromRotation == target.Rotation)) continue;
                Console.WriteLine(
                    $"     {(target.IsTablet ? "석판" : "아티팩트")} {target.InstanceId} {target.From}" +
                    (target.IsTablet ? $"r{target.FromRotation}" : "") +
                    $" -> {target.To}" + (target.IsTablet ? $"r{target.Rotation}" : ""));
            }

            if (relocated == 0 || plan.Targets.Count == 0)
            {
                Console.WriteLine("더 옮길 것이 없다.");
                return 0;
            }

            Apply(inventory, plan, catalog);
            if (snapshot.Run?.Combat is { } combat && plan.Best.Combat is { } predicted)
            {
                combat.ObservedStats = new(predicted.FinalStats);
                combat.ObservedAmplification = new(predicted.FinalAmplification);
            }
        }

        Console.WriteLine($"{rounds}번 뒤에도 계획이 바뀐다.");
        return 0;
    }

    /// <summary>플러그인이 쓰는 강화 우선 지정을 그대로 쓴다. 없으면 기본값이다.</summary>
    private static PlanPreferences Preferences()
    {
        var preferences = new PlanPreferences();
        var path = Path.Combine(PlannerData.DataDirectory, "plugin-settings.json");
        if (!File.Exists(path)) return preferences;

        try
        {
            var settings = JsonSerializer.Deserialize<PluginSettingsFile>(File.ReadAllText(path), Options);
            if (settings?.Combat != null) preferences.Combat = settings.Combat.Copy();
            if (settings?.PinnedLevels is not null)
            {
                foreach (var pair in settings.PinnedLevels) preferences.PinnedCharms[pair.Key] = pair.Value;
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("전투·빌드 설정 파일을 해석하지 못했습니다.", exception);
        }
        return preferences;
    }

    private sealed class PluginSettingsFile
    {
        public CombatScenario? Combat { get; set; }
        public Dictionary<int, int>? PinnedLevels { get; set; }
    }

    private static string Signature(Plan plan) =>
        string.Join(";", plan.Targets
            .OrderBy(t => t.InstanceId)
            .Select(t => $"{t.InstanceId}@{t.To}r{(t.IsTablet ? t.Rotation : 0)}"));

    /// <summary>목표대로 옮기고, 게임이 다시 계산할 레벨과 적용 여부를 우리 시뮬레이션 값으로 채운다.</summary>
    private static void Apply(InventoryState inventory, Plan plan, Catalog catalog)
    {
        var tabletsById = inventory.Tablets.ToDictionary(t => t.InstanceId);
        var itemsById = inventory.Items.ToDictionary(i => i.InstanceId);
        foreach (var target in plan.Targets)
        {
            if (target.IsTablet && tabletsById.TryGetValue(target.InstanceId, out var tablet))
            {
                tablet.Position = target.To;
                tablet.Rotation = target.Rotation;
                tablet.IsApplied = plan.Best.AppliedTablets.TryGetValue(target.InstanceId, out var applied) && applied;
            }
            else if (itemsById.TryGetValue(target.InstanceId, out var item))
            {
                item.Position = target.To;
            }
        }

        inventory.LevelMatrix = plan.Best.CellLevels.ToDictionary(p => $"{p.Key.X},{p.Key.Y}", p => p.Value);
        inventory.DisabledCells = plan.Best.DisabledCells.Select(c => $"{c.X},{c.Y}").ToList();
        foreach (var item in inventory.Items)
        {
            if (plan.Best.Levels.TryGetValue(item.Position, out var level)) item.EffectiveLevel = level;
            item.IsActive = !plan.Best.DisabledCells.Contains(item.Position);
        }
        inventory.ComboCounts = Recount(inventory, catalog);
    }

    /// <summary>
    /// 게임이 다시 셀 콤보 개수. 침·열쇠·종이는 자리에 따라 카테고리가 달라져 옮기면 개수가 바뀐다 -
    /// 그것을 빼먹으면 이 도구는 수렴하는데 게임은 도는 판을 놓친다(실제로 그랬다).
    /// </summary>
    private static Dictionary<string, int> Recount(InventoryState inventory, Catalog catalog)
    {
        var byCell = new Dictionary<GridPos, CharmSlot>();
        foreach (var item in inventory.Items)
        {
            var definition = catalog.Charm(item.DefinitionId);
            if (definition is null) continue;
            byCell[item.Position] = new CharmSlot { InstanceId = item.InstanceId, Definition = definition };
        }
        return ComboCounting.CountAll(byCell);
    }

    private static T? Load<T>(string fileName)
    {
        var path = PlannerData.ActiveDataFile(fileName);
        return path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            : default;
    }
}

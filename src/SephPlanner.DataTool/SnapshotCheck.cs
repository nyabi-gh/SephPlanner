using System.Text.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.DataTool;

/// <summary>
/// 저장해 둔 스냅샷 한 장으로 우리 레벨 계산을 게임이 계산해 둔 값과 견준다.
///
/// 게임 안에서는 플러그인의 SimulationVerifier 가 같은 일을 하지만, 그쪽은 게임을 다시 켜야
/// 새 코드가 돈다. 어긋나는 칸이 있으면 우리가 아직 읽지 않는 효과(각인, 세트 효과, 배치 보너스)가
/// 걸려 있다는 뜻이다.
/// </summary>
public static class SnapshotCheck
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static int Run(string snapshotPath)
    {
        if (!File.Exists(snapshotPath))
        {
            Console.Error.WriteLine($"스냅샷 파일이 없습니다: {snapshotPath}");
            return 1;
        }

        var snapshot = JsonSerializer.Deserialize<GameSnapshot>(File.ReadAllText(snapshotPath), Options);
        var inventory = snapshot?.Inventory;
        if (inventory is null)
        {
            Console.Error.WriteLine("스냅샷에 인벤토리가 없습니다.");
            return 1;
        }

        var tablets = Load<List<TabletDefinition>>(IpcContract.TabletDbFile);
        var charms = Load<List<CharmDefinition>>(IpcContract.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine($"카탈로그가 없습니다. 게임을 한 번 실행해 {IpcContract.DataDirectory} 를 채우세요.");
            return 1;
        }

        var tabletById = tablets.GroupBy(t => t.EntityId).ToDictionary(g => g.Key, g => g.First());
        var charmById = charms.GroupBy(c => c.EntityId).ToDictionary(g => g.Key, g => g.First());

        var grid = new GridSpec(inventory.Width, inventory.Height, inventory.Storage);
        var occupancy = Occupancy(inventory, charmById, grid);

        var layout = inventory.Tablets.Select(tablet => new TabletPlacement
        {
            Definition = tabletById.TryGetValue(tablet.DefinitionId, out var definition)
                ? definition
                : new TabletDefinition(),
            Position = tablet.Position,
            Rotation = tablet.Rotation,
            InstanceQuery = tablet.Query,
            InstanceConditionQuery = tablet.ConditionQuery,
        }).ToList();

        var result = TabletSimulator.Run(layout, occupancy, grid);

        Console.WriteLine($"게임 {snapshot!.GameVersion}  격자 {grid.Width}x{grid.Height} 열린 칸 {grid.Storage}");
        Console.WriteLine($"석판 {layout.Count}개, 아이템 {inventory.Items.Count}개, 멀티 {snapshot.IsMultiplayer}");
        Console.WriteLine($"장착 무기 {Describe(snapshot.Run?.WeaponId)}, 소지금 {Describe(snapshot.Run?.Gold.ToString())}");
        Console.WriteLine();

        var applied = result.Applied.Count(a => a);
        var reportedApplied = inventory.Tablets.Count(t => t.IsApplied);
        Console.WriteLine($"석판 적용: 우리 {applied}개 / 게임 {reportedApplied}개");
        Console.WriteLine();

        var mismatches = 0;
        var missingEnchant = 0;

        foreach (var item in inventory.Items.OrderBy(i => i.Position.Y).ThenBy(i => i.Position.X))
        {
            if (!inventory.LevelMatrix.TryGetValue($"{item.Position.X},{item.Position.Y}", out var reported)) continue;

            var ours = result.EffectiveLevel(item.Position, item.Enchant);
            if (ours == reported) continue;

            mismatches++;

            // 예전 플러그인이 만든 스냅샷에는 인챈트가 없어 0으로 온다. 차이가 딱 그만큼이면
            // 우리 모델이 아니라 스냅샷이 오래된 것이다.
            var looksLikeEnchant = item.Enchant == 0 && reported > ours;
            if (looksLikeEnchant) missingEnchant++;

            var name = charmById.TryGetValue(item.DefinitionId, out var charm) ? charm.Id : $"#{item.DefinitionId}";
            Console.WriteLine(
                $"  {item.Position} {name,-28} 우리 {ours,3} / 게임 {reported,3}" +
                (looksLikeEnchant ? "  (인챈트 미포함 스냅샷일 수 있음)" : ""));
        }

        Console.WriteLine();
        if (mismatches == 0)
        {
            Console.WriteLine("칸별 레벨이 모두 일치합니다.");
            return 0;
        }

        Console.WriteLine($"어긋난 칸 {mismatches}개 (그중 인챈트로 설명되는 것 {missingEnchant}개).");
        Console.WriteLine("나머지는 각인·세트 효과·배치 보너스처럼 아직 읽지 않는 효과일 수 있습니다.");
        return 0;
    }

    private static string Describe(string? value) => string.IsNullOrEmpty(value) ? "(스냅샷에 없음)" : value!;

    private static GridOccupancy Occupancy(
        InventoryState inventory, Dictionary<int, CharmDefinition> charmById, GridSpec grid)
    {
        var occupancy = new GridOccupancy();
        foreach (var tablet in inventory.Tablets) occupancy.AddItem(tablet.Position, false);

        foreach (var item in inventory.Items)
        {
            var isCharm = charmById.TryGetValue(item.DefinitionId, out var definition);
            occupancy.AddItem(item.Position, isCharm, isCharm && definition!.IsMagic);
        }
        return occupancy;
    }

    private static T? Load<T>(string fileName)
    {
        var path = Path.Combine(IpcContract.DataDirectory, fileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
    }
}

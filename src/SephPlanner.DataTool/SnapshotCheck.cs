using System.Text.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;

namespace SephPlanner.DataTool;

/// <summary>
/// 저장해 둔 스냅샷 한 장으로 우리 레벨 계산을 게임이 계산해 둔 값과 견준다.
///
/// 게임 안에서는 플러그인의 SimulationVerifier 가 같은 일을 하지만, 그쪽은 게임을 다시 켜야
/// 새 코드가 돈다. 어긋나는 칸이 있으면 우리가 아직 읽지 않는 효과(각인, 세트 효과, 배치 보너스)가
/// 걸려 있다는 뜻이다.
///
/// 계산은 오버레이와 같은 <see cref="PlanBuilder"/> 를 그대로 쓴다. 여기서 따로 계산하면
/// 진단 도구와 실제 동작이 어긋나 버린다.
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
        if (snapshot is null || inventory is null)
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

        var catalog = new Catalog(tablets, charms);
        var plan = PlanBuilder.Build(snapshot, catalog);
        if (plan is null)
        {
            Console.Error.WriteLine("배치를 계산할 수 없는 스냅샷입니다. (런이 진행 중이 아니거나 아티팩트가 없음)");
            return 1;
        }

        Console.WriteLine($"게임 {snapshot.GameVersion}  격자 {inventory.Width}x{inventory.Height} 열린 칸 {inventory.Storage}");
        Console.WriteLine($"석판 {inventory.Tablets.Count}개, 아이템 {inventory.Items.Count}개, 멀티 {snapshot.IsMultiplayer}");
        Console.WriteLine($"장착 무기 {Describe(snapshot.Run?.WeaponId)}, 소지금 {Describe(snapshot.Run?.Gold.ToString())}");
        Console.WriteLine($"현재 점수 {plan.Current.Score:0.#} → 제안 {plan.Best.Score:0.#}");
        Console.WriteLine();

        if (plan.LevelMismatches == 0)
        {
            Console.WriteLine("칸별 레벨이 모두 일치합니다.");
            return 0;
        }

        var explainedByEnchant = 0;
        foreach (var item in inventory.Items.OrderBy(i => i.Position.Y).ThenBy(i => i.Position.X))
        {
            if (!plan.Current.Levels.TryGetValue(item.Position, out var ours)) continue;
            if (!inventory.LevelMatrix.TryGetValue($"{item.Position.X},{item.Position.Y}", out var reported)) continue;
            if (ours == reported) continue;

            // 인챈트를 싣기 전 플러그인이 만든 스냅샷은 그 값이 0으로 온다. 차이가 그 방향이면
            // 우리 모델이 아니라 스냅샷이 오래된 것일 수 있다.
            var looksLikeEnchant = item.Enchant == 0 && reported > ours;
            if (looksLikeEnchant) explainedByEnchant++;

            var name = catalog.Charm(item.DefinitionId)?.Id ?? $"#{item.DefinitionId}";
            Console.WriteLine(
                $"  {item.Position} {name,-28} 우리 {ours,3} / 게임 {reported,3}" +
                (looksLikeEnchant ? "  (인챈트 미포함 스냅샷일 수 있음)" : ""));
        }

        Console.WriteLine();
        Console.WriteLine($"어긋난 칸 {plan.LevelMismatches}개 (그중 인챈트로 설명되는 것 {explainedByEnchant}개).");
        Console.WriteLine("나머지는 각인·세트 효과·배치 보너스처럼 아직 읽지 않는 효과일 수 있습니다.");
        return 0;
    }

    private static string Describe(string? value) => string.IsNullOrEmpty(value) ? "(스냅샷에 없음)" : value!;

    private static T? Load<T>(string fileName)
    {
        var path = Path.Combine(IpcContract.DataDirectory, fileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) : default;
    }
}

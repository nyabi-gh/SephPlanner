using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.DataTool;

/// <summary>
/// 저장해 둔 스냅샷 한 장으로 우리 레벨 계산을 게임이 계산해 둔 값과 견준다.
///
/// 게임 안에서는 플러그인의 SimulationVerifier 가 같은 일을 하지만, 그쪽은 게임을 다시 켜야
/// 새 코드가 돈다. 어긋나는 칸이 있으면 우리가 아직 읽지 않는 효과(각인, 세트 효과, 배치 보너스)가
/// 걸려 있다는 뜻이다.
///
/// 계산은 플러그인과 같은 <see cref="PlanBuilder"/>를 그대로 쓴다. 여기서 따로 계산하면
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

        var tablets = Load<List<TabletDefinition>>(PlannerData.TabletDbFile);
        var charms = Load<List<CharmDefinition>>(PlannerData.CharmDbFile);
        if (tablets is null || charms is null)
        {
            Console.Error.WriteLine($"카탈로그가 없습니다. 게임을 한 번 실행해 {PlannerData.DataDirectory} 를 채우세요.");
            return 1;
        }

        // 콤보를 빼고 만들면 플러그인과 다른 점수가 나온다. 진단 도구가 실제 동작과 어긋나면
        // 여기서 통과한 것이 실사용에서 재현되지 않는다. 콤보 파일은 없을 수 있어 선택이다.
        var combos = Load<List<ComboDefinition>>(PlannerData.ComboDbFile);
        var catalog = new Catalog(tablets, charms, combos);
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

        if (plan.Verification.Passed)
        {
            Console.WriteLine("레벨·유효 레벨·비활성·석판 적용 상태가 모두 일치합니다.");
            return 0;
        }

        var explainedByEnchant = 0;
        foreach (var pair in plan.Current.CellLevels.OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X))
        {
            var cell = pair.Key;
            var reported = 0;
            inventory.LevelMatrix?.TryGetValue($"{cell.X},{cell.Y}", out reported);
            if (pair.Value == reported) continue;

            var item = inventory.Items.FirstOrDefault(candidate => candidate.Position == cell);
            var looksLikeEnchant = item is not null && item.Enchant == 0 && reported > pair.Value;
            if (looksLikeEnchant) explainedByEnchant++;

            var name = item is null
                ? "(빈 칸)"
                : catalog.Charm(item.DefinitionId)?.Id ?? $"#{item.DefinitionId}";
            Console.WriteLine(
                $"  {cell} {name,-28} 우리 {pair.Value,3} / 게임 {reported,3}" +
                (looksLikeEnchant ? "  (인챈트 미포함 스냅샷일 수 있음)" : ""));
        }

        Console.WriteLine();
        Console.WriteLine(plan.Verification.Reason);
        Console.WriteLine(
            $"칸 레벨 {plan.Verification.LevelMismatches}개, " +
            $"아이템 유효 레벨 {plan.Verification.EffectiveLevelMismatches}개, " +
            $"비활성 상태 {plan.Verification.DisabledMismatches}개, " +
            $"석판 적용 상태 {plan.Verification.TabletMismatches}개");
        Console.WriteLine($"칸 레벨 차이 중 인챈트로 설명되는 것: {explainedByEnchant}개");
        return 0;
    }

    private static string Describe(string? value) => string.IsNullOrEmpty(value) ? "(스냅샷에 없음)" : value!;

    private static T? Load<T>(string fileName)
    {
        var path = PlannerData.ActiveDataFile(fileName);
        return path is not null && File.Exists(path)
            ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
            : default;
    }
}

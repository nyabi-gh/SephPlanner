using System.Diagnostics;
using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.DataTool;

public enum ChurnStatus { Converged, Blocked, Cycle, LimitReached }

public sealed class ChurnReport
{
    public ChurnStatus Status { get; set; }
    public string Reason { get; set; } = "";
    public List<Plan> Plans { get; } = new();
    public List<double> Milliseconds { get; } = new();
}

public static class PlanChurn
{
    public static int Run(string path, int rounds, bool allowModelChange = false)
    {
        try
        {
            var replay = JsonSerializer.Deserialize<PlanReplay>(PlanReplayFile.Read(path)) ??
                throw new InvalidDataException("재현 입력이 비었습니다.");
            var report = Analyze(replay, rounds, allowModelChange);
            Console.WriteLine("저장된 설정 전체·카탈로그·직전 목표로 반복 계산합니다. 네트워크 이동과 실제 전투·프레임 검증은 포함하지 않습니다.");
            for (var i = 0; i < report.Plans.Count; i++)
            {
                var plan = report.Plans[i];
                Console.WriteLine($"{i + 1:00} 예상 DPS {plan.Current.Score:0.##} → {plan.Best.Score:0.##}, " +
                    $"이동 {plan.Targets.Count(target => target.From != target.To || target.IsTablet && target.FromRotation != target.Rotation)}건, 계산 {report.Milliseconds[i]:0.##}ms");
            }
            Console.WriteLine(report.Reason);
            return report.Status == ChurnStatus.Converged ? 0 : 2;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine("반복 재현 실패: " + ex.Message);
            return 1;
        }
    }

    public static ChurnReport Analyze(PlanReplay input, int rounds, bool allowModelChange = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rounds);
        var replay = JsonSerializer.Deserialize<PlanReplay>(JsonSerializer.Serialize(input))!;
        var timer = Stopwatch.StartNew();
        var first = replay.Rebuild(allowModelChange);
        var firstMilliseconds = timer.Elapsed.TotalMilliseconds;
        var snapshot = replay.Snapshot!;
        var inventory = snapshot.Inventory!;
        var catalog = replay.Catalog!.Restore();
        var preferences = replay.Preferences!.Restore();
        var report = new ChurnReport();
        var seen = new HashSet<string>();
        Plan? previous = null;
        for (var round = 0; round < rounds; round++)
        {
            timer.Restart();
            var plan = round == 0 ? first : PlanBuilder.Build(snapshot, catalog, preferences, previous) ??
                throw new InvalidDataException("이동 후 계획을 만들지 못했습니다.");
            report.Plans.Add(plan);
            report.Milliseconds.Add(round == 0 ? firstMilliseconds : timer.Elapsed.TotalMilliseconds);
            var decision = AutoPlacePolicy.EvaluatePlacement(plan);
            if (!decision.Allowed)
            {
                report.Status = plan.Verification.Passed && !plan.HasPlacementChanges && plan.Targets.Count > 0 ?
                    ChurnStatus.Converged : ChurnStatus.Blocked;
                report.Reason = report.Status == ChurnStatus.Converged ? "배치가 수렴했습니다." : "적용 제한: " + decision.Reason;
                return report;
            }
            var command = plan.CreateApplyCommand();
            var live = inventory.Items.Select(item => new LivePlanItem { InstanceId = item.InstanceId, Position = item.Position })
                .Concat(inventory.Tablets.Select(tablet => new LivePlanItem
                {
                    InstanceId = tablet.InstanceId,
                    Position = tablet.Position,
                    IsTablet = true,
                    Rotation = tablet.Rotation,
                    CanRotate = tablet.IsRotatable ?? catalog.Tablet(tablet.DefinitionId)?.IsRotatable ?? false,
                })).ToList();
            var validation = ApplyPlanValidator.Validate(command, live, inventory.Width, inventory.Height, inventory.Storage);
            if (validation != null)
            {
                report.Status = ChurnStatus.Blocked;
                report.Reason = "명령 검증 실패: " + validation;
                return report;
            }
            var signature = string.Join(";", plan.Targets.OrderBy(target => target.InstanceId)
                .Select(target => $"{target.InstanceId}@{target.To}r{target.Rotation}"));
            if (!seen.Add(signature))
            {
                report.Status = ChurnStatus.Cycle;
                report.Reason = "이전 배치로 돌아오는 순환을 발견했습니다.";
                return report;
            }
            Apply(snapshot, catalog, plan);
            previous = plan;
        }
        report.Status = ChurnStatus.LimitReached;
        report.Reason = $"{rounds}회 안에 수렴을 확인하지 못했습니다.";
        return report;
    }

    private static void Apply(GameSnapshot snapshot, Catalog catalog, Plan plan)
    {
        var inventory = snapshot.Inventory!;
        var before = Count(inventory, catalog);
        var targets = plan.Targets.ToDictionary(target => target.InstanceId);
        foreach (var tablet in inventory.Tablets)
        {
            var target = targets[tablet.InstanceId];
            tablet.Position = target.To;
            tablet.Rotation = target.Rotation;
            tablet.IsApplied = plan.Best.AppliedTablets.GetValueOrDefault(tablet.InstanceId);
        }
        foreach (var item in inventory.Items)
        {
            item.Position = targets[item.InstanceId].To;
            item.EffectiveLevel = plan.Best.Levels.GetValueOrDefault(item.Position);
            item.IsActive = !plan.Best.InactiveCharms.Contains(item.InstanceId);
        }
        inventory.LevelMatrix = plan.Best.CellLevels.ToDictionary(pair => $"{pair.Key.X},{pair.Key.Y}", pair => pair.Value);
        inventory.DisabledCells = plan.Best.DisabledCells.Select(cell => $"{cell.X},{cell.Y}").ToList();
        var after = Count(inventory, catalog);
        foreach (var key in before.Keys.Union(after.Keys))
            inventory.ComboCounts[key] = inventory.ComboCounts.GetValueOrDefault(key) - before.GetValueOrDefault(key) + after.GetValueOrDefault(key);
        if (snapshot.Run?.Combat is { } combat && plan.Best.Combat is { } predicted)
        {
            combat.ObservedStats = new(predicted.FinalStats);
            combat.ObservedAmplification = new(predicted.FinalAmplification);
        }
    }

    private static Dictionary<string, int> Count(InventoryState inventory, Catalog catalog) => ComboCounting.CountAll(inventory.Items
        .Where(item => catalog.Charm(item.DefinitionId) != null).ToDictionary(item => item.Position, item => new CharmSlot
        {
            InstanceId = item.InstanceId,
            Definition = catalog.Charm(item.DefinitionId)!,
            IsAttackable = item.IsAttackable,
            ObservedCategories = item.ObservedCategories,
        }));
}

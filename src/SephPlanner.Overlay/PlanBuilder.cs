using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Overlay;

public sealed record Move(string Label, GridPos From, GridPos To);

public sealed class Plan
{
    public required Arrangement Current { get; init; }
    public required Arrangement Best { get; init; }
    public required List<Move> Moves { get; init; }
    public double Gain => Best.Score - Current.Score;
}

/// <summary>스냅샷과 카탈로그를 합쳐 현재 배치를 채점하고 더 나은 배치를 찾는다.</summary>
public static class PlanBuilder
{
    public static Plan? Build(GameSnapshot snapshot, CatalogStore catalog)
    {
        var inventory = snapshot.Inventory;
        if (inventory is null || inventory.Storage <= 0) return null;

        var grid = new GridSpec(inventory.Width, inventory.Height, inventory.Storage);
        var problem = new PlacementProblem { Grid = grid };

        var layout = new List<TabletPlacement>();
        foreach (var tablet in inventory.Tablets)
        {
            var definition = catalog.Tablet(tablet.DefinitionId) ?? new TabletDefinition();
            var slot = new TabletSlot
            {
                Definition = definition,
                InstanceId = tablet.InstanceId,
                InstanceQuery = tablet.Query,
                InstanceConditionQuery = tablet.ConditionQuery,
            };
            problem.Tablets.Add(slot);
            layout.Add(slot.At(tablet.Position, tablet.Rotation));
        }

        var occupancy = BuildOccupancy(inventory, catalog);
        var tabletLevels = TabletSimulator.Run(layout, occupancy, grid);

        var positions = new Dictionary<int, GridPos>();
        foreach (var item in inventory.Items)
        {
            var definition = catalog.Charm(item.DefinitionId);
            if (definition is null) continue;

            problem.Charms.Add(new CharmSlot
            {
                Definition = definition,
                InstanceId = item.InstanceId,
                Enchant = DeriveEnchant(item, tabletLevels),
            });
            positions[item.InstanceId] = item.Position;
        }

        if (problem.Charms.Count == 0) return null;

        var current = PlacementSolver.Score(problem, layout, positions);
        var best = PlacementSolver.Solve(problem);
        return new Plan { Current = current, Best = best, Moves = Moves(problem, current, best) };
    }

    /// <summary>
    /// 인챈트로 붙은 고정 레벨은 따로 알 수 없어, 게임이 보고한 레벨에서 석판 몫을 빼서 구한다.
    /// 배수가 걸린 칸에서는 나눗셈이 정확히 떨어지지 않을 수 있어 근사값이 된다.
    /// </summary>
    private static int DeriveEnchant(PlacedItem item, SimulationResult tabletLevels)
    {
        var reported = item.EffectiveLevel;
        if (tabletLevels.MultiplyLevel.TryGetValue(item.Position, out var multiplier) && multiplier != 0)
            reported /= multiplier;

        return reported - tabletLevels.LevelAt(item.Position);
    }

    private static GridOccupancy BuildOccupancy(InventoryState inventory, CatalogStore catalog)
    {
        var occupancy = new GridOccupancy();
        foreach (var tablet in inventory.Tablets) occupancy.AddItem(tablet.Position, false);

        foreach (var item in inventory.Items)
        {
            var definition = catalog.Charm(item.DefinitionId);
            occupancy.AddItem(item.Position, definition is not null, definition?.IsMagic ?? false);
        }
        return occupancy;
    }

    private static List<Move> Moves(PlacementProblem problem, Arrangement current, Arrangement best)
    {
        var moves = new List<Move>();

        for (var i = 0; i < problem.Tablets.Count && i < best.Tablets.Count; i++)
        {
            var from = current.Tablets[i];
            var to = best.Tablets[i];
            if (from.Position == to.Position && from.Rotation == to.Rotation) continue;

            var name = Name(problem.Tablets[i].Definition.Id, "석판");
            moves.Add(new Move(name, from.Position, to.Position));
        }

        foreach (var charm in problem.Charms)
        {
            if (!current.CharmPositions.TryGetValue(charm.InstanceId, out var from)) continue;
            if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var to)) continue;
            if (from == to) continue;

            moves.Add(new Move(Name(charm.Definition.Id, "아티팩트"), from, to));
        }
        return moves;
    }

    private static string Name(string id, string fallback) => string.IsNullOrEmpty(id) ? fallback : id;
}

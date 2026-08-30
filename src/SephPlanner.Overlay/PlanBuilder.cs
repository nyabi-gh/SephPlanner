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
    public required List<OfferAdvice> Offers { get; init; }
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

        var occupancy = BuildOccupancy(inventory, catalog, grid);
        var tabletLevels = TabletSimulator.Run(layout, occupancy, grid);

        var positions = new Dictionary<int, GridPos>();
        foreach (var item in inventory.Items)
        {
            if (!IsOnGrid(item.Position, grid)) continue;

            // 아티팩트가 아닌 아이템도 칸을 차지한다. 빼놓으면 솔버가 그 자리를 비어 있다고 본다.
            var definition = catalog.Charm(item.DefinitionId);
            problem.Charms.Add(new CharmSlot
            {
                Definition = definition ?? new CharmDefinition(),
                InstanceId = item.InstanceId,
                Enchant = definition is null ? 0 : DeriveEnchant(item, tabletLevels),
                IsFiller = definition is null,
            });
            positions[item.InstanceId] = item.Position;
        }

        if (problem.Charms.All(charm => charm.IsFiller)) return null;

        var current = PlacementSolver.Score(problem, layout, positions);
        var best = PlacementSolver.Solve(problem);
        var offers = OfferAdvisor.Rank(problem, best.Score, Candidates(snapshot, catalog));

        return new Plan
        {
            Current = current,
            Best = best,
            Moves = Moves(problem, current, best),
            Offers = offers,
        };
    }

    /// <summary>후보가 많으면 한 번에 다 풀기에는 무거워, 종류가 같은 것은 하나로 묶고 수를 제한한다.</summary>
    private const int MaxCandidates = 8;

    private static List<OfferCandidate> Candidates(GameSnapshot snapshot, CatalogStore catalog)
    {
        var candidates = new List<OfferCandidate>();
        var seen = new HashSet<int>();

        foreach (var offer in snapshot.Offers)
        {
            if (!seen.Add(offer.DefinitionId)) continue;
            if (candidates.Count >= MaxCandidates) break;

            var charm = offer.Kind == "charm" ? catalog.Charm(offer.DefinitionId) : null;
            var tablet = offer.Kind == "tablet" ? catalog.Tablet(offer.DefinitionId) : null;
            if (charm is null && tablet is null) continue;

            candidates.Add(new OfferCandidate
            {
                DefinitionId = offer.DefinitionId,
                Kind = offer.Kind,
                Name = Display(charm?.Names ?? tablet!.Names, charm?.Id ?? tablet!.Id),
                Price = offer.Price,
                Charm = charm,
                Tablet = tablet,
            });
        }
        return candidates;
    }

    private static string Display(Dictionary<string, string> names, string fallback) =>
        names.TryGetValue("current", out var text) && text.Length > 0 ? text : fallback;

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

    private static GridOccupancy BuildOccupancy(InventoryState inventory, CatalogStore catalog, GridSpec grid)
    {
        var occupancy = new GridOccupancy();
        foreach (var tablet in inventory.Tablets) occupancy.AddItem(tablet.Position, false);

        foreach (var item in inventory.Items)
        {
            if (!IsOnGrid(item.Position, grid)) continue;

            var definition = catalog.Charm(item.DefinitionId);
            occupancy.AddItem(item.Position, definition is not null, definition?.IsMagic ?? false);
        }
        return occupancy;
    }

    /// <summary>보조 가방처럼 본 격자 밖에 있는 자리는 배치 대상이 아니다.</summary>
    private static bool IsOnGrid(GridPos position, GridSpec grid) =>
        position.X >= 0 && position.X < grid.Width &&
        position.Y >= 0 && position.Y < grid.Height &&
        grid.ToIndex(position.X, position.Y) < grid.Storage;

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

            moves.Add(new Move(Name(charm.Definition.Id, charm.IsFiller ? "아이템" : "아티팩트"), from, to));
        }
        return moves;
    }

    private static string Name(string id, string fallback) => string.IsNullOrEmpty(id) ? fallback : id;
}

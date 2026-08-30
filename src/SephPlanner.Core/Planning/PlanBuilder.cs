using System.Collections.Generic;
using System.Linq;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Planning
{
    /// <summary>스냅샷과 카탈로그를 합쳐 현재 배치를 채점하고 더 나은 배치를 찾는다.</summary>
    public static class PlanBuilder
    {
        public static Plan? Build(GameSnapshot snapshot, ICatalog catalog)
        {
            var inventory = snapshot.Inventory;
            if (inventory is null || inventory.Storage <= 0) return null;

            var grid = new GridSpec(inventory.Width, inventory.Height, inventory.Storage);
            var problem = new PlacementProblem { Grid = grid };

            // 빔 서치는 후보를 살펴보는 순서에 따라 같은 점수의 다른 배치를 내놓는다. 스냅샷의 순서는
            // 석판을 옮기면 바뀌므로, 여기서 한 번 고정해 두어야 제안이 흔들리지 않는다.
            var orderedTablets = inventory.Tablets
                .OrderBy(t => t.DefinitionId).ThenBy(t => t.InstanceId)
                .ToList();

            var layout = new List<TabletPlacement>();
            foreach (var tablet in orderedTablets)
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
                problem.CurrentTablets[slot.InstanceId] = new TabletSpot(tablet.Position, tablet.Rotation);
            }

            // 무기 연동 아티팩트는 해당 무기를 들고 있어야 효과가 켜진다. 무기를 모르면 판정하지 않는다.
            var weapon = snapshot.Run?.WeaponId ?? "";

            var positions = new Dictionary<int, GridPos>();
            foreach (var item in inventory.Items
                         .OrderBy(i => i.DefinitionId).ThenBy(i => i.InstanceId))
            {
                if (!IsOnGrid(item.Position, grid)) continue;

                // 아티팩트가 아닌 아이템도 칸을 차지한다. 빼놓으면 솔버가 그 자리를 비어 있다고 본다.
                var definition = catalog.Charm(item.DefinitionId);
                problem.Charms.Add(new CharmSlot
                {
                    Definition = definition ?? new CharmDefinition(),
                    InstanceId = item.InstanceId,
                    Enchant = definition is null ? 0 : item.Enchant,
                    IsFiller = definition is null,
                    IsDormant = definition is not null && WeaponMatch.IsDormant(definition, weapon),
                });
                positions[item.InstanceId] = item.Position;
                problem.CurrentCharms[item.InstanceId] = item.Position;
            }

            if (problem.Charms.All(charm => charm.IsFiller)) return null;

            var current = PlacementSolver.Score(problem, layout, positions);
            var best = PlacementSolver.Solve(problem);
            var offers = OfferAdvisor.Rank(
                problem, best.Score, Candidates(snapshot, catalog, weapon), snapshot.Run?.Gold ?? int.MaxValue);

            return new Plan
            {
                LevelMismatches = CountLevelMismatches(inventory, current),
                Current = current,
                Best = best,
                Moves = Moves(problem, current, best),
                Offers = offers,
                Names = NamesByCell(problem, best),
            };
        }

        private static Dictionary<GridPos, string> NamesByCell(PlacementProblem problem, Arrangement best)
        {
            var names = new Dictionary<GridPos, string>();
            foreach (var charm in problem.Charms)
            {
                if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var position)) continue;
                names[position] = Naming.Of(
                    charm.Definition.Names, charm.Definition.Id, charm.IsFiller ? "아이템" : "아티팩트");
            }
            return names;
        }

        /// <summary>후보가 많으면 한 번에 다 풀기에는 무거워, 종류가 같은 것은 하나로 묶고 수를 제한한다.</summary>
        private const int MaxCandidates = 8;

        private static List<OfferCandidate> Candidates(GameSnapshot snapshot, ICatalog catalog, string weapon)
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
                    Name = Naming.Of(charm?.Names ?? tablet!.Names, charm?.Id ?? tablet!.Id, "?"),
                    Price = offer.Price,
                    Charm = charm,
                    Tablet = tablet,
                    CharmIsDormant = charm is not null && WeaponMatch.IsDormant(charm, weapon),
                });
            }
            return candidates;
        }

        /// <summary>
        /// 지금 배치를 우리가 계산한 레벨과 게임이 계산해 둔 레벨을 칸마다 견준다.
        ///
        /// 각인과 세트 효과, 배치 보너스는 아직 모델에 없어서 그런 것이 걸려 있으면 점수가 어긋난다.
        /// 무엇이 걸려 있을지 미리 추측해 경고하는 대신, 실제로 어긋날 때만 세어 알린다.
        /// </summary>
        private static int CountLevelMismatches(InventoryState inventory, Arrangement current)
        {
            var mismatches = 0;
            foreach (var pair in current.Levels)
            {
                var key = pair.Key.X + "," + pair.Key.Y;
                if (!inventory.LevelMatrix.TryGetValue(key, out var reported)) continue;
                if (reported != pair.Value) mismatches++;
            }
            return mismatches;
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
                var turned = from.Rotation != to.Rotation;
                if (from.Position == to.Position && !turned) continue;

                var definition = problem.Tablets[i].Definition;
                var name = Naming.Of(definition.Names, definition.Id, "석판");
                var detail = from.Position == to.Position
                    ? $"{from.Position} 회전 {from.Rotation} → {to.Rotation}"
                    : turned
                        ? $"{from.Position} → {to.Position} 회전 {to.Rotation}"
                        : $"{from.Position} → {to.Position}";

                moves.Add(new Move(name, from.Position, to.Position, detail));
            }

            foreach (var charm in problem.Charms)
            {
                if (!current.CharmPositions.TryGetValue(charm.InstanceId, out var from)) continue;
                if (!best.CharmPositions.TryGetValue(charm.InstanceId, out var to)) continue;
                if (from == to) continue;

                var name = Naming.Of(charm.Definition.Names, charm.Definition.Id, charm.IsFiller ? "아이템" : "아티팩트");
                moves.Add(new Move(name, from, to, $"{from} → {to}"));
            }
            return moves;
        }
    }
}

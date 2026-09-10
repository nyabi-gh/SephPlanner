using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Combat
{
    internal sealed class CombatEstimateCache
    {
        internal List<LocatedCombatCharm>? Reference;
        internal double Baseline;
        internal Dictionary<(int Instance, int Level, GridPos Cell, bool Active), double> Values = new Dictionary<(int, int, GridPos, bool), double>();
    }

    public static class CombatPlanning
    {
        public static PlacementCombatContext Capture(PlacementProblem problem, Arrangement current, CombatSnapshot snapshot,
            CombatScenario scenario, CancellationToken cancellation = default)
        {
            CombatSimulator.Validate(scenario);
            var context = new PlacementCombatContext { Snapshot = snapshot, Scenario = scenario, Cancellation = cancellation };
            context.Current = Locate(problem, current);
            var counted = ComboCounting.CountAll(context.Current.ToDictionary(item => item.Position, item => item.Charm));
            if (problem.ComboCounts != null)
                foreach (var key in problem.ComboCounts.Keys.Concat(counted.Keys).Distinct())
                    context.ExtraComboCounts[key] = problem.ComboCounts.GetValueOrDefault(key) - counted.GetValueOrDefault(key);
            var modeled = CombatLoadouts.Build(problem, context, context.Current);
            var background = new CombatStatSource
            {
                Id = "observed-background",
                Stats = new Dictionary<string, int>(snapshot.ObservedStats),
                Amplification = new Dictionary<string, int>(snapshot.ObservedAmplification),
            };
            foreach (var source in modeled.Stats.Export())
            {
                foreach (var pair in source.Stats) CombatLoadouts.Add(background.Stats, pair.Key, -pair.Value);
                foreach (var pair in source.Amplification) CombatLoadouts.Add(background.Amplification, pair.Key, -pair.Value);
            }
            context.Background.SetSource(background);
            return context;
        }

        internal static List<LocatedCombatCharm> Locate(PlacementProblem problem, Arrangement arrangement) => problem.Charms
            .Where(charm => arrangement.CharmPositions.ContainsKey(charm.InstanceId)).Select(charm => new LocatedCombatCharm
            {
                Charm = charm,
                Position = arrangement.CharmPositions[charm.InstanceId],
                Level = arrangement.Levels.GetValueOrDefault(arrangement.CharmPositions[charm.InstanceId]),
                Active = !charm.IsDormant && !arrangement.InactiveCharms.Contains(charm.InstanceId),
            }).ToList();

        internal static CombatResult Evaluate(PlacementProblem problem, IReadOnlyList<LocatedCombatCharm> placed, bool details = false)
        {
            var context = problem.Combat ?? throw new InvalidOperationException("전투 스냅샷이 없습니다.");
            return CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, placed, details), context.Snapshot, context.Scenario, details, context.Cancellation);
        }

        internal static void AddEmptyStartComparison(PlacementProblem problem, Arrangement arrangement)
        {
            if (problem.Combat is not { } context || arrangement.Combat is not { } result) return;
            var scenario = context.Scenario.Copy();
            scenario.InitialManaFraction = 0;
            scenario.InitialChargeFraction = 0;
            result.EmptyStartDps = CombatSimulator.Evaluate(CombatLoadouts.Build(problem, context, Locate(problem, arrangement), false),
                context.Snapshot, scenario, false, context.Cancellation).Dps;
        }

        internal static double Estimate(PlacementProblem problem, CharmSlot charm, int level, GridPos cell, bool active)
        {
            if (charm.IsFiller) return 0;
            var context = problem.Combat!;
            var cache = problem.CombatEstimates;
            level = Math.Min(level, charm.Definition.MaxLevel);
            var key = (charm.InstanceId, level, cell, active);
            if (cache.Values.TryGetValue(key, out var cached)) return cached;
            if (cache.Reference == null)
            {
                var ids = new HashSet<int>(problem.Charms.Select(item => item.InstanceId));
                cache.Reference = context.Current.Where(item => ids.Contains(item.Charm.InstanceId)).ToList();
                var used = new HashSet<GridPos>(cache.Reference.Select(item => item.Position));
                used.UnionWith(problem.CurrentTablets.Values.Select(spot => spot.Position));
                foreach (var added in problem.Charms.Where(item => !cache.Reference.Any(current => current.Charm.InstanceId == item.InstanceId)))
                {
                    var spot = Enumerable.Range(0, problem.Grid.Storage).Select(problem.Grid.ToPosition).FirstOrDefault(position => !used.Contains(position));
                    if (used.Contains(spot)) continue;
                    used.Add(spot);
                    cache.Reference.Add(new LocatedCombatCharm { Charm = added, Position = spot, Level = added.Enchant, Active = !added.IsDormant });
                }
                cache.Baseline = Evaluate(problem, cache.Reference).Dps;
            }
            var previous = cache.Reference.FirstOrDefault(item => item.Charm.InstanceId == charm.InstanceId);
            var variant = new List<LocatedCombatCharm>();
            foreach (var item in cache.Reference)
            {
                if (item.Charm.InstanceId == charm.InstanceId) continue;
                if (item.Position != cell) variant.Add(item);
                else if (previous != null && previous.Position != cell)
                    variant.Add(new LocatedCombatCharm { Charm = item.Charm, Position = previous.Position, Level = item.Level, Active = item.Active });
            }
            variant.Add(new LocatedCombatCharm { Charm = charm, Position = cell, Level = level, Active = active && !charm.IsDormant });
            var value = Evaluate(problem, variant).Dps - cache.Baseline;
            cache.Values.Add(key, value);
            return value;
        }
    }
}

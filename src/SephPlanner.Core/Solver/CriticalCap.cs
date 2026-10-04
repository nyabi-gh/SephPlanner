using System;
using System.Collections.Generic;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 치명타 확률의 상한. 게임은 확률이 100% 를 넘으면 넘은 몫을 버린다. 처형(<c>EXECUTION</c>,
    /// 베루트의 낫)이 있으면 100% 위의 몫이 처형 확률로 바뀌어 200% 까지 쓰인다
    /// (<c>UnitAvatar.TakeDamage</c>, 1.0.33). 일반 공격은 <c>CRITICAL + WEAPONCRITICAL</c>, 마법은
    /// <c>CRITICAL + MAGICCRITICAL</c> 로 따로 굴린다(<c>DamageInstance.Set</c>).
    ///
    /// 아티팩트마다 값어치를 따로 매기는 모델은 상한을 표현하지 못하므로, 배치 전체의 치명타를 더해
    /// 넘친 몫의 값어치를 점수에서 뺀다. 적의 치명타 저항(<c>CRITICALRESIST</c>)은 모르므로 보지 않는다.
    /// </summary>
    public static class CriticalCap
    {
        /// <summary>게임 단위로 100%. 능력치 표의 치명타는 100 이 1% 다.</summary>
        public const int Certain = 10000;

        private const string Critical = "CRITICAL";
        private const string MagicCritical = "MAGIC_CRITICAL";
        private const string Scythe = "Charm_ScytheOfBerut";

        /// <summary>
        /// 캐릭터의 치명타에서 우리가 값어치를 매기는 아티팩트 몫을 뺀 것. 무기·패시브·콤보 몫이 남는다.
        /// 아티팩트 몫은 점수와 같은 <see cref="Amount"/> 로 빼므로, 우리가 못 세는 출처가 있어도
        /// 그 몫은 양쪽에서 함께 바탕에 남는다.
        /// </summary>
        public static CriticalBase Base(
            InventoryState inventory, ICatalog catalog, int critical, int weaponCritical, int magicCritical, int execution)
        {
            int direct = critical + weaponCritical, magic = critical + magicCritical, scythes = 0;
            foreach (var item in inventory.Items)
            {
                var definition = catalog.Charm(item.DefinitionId);
                if (definition is null || !item.IsActive) continue;

                var level = Math.Min(item.EffectiveLevel, definition.MaxLevel);
                var own = Amount(definition, Critical, level, inventory.Tablets.Count);
                direct -= own;
                magic -= own + Amount(definition, MagicCritical, level, inventory.Tablets.Count);
                if (definition.Behavior == Scythe) scythes++;
            }
            return new CriticalBase { Direct = direct, Magic = magic, Execution = execution - scythes > 0 };
        }

        /// <summary>이 배치에서 상한을 넘겨 버려지는 치명타의 값어치.</summary>
        internal static double Penalty(
            PlacementProblem problem, Dictionary<int, GridPos> positions, SimulationResult result, GridOccupancy occupancy)
        {
            var based = problem.Critical;
            if (based is null) return 0;
            var model = problem.CriticalModel ??= CriticalModel.Of(problem);
            if (model.Charms.Count == 0) return 0;

            int direct = 0, magic = 0;
            var execution = based.Execution;
            foreach (var charm in model.Charms)
            {
                if (!positions.TryGetValue(charm.InstanceId, out var cell)) continue;
                if (PlacementSolver.Reason(charm, cell, result, problem.Grid, occupancy) != CharmInactiveReason.None) continue;

                var level = Math.Min(result.EffectiveLevel(cell, charm.Enchant), charm.Definition.MaxLevel);
                direct += Amount(charm.Definition, Critical, level, problem.Tablets.Count);
                magic += Amount(charm.Definition, MagicCritical, level, problem.Tablets.Count);
                execution |= charm.Definition.Behavior == Scythe;
            }

            var cap = execution ? 2 * Certain : Certain;
            var directOver = Math.Max(0, Math.Min(based.Direct + direct - cap, direct));
            var magicOver = Math.Max(0, Math.Min(based.Magic + direct + magic - cap, magic));
            return directOver * model.CriticalWorth + magicOver * model.MagicCriticalWorth;
        }

        /// <summary>
        /// 그 레벨에서 주는 치명타. 석판 수를 따르는 것만 센다 - 다른 출처를 따르는 치명타는 지금
        /// 카탈로그에 없고, 생겨도 <see cref="Base"/> 와 같은 함수로 빠지므로 바탕에 그대로 남는다.
        /// </summary>
        internal static int Amount(CharmDefinition definition, string status, int level, int tablets)
        {
            var total = 0;
            foreach (var effect in definition.StatEffects)
                if (effect.StatusId == status) total += At(effect.AmountByLevel, level);
            foreach (var bonus in definition.ContextStats)
                if (bonus.StatusId == status && bonus.Source == StatCountSource.StoneTablets)
                    total += (int)Math.Round(At(bonus.AmountByLevel, level) * tablets);
            return total;
        }

        private static int At(List<int> table, int level) =>
            table.Count == 0 ? 0 : table[Math.Min(Math.Max(0, level), table.Count - 1)];

        private static double At(List<double> table, int level) =>
            table.Count == 0 ? 0 : table[Math.Min(Math.Max(0, level), table.Count - 1)];

        internal sealed class CriticalModel
        {
            public List<CharmSlot> Charms { get; } = new List<CharmSlot>();
            public double CriticalWorth { get; private set; }
            public double MagicCriticalWorth { get; private set; }

            public static CriticalModel Of(PlacementProblem problem)
            {
                var model = new CriticalModel();
                foreach (var charm in problem.Charms)
                {
                    if (charm.IsFiller) continue;
                    var definition = charm.Definition;
                    var relevant = definition.Behavior == Scythe;
                    foreach (var effect in definition.StatEffects)
                    {
                        if (effect.StatusId == Critical) relevant = true;
                        if (effect.StatusId == MagicCritical) relevant = true;
                        model.Take(effect.StatusId, effect.WorthPerUnit);
                    }
                    foreach (var bonus in definition.ContextStats)
                    {
                        if (bonus.StatusId == Critical || bonus.StatusId == MagicCritical) relevant = true;
                        model.Take(bonus.StatusId, bonus.WorthPerUnit);
                    }
                    if (relevant) model.Charms.Add(charm);
                }
                return model;
            }

            private void Take(string status, double? worth)
            {
                if (worth is not double value) return;
                if (status == Critical && CriticalWorth == 0) CriticalWorth = value;
                if (status == MagicCritical && MagicCriticalWorth == 0) MagicCriticalWorth = value;
            }
        }
    }
}

using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 치명타는 100% 를 넘으면 버려지고, 처형이 있으면 200% 까지 쓰인다(게임 <c>UnitAvatar.TakeDamage</c>).
/// 제보 27f25bbc 는 치명타 118.5% 인 판에서 치명타 아티팩트에 레벨을 몰아주는 배치를 받았다.
/// </summary>
public class CriticalCapTests
{
    private const double PerUnit = 0.001;

    private static CharmDefinition Critical(string status = "CRITICAL") => new()
    {
        EntityId = 10,
        MaxLevel = 2,
        StatWorthCoverageKnown = true,
        StatEffects = { new CharmStatEffect { StatusId = status, AmountByLevel = { 1000, 2000, 3000 }, WorthPerUnit = PerUnit } },
    };

    private static PlacementProblem OneCharm(CharmDefinition definition, CriticalBase? critical)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 1, 2), Critical = critical };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = definition, Worth = CharmWorth.Resolve(definition) });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        return problem;
    }

    private static double Score(PlacementProblem problem) =>
        PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms).Score;

    [Fact]
    public void CriticalPastCertainIsWorthNothing()
    {
        var unknown = Score(OneCharm(Critical(), null));
        var capped = Score(OneCharm(Critical(), new CriticalBase { Direct = 9500 }));
        Assert.Equal(500 * PerUnit, unknown - capped, 6);
    }

    [Fact]
    public void CriticalBelowCertainKeepsItsWorth()
    {
        var unknown = Score(OneCharm(Critical(), null));
        Assert.Equal(unknown, Score(OneCharm(Critical(), new CriticalBase { Direct = 9000 })), 6);
    }

    [Fact]
    public void ExecutionTurnsTheOverflowIntoUse()
    {
        var unknown = Score(OneCharm(Critical(), null));
        Assert.Equal(unknown, Score(OneCharm(Critical(), new CriticalBase { Direct = 9500, Execution = true })), 6);

        var problem = OneCharm(Critical(), new CriticalBase { Direct = 9500 });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition { EntityId = 11, MaxLevel = 1, Behavior = "Charm_ScytheOfBerut" },
        });
        problem.CurrentCharms[2] = new GridPos(1, 0);
        var capped = Score(problem);
        problem.Critical = null;
        Assert.Equal(Score(problem), capped, 6);
    }

    [Fact]
    public void AnInactiveCharmGivesNoCritical()
    {
        var problem = OneCharm(Critical(), new CriticalBase { Direct = 9500 });
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Disable = 1 });
        var disabled = Score(problem);
        problem.Critical = null;
        Assert.Equal(Score(problem), disabled, 6);
    }

    /// <summary>일반 치명타는 마법에도 더해지므로, 마법 치명타는 그 위에서 넘친다.</summary>
    [Fact]
    public void MagicCriticalOverflowsOnTopOfCritical()
    {
        var unknown = Score(OneCharm(Critical("MAGIC_CRITICAL"), null));
        var capped = Score(OneCharm(Critical("MAGIC_CRITICAL"), new CriticalBase { Direct = 9500, Magic = 9800 }));
        Assert.Equal(800 * PerUnit, unknown - capped, 6);
    }

    /// <summary>
    /// 바탕은 게임이 보여 주는 치명타에서 우리가 세는 아티팩트 몫을 뺀 것이다. 꺼진 것은 주지 않으므로
    /// 빼지 않고, 석판 수를 따르는 몫은 지금 석판 수로 뺀다.
    /// </summary>
    [Fact]
    public void TheBaseLeavesOnlyWhatArtifactsDoNotGive()
    {
        var active = Critical();
        var perTablet = new CharmDefinition
        {
            EntityId = 12,
            MaxLevel = 1,
            ContextStats = { new ContextStatBonus { Source = StatCountSource.StoneTablets, StatusId = "CRITICAL", AmountByLevel = { 100, 150 } } },
        };
        var off = Critical();
        off.EntityId = 13;
        var scythe = new CharmDefinition { EntityId = 14, MaxLevel = 1, Behavior = "Charm_ScytheOfBerut" };
        var catalog = new Catalog(new List<TabletDefinition>(), new[] { active, perTablet, off, scythe });

        var inventory = new InventoryState
        {
            Items =
            {
                new PlacedItem { DefinitionId = 10, InstanceId = 1, EffectiveLevel = 1, IsActive = true },
                new PlacedItem { DefinitionId = 12, InstanceId = 2, EffectiveLevel = 1, IsActive = true },
                new PlacedItem { DefinitionId = 13, InstanceId = 3, EffectiveLevel = 2, IsActive = false },
                new PlacedItem { DefinitionId = 14, InstanceId = 4, EffectiveLevel = 0, IsActive = true },
            },
            Tablets = { new PlacedTablet(), new PlacedTablet() },
        };

        var critical = CriticalCap.Base(inventory, catalog, critical: 5000, weaponCritical: 200, magicCritical: 1000, execution: 1);
        Assert.Equal(5000 + 200 - 2000 - 300, critical.Direct);
        Assert.Equal(5000 + 1000 - 2000 - 300, critical.Magic);
        Assert.False(critical.Execution);
    }
}

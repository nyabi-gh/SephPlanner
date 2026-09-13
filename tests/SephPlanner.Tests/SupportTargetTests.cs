using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 침·모래시계가 누구를 강화할지에 대한 사용자 지정. 게임은 자리만 보고 정하므로 이것은 게임
/// 규칙이 아니라 우리 배치 선택의 우선순위다.
/// </summary>
public class SupportTargetTests
{
    private static CharmDefinition Needle() => new()
    {
        MaxLevel = 2,
        Rarity = Rarity.Rare,
        Behavior = "Charm_UpCharmDamage",
        DependencyOffsetY = -1,
        DependencyBonusByLevel = { 6, 8, 10 },
        DependencyExtraByLevel = { 15, 20, 25 },
        HasDependencyCondition = true,
        DependencyMaxRarity = Rarity.Uncommon,
    };

    private static CharmSlot Attacker(int id, Rarity rarity) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition { MaxLevel = 2, IsAttackable = true, Rarity = rarity },
        Worth = new CharmWorth { Base = 5, PerLevel = 5 },
    };

    private static CharmSlot Hourglass(int id) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition
        {
            MaxLevel = 2,
            Behavior = "Charm_RightSpellCooldownHelper",
            MagicSupport = new DirectedMagicSupport { OffsetX = 1, AmountByLevel = { 60, 100, 140 } },
        },
    };

    private static CharmSlot Magic(int id, double worth) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition { MaxLevel = 1, IsMagic = true },
        Worth = new CharmWorth { Base = worth, PerLevel = worth },
    };

    private static PlacementProblem Needles()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 2, 4) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Needle() });
        problem.Charms.Add(Attacker(2, Rarity.Common));
        problem.Charms.Add(Attacker(3, Rarity.Rare));
        return problem;
    }

    private static PlacementProblem Magics()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(3, 1, 3) };
        problem.Charms.Add(Hourglass(1));
        problem.Charms.Add(Magic(2, 10));
        problem.Charms.Add(Magic(3, 4));
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.CurrentCharms[2] = new GridPos(1, 0);
        problem.CurrentCharms[3] = new GridPos(2, 0);
        return problem;
    }

    /// <summary>
    /// 세기가 같으면 침은 레어도 덤이 붙는 흔한 쪽으로 간다. 지정은 그 답을 덮는다 - 덤은
    /// 대상의 피해에 붙는 것이고 어느 대상을 실제로 키우고 있는지는 게임 데이터에 없다.
    /// </summary>
    [Fact]
    public void ANeedleEnhancesTheDesignatedTargetInsteadOfTheRarityBonusOne()
    {
        var plain = PlacementSolver.Solve(Needles());
        Assert.Equal(plain.CharmPositions[1].Offset(0, -1), plain.CharmPositions[2]);

        var problem = Needles();
        problem.Charms[2].IsSupportTarget = true;
        var solved = PlacementSolver.Solve(problem);

        Assert.Equal(solved.CharmPositions[1].Offset(0, -1), solved.CharmPositions[3]);
        Assert.Equal(1, solved.SupportTargetMatches);
        Assert.Empty(solved.UnmatchedSupportCharms);
    }

    /// <summary>
    /// 모래시계는 값어치가 큰 마법 옆에 선다. 마법서 26종은 값어치가 전부 레어도 어림값이라
    /// 그 줄 세우기가 "내가 쓰는 마법"과 무관할 수 있다.
    /// </summary>
    [Fact]
    public void AnHourglassPairsWithTheDesignatedMagicNotTheMostValuableOne()
    {
        var plain = PlacementSolver.Solve(Magics());
        Assert.Equal(plain.CharmPositions[1].Offset(1, 0), plain.CharmPositions[2]);

        var problem = Magics();
        problem.Charms[2].IsSupportTarget = true;
        var solved = PlacementSolver.Solve(problem);

        // 마법을 끌어오든 모래시계를 옮기든 상관없다. 짝이 맞는지만 본다.
        Assert.Equal(solved.CharmPositions[1].Offset(1, 0), solved.CharmPositions[3]);
        Assert.Equal(1, solved.SupportTargetMatches);
    }

    /// <summary>지정이 없으면 이 기능이 없던 때와 같은 배치와 점수여야 한다.</summary>
    [Fact]
    public void NoDesignationChangesNothing()
    {
        var solved = PlacementSolver.Solve(Magics());

        Assert.Equal(0, solved.SupportTargetMatches);
        Assert.Empty(solved.UnmatchedSupportCharms);
        Assert.Equal(solved.CharmPositions[1].Offset(1, 0), solved.CharmPositions[2]);
    }

    /// <summary>닿을 수 없으면 계획을 포기하지 않고 그 지정만 무시하며 이유를 알린다.</summary>
    [Fact]
    public void AnUnreachableDesignationIsIgnoredWithAReason()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 2, 2) };
        problem.Charms.Add(Hourglass(1));
        problem.Charms.Add(Magic(2, 10));
        problem.Charms[1].IsSupportTarget = true;

        var solved = PlacementSolver.Solve(problem);

        Assert.Equal(2, solved.CharmPositions.Count);
        Assert.Contains(1, solved.UnmatchedSupportCharms);
        Assert.Equal(0, solved.SupportTargetMatches);
        Assert.Contains("놓을 칸이 없습니다", PriorityPlacement.SupportFailureReason(problem, problem.Charms[0]));
    }

    /// <summary>모래시계 둘이 같은 마법을 지정받으면 하나만 닿는다. 나머지는 알린다.</summary>
    [Fact]
    public void TwoHelpersCannotBothReachOneDesignatedTarget()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(4, 1, 4) };
        problem.Charms.Add(Hourglass(1));
        problem.Charms.Add(Hourglass(2));
        problem.Charms.Add(Magic(3, 10));
        problem.Charms[2].IsSupportTarget = true;

        var solved = PlacementSolver.Solve(problem);

        Assert.Equal(1, solved.SupportTargetMatches);
        Assert.Single(solved.UnmatchedSupportCharms);
        Assert.Contains("닿는 배치를 찾지 못했습니다", PriorityPlacement.SupportFailureReason(problem, problem.Charms[1]));
    }

    /// <summary>이 항목이 없던 시절의 재현 자료는 "지정한 대상이 없다"로 읽는다.</summary>
    [Fact]
    public void OlderReplaysRestoreWithoutDesignations()
    {
        var preferences = new PlanPreferences { SupportTargets = { 1289, 3002 } };
        var stored = ReplayPreferences.From(preferences);
        Assert.Equal(preferences.SupportTargets, stored.Restore().SupportTargets);

        stored.SupportTargets = null;
        Assert.Empty(stored.Restore().SupportTargets);
    }
}

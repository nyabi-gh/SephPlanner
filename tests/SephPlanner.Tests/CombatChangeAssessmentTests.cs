using SephPlanner.Core.Combat;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

public sealed class CombatChangeAssessmentTests
{
    [Theory]
    [InlineData("move")]
    [InlineData("level")]
    [InlineData("disable")]
    [InlineData("remove")]
    public void UnsupportedSourceChangesAreIdentified(string change)
    {
        var problem = Problem();
        var current = At((1, 0, 0));
        var best = At((1, change == "move" ? 1 : 0, 0));
        if (change == "level") best.Levels[new(0, 0)] = 1;
        if (change == "disable") best.InactiveCharms.Add(1);
        if (change == "remove") best.CharmPositions.Clear();
        Assert.Contains("위치·레벨·활성", Assert.Single(CombatChangeAssessment.Compare(problem, current, best)));
    }

    [Fact]
    public void UnchangedUnknownItemAllowsRemoteSupportedChanges()
    {
        var problem = Problem();
        problem.Combat!.Snapshot.Unsupported.Add("무기 동작 미반영");
        problem.Charms.Add(Known(2));
        var current = At((1, 0, 0), (2, 4, 0));
        var best = At((1, 0, 0), (2, 5, 0));
        best.Levels[new(5, 0)] = 1;
        Assert.Empty(CombatChangeAssessment.Compare(problem, current, best));
    }

    [Fact]
    public void NeighborChangesAreConservativelyReported()
    {
        var problem = Problem();
        problem.Charms.Add(Known(2));
        Assert.Contains("주변 배치", Assert.Single(CombatChangeAssessment.Compare(problem,
            At((1, 0, 0), (2, 1, 0)), At((1, 0, 0), (2, 3, 0)))));
    }

    [Fact]
    public void RemoteNeedleChainLevelChangesAffectAnUnmovedUnsupportedTarget()
    {
        var problem = Problem();
        problem.Charms[0].Definition.IsAttackable = true;
        for (var id = 2; id <= 4; id++)
        {
            var needle = Known(id);
            needle.Definition.DependencyOffsetX = -1;
            needle.Definition.DependencyBonusByLevel = new() { 10, 20 };
            problem.Charms.Add(needle);
        }
        var current = At((1, 0, 0), (2, 1, 0), (3, 2, 0), (4, 3, 0));
        var best = At((1, 0, 0), (2, 1, 0), (3, 2, 0), (4, 3, 0));
        best.Levels[new(3, 0)] = 1;
        Assert.Contains("지원 연결", Assert.Single(CombatChangeAssessment.Compare(problem, current, best)));
    }

    [Fact]
    public void UnknownComboChangesAreReportedEvenWhenAllItemsAreModeled()
    {
        var key = Known(1);
        key.Definition.LineCategories = new() { "FIRE", "ICE" };
        var problem = new PlacementProblem
        {
            Grid = new(1, 2, 2),
            Combat = new(),
            Charms = { key },
            Combos = category => new ComboDefinition
            {
                Id = category,
                Combat = new() { Collected = true, Unsupported = { "미지원 콤보 발동" } }
            }
        };
        var changes = CombatChangeAssessment.Compare(problem, At((1, 0, 0)), At((1, 0, 1)));
        Assert.Equal(2, changes.Count);
        Assert.All(changes, change => Assert.Contains("콤보 수량", change));
    }

    [Fact]
    public void UnmodeledStatGrantsAreProtectedAndModeledPositionEffectsRemainAllowed()
    {
        var problem = Problem();
        var effect = problem.Charms[0].Definition.Combat;
        effect.Unsupported.Clear();
        effect.Stats.Add(new() { Key = "NEW_TRIGGER", Values = new() { 1 } });
        Assert.NotEmpty(CombatChangeAssessment.Compare(problem, At((1, 0, 0)), At((1, 4, 0))));
        effect.Stats.Clear();
        effect.FireIcePosition = true;
        Assert.Empty(CombatChangeAssessment.Compare(problem, At((1, 0, 0)), At((1, 4, 0))));
    }

    private static PlacementProblem Problem()
    {
        var unknown = Known(1);
        unknown.Definition.Combat.Unsupported.Add("미지원 발동");
        return new() { Grid = new(6, 1, 6), Combat = new(), Charms = { unknown } };
    }

    private static CharmSlot Known(int id) => new()
    {
        InstanceId = id,
        Definition = new() { EntityId = id, Id = "아이템" + id, Combat = new() { Collected = true } }
    };

    private static Arrangement At(params (int Id, int X, int Y)[] placements)
    {
        var arrangement = new Arrangement();
        foreach (var item in placements) arrangement.CharmPositions[item.Id] = new(item.X, item.Y);
        return arrangement;
    }
}

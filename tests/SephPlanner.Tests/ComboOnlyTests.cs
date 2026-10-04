using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 목표 레벨의 '콤보만' 단계. 효과 값어치를 버리고 콤보 수만 채우는 아이템은 꺼져도 잃는 것이 없으므로
/// 마음의 짐처럼 감점 칸을 채우고 좋은 칸을 넘긴다(2026-10-04 제보: 베루트의 낫이 없는 판의 단안경).
/// </summary>
public class ComboOnlyTests
{
    private static PlacementProblem NegativeAndPlain(int? cap)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 1, 2) };
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = -1 });
        foreach (var id in new[] { 1, 2 })
        {
            var definition = new CharmDefinition { EntityId = id, MaxLevel = 2, Rarity = Rarity.Rare };
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = id,
                Definition = definition,
                Worth = CharmWorth.Resolve(definition),
                LevelCap = id == 1 ? cap : null,
            });
        }
        problem.CurrentCharms[1] = new GridPos(1, 0);
        problem.CurrentCharms[2] = new GridPos(0, 0);
        return problem;
    }

    [Fact]
    public void AComboOnlyArtifactTakesTheNegativeCell()
    {
        var kept = PlacementSolver.Solve(NegativeAndPlain(cap: 0));
        Assert.Equal(new GridPos(1, 0), kept.CharmPositions[1]);

        var combo = PlacementSolver.Solve(NegativeAndPlain(cap: PlanPreferences.ComboOnly));
        Assert.Equal(new GridPos(0, 0), combo.CharmPositions[1]);
        Assert.Equal(new GridPos(1, 0), combo.CharmPositions[2]);
        Assert.Empty(combo.UnpreservedCharms);
    }

    [Fact]
    public void AComboOnlyKeyStillScoresItsRowWhenSwitchedOff()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 1, 1) };
        problem.FixedEffects.Add(new FixedEffectCell { Position = new GridPos(0, 0), Level = -1 });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { MaxLevel = 3, Behavior = "Charm_3Elemental_ByRow", LineCategories = { "GLACIER" } },
            LevelCap = PlanPreferences.ComboOnly,
        });
        problem.CurrentCharms[1] = new GridPos(0, 0);
        problem.ComboCounts = new Dictionary<string, int> { ["GLACIER"] = 2 };
        problem.Combos = id => new ComboDefinition { Id = id, Thresholds = { 2 } };

        var score = PlacementSolver.Score(problem, new List<TabletPlacement>(), problem.CurrentCharms);
        Assert.Contains(1, score.InactiveCharms);
        Assert.Equal(problem.Scale.ComboThreshold, score.Score, 6);
    }
}

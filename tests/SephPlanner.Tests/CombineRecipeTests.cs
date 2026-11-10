using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Tests;

/// <summary>
/// 게임 1.1.2 의 합치기형 배치 보너스(날개 + 천 갑옷 → 사과주스)는 재료가 상하좌우로 이어지면 둘 다 지운다. 배치
/// 추천이 그렇게 붙이면 되돌릴 수 없다.
/// </summary>
public class CombineRecipeTests
{
    private const int Tablet = 100, Wing = 1143, Cloth = 1215;

    private static Catalog Catalog() => new(
        new[] { new TabletDefinition { Id = "T", EntityId = Tablet, Query = "RIGHT 2" } },
        new[]
        {
            new CharmDefinition { Id = "Wing", EntityId = Wing, MaxLevel = 5 },
            new CharmDefinition { Id = "Cloth", EntityId = Cloth, MaxLevel = 5 },
        });

    private static GameSnapshot Snapshot(List<List<int>>? recipes, GridPos cloth)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 12 };
        inventory.Tablets.Add(new PlacedTablet { DefinitionId = Tablet, InstanceId = 1, Position = new GridPos(0, 0), IsApplied = true });
        inventory.Items.Add(new PlacedItem { DefinitionId = Wing, InstanceId = 10, Position = new GridPos(1, 0), EffectiveLevel = 2, IsActive = true });
        inventory.Items.Add(new PlacedItem { DefinitionId = Cloth, InstanceId = 11, Position = cloth, EffectiveLevel = 0, IsActive = true });
        inventory.LevelMatrix["1,0"] = 2;
        return new GameSnapshot { Inventory = inventory, Run = new RunState { CombineRecipes = recipes } };
    }

    [Fact]
    public void TheSuggestedPlacementKeepsTheMaterialsApart()
    {
        var plan = PlanBuilder.Build(Snapshot(new List<List<int>> { new() { Wing, Cloth } }, new GridPos(2, 0)), Catalog())!;

        Assert.NotEmpty(plan.Current.CombinedCharms);
        Assert.Empty(plan.Best.CombinedCharms);
        var wing = plan.Best.CharmPositions[10];
        var cloth = plan.Best.CharmPositions[11];
        Assert.NotEqual(1, Math.Abs(wing.X - cloth.X) + Math.Abs(wing.Y - cloth.Y));
        Assert.Single(plan.RetentionWarnings);
    }

    /// <summary>
    /// 떼어 둘 자리가 없어 추천도 붙은 채면 적용하지 않고, 사유는 끄기 허용이 아니라 합쳐짐으로 댄다.
    /// </summary>
    [Fact]
    public void MaterialsThatCannotBeKeptApartBlockTheSuggestionForThatReason()
    {
        var inventory = new InventoryState { Width = 2, Height = 1, Storage = 2 };
        inventory.Items.Add(new PlacedItem { DefinitionId = Wing, InstanceId = 10, Position = new GridPos(0, 0), IsActive = true });
        inventory.Items.Add(new PlacedItem { DefinitionId = Cloth, InstanceId = 11, Position = new GridPos(1, 0), IsActive = true });
        var snapshot = new GameSnapshot
        {
            Inventory = inventory,
            Run = new RunState { CombineRecipes = new List<List<int>> { new() { Wing, Cloth } } },
        };

        var plan = PlanBuilder.Build(snapshot, Catalog())!;

        Assert.NotEmpty(plan.Best.CombinedCharms);
        Assert.False(plan.HasUnapprovedDeactivation);
        Assert.Empty(plan.Targets);
        Assert.Contains(plan.RetentionWarnings, warning => warning.Contains("떼어 둘 자리를 찾지 못해"));
        Assert.DoesNotContain(plan.RetentionWarnings, warning => warning.Contains("추천 배치는 떼어 둡니다"));
    }

    [Fact]
    public void DiagonalMaterialsDoNotCombine()
    {
        var plan = PlanBuilder.Build(Snapshot(new List<List<int>> { new() { Wing, Cloth } }, new GridPos(2, 1)), Catalog())!;

        Assert.Empty(plan.Current.CombinedCharms);
    }

    /// <summary>능력치가 없으면 게임이 레시피를 찾지 않으므로 붙여도 된다.</summary>
    [Fact]
    public void WithoutTheStatNothingCombines()
    {
        var plan = PlanBuilder.Build(Snapshot(null, new GridPos(2, 0)), Catalog())!;

        Assert.Empty(plan.Current.CombinedCharms);
        Assert.Empty(plan.RetentionWarnings);
    }

    [Fact]
    public void TheRecipesEnterTheFingerprint()
    {
        var at = new GridPos(3, 0);
        var plain = PlanFingerprint.Placement(Snapshot(null, at), "");
        Assert.Equal(plain, PlanFingerprint.Placement(Snapshot(new List<List<int>>(), at), ""));
        Assert.NotEqual(plain, PlanFingerprint.Placement(Snapshot(new List<List<int>> { new() { Wing, Cloth } }, at), ""));
    }

    /// <summary>후보 조언은 판을 복제해 푼다. 레시피를 흘리면 후보를 놓는 자리가 재료를 붙인다.</summary>
    [Fact]
    public void AnOfferTrialKeepsTheRecipes()
    {
        var problem = new PlacementProblem { CombineRecipes = { new List<int> { Wing, Cloth } } };

        Assert.Same(problem.CombineRecipes, OfferAdvisor.Clone(problem).CombineRecipes);
    }
}

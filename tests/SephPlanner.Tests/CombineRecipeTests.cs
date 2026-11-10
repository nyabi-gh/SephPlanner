using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

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
}

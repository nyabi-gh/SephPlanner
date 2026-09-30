using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

/// <summary>
/// 합성 석판의 질의는 게임이 인스턴스마다 따로 들고 있어, 그 표를 잃으면 효과가 사라진다
/// (제보 34a32e60). 계산은 게임을 따르고 사용자에게는 알린다.
/// </summary>
public class MixedTabletQueryTests
{
    private const int Mixed = 2101;

    private static Plan Build(params (string? Query, string Name, int X)[] tablets)
    {
        var inventory = new InventoryState { Width = 6, Height = 1, Storage = 6 };
        inventory.Items.Add(new PlacedItem { DefinitionId = 200, InstanceId = 10, Position = new GridPos(5, 0), IsActive = true });
        for (var i = 0; i < tablets.Length; i++)
        {
            inventory.Tablets.Add(new PlacedTablet
            {
                DefinitionId = Mixed,
                InstanceId = 20 + i,
                Position = new GridPos(tablets[i].X, 0),
                Query = tablets[i].Query,
                Name = tablets[i].Name,
                IsApplied = true,
            });
        }

        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "Mixed", EntityId = Mixed, IsCustom = true } },
            new[] { new CharmDefinition { Id = "C", EntityId = 200, MaxLevel = 5 } });
        return PlanBuilder.Build(new GameSnapshot { Inventory = inventory }, catalog)!;
    }

    [Fact]
    public void MixedTabletsTheGameForgotAreNamed()
    {
        var plan = Build(("", "융합", 0), ("", "융합", 1), ("", "축적", 2), ("RIGHT 1", "합작", 3));

        var warning = Assert.Single(plan.TabletWarnings);
        Assert.Equal("합성 석판 3개(융합 2, 축적)의 효과가 게임에 남아 있지 않아 아무 칸에도 영향을 주지 않습니다.", warning);
        Assert.Contains("경고/석판", ReplayResult.From(plan).Facts.Keys);
    }

    [Fact]
    public void MixedTabletsWithTheirQueryAreNotWarned()
    {
        var plan = Build(("RIGHT 1", "융합", 0));

        Assert.Empty(plan.TabletWarnings);
        Assert.DoesNotContain("경고/석판", ReplayResult.From(plan).Facts.Keys);
    }
}

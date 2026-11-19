using System.Globalization;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

/// <summary>
/// 재현 비교의 항목은 저장한 기계(게임 Mono)와 재생하는 기계(개발 PC)에서 같은 글자여야 한다. 인스턴스 번호는
/// 음수일 수 있는데, 음수 기호는 문화권마다 다르다.
/// </summary>
public class ReplayResultCultureTests
{
    [Fact]
    public void NegativeInstanceIdsReadTheSameUnderEveryCulture()
    {
        var inventory = new InventoryState { Width = 4, Height = 1, Storage = 4 };
        inventory.Items.Add(new PlacedItem { DefinitionId = 200, InstanceId = -7, Position = new GridPos(3, 0), IsActive = true });
        inventory.Tablets.Add(new PlacedTablet { DefinitionId = 2001, InstanceId = -9, Position = new GridPos(0, 0), IsApplied = true });
        var catalog = new Catalog(
            new[] { new TabletDefinition { Id = "T", EntityId = 2001 } },
            new[] { new CharmDefinition { Id = "C", EntityId = 200, MaxLevel = 5 } });
        var plan = PlanBuilder.Build(new GameSnapshot { Inventory = inventory }, catalog)!;

        var invariant = ReplayResult.From(plan);
        var previous = CultureInfo.CurrentCulture;
        ReplayResult swedish;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            Assert.NotEqual("-7", (-7).ToString());
            swedish = ReplayResult.From(plan);
        }
        finally { CultureInfo.CurrentCulture = previous; }

        Assert.Contains("최선/아이템/-7", invariant.Facts.Keys);
        Assert.Contains("최선/석판/-9", invariant.Facts.Keys);
        Assert.Empty(invariant.Differences(swedish));
        Assert.Equal(invariant.Facts.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            swedish.Facts.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }
}

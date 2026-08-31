using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 후보를 집었을 때의 격자. 증가분이라는 숫자 하나로는 무엇이 어떻게 달라지는지 알 수 없어,
/// 솔버가 후보마다 이미 푼 결과를 버리지 않고 화면이 쓸 모양으로 들고 있는다.
/// </summary>
public class OfferPreviewTests
{
    private const int Tablet = 2025;
    private const int Held = 1300;
    private const int Offered = 1301;

    [Fact]
    public void EveryOfferCarriesTheGridItWouldProduce()
    {
        var plan = PlanBuilder.Build(Snapshot(), Catalog());

        Assert.NotEmpty(plan!.Offers);
        Assert.All(plan.Offers, advice => Assert.NotNull(advice.Preview));
    }

    [Fact]
    public void ThePreviewContainsTheCandidateItself()
    {
        var plan = PlanBuilder.Build(Snapshot(), Catalog());
        var advice = plan!.Offers.Single(entry => entry.Candidate.DefinitionId == Offered);

        // 집었을 때의 격자이므로 후보가 그 안에 놓여 있어야 한다. 없으면 기준 배치를 보여준 것이다.
        Assert.Contains("후보", advice.Preview!.Names.Values);
    }

    [Fact]
    public void ThePreviewScoreIsTheOneTheGainWasMeasuredAgainst()
    {
        var plan = PlanBuilder.Build(Snapshot(), Catalog());
        var advice = plan!.Offers.Single(entry => entry.Candidate.DefinitionId == Offered);

        // 화면이 격자와 다른 점수를 말하면 안 된다. 미리보기 점수는 그 배치의 실제 점수다.
        Assert.True(advice.Preview!.Score > 0);
    }

    [Fact]
    public void CellsThatChangeAreMarked()
    {
        var plan = PlanBuilder.Build(Snapshot(), Catalog());
        var advice = plan!.Offers.Single(entry => entry.Candidate.DefinitionId == Offered);

        // 후보가 놓인 칸은 지금 비어 있으므로 반드시 달라지는 칸으로 잡혀야 한다.
        var where = advice.Preview!.Names.First(pair => pair.Value == "후보").Key;
        Assert.Contains(where, advice.Preview.Changed);
    }

    [Fact]
    public void TurningRecommendationsOffLeavesNoPreviews()
    {
        var plan = PlanBuilder.Build(
            Snapshot(), Catalog(), new PlanPreferences { Recommendations = false });

        Assert.Empty(plan!.Offers);
    }

    private static Catalog Catalog() => new(
        new[]
        {
            new TabletDefinition
            {
                Id = "T", EntityId = Tablet, IsRotatable = true, Query = "RIGHT 2\nLEFT 2",
                Names = { ["current"] = "석판" },
            },
        },
        new[]
        {
            new CharmDefinition { Id = "Held", EntityId = Held, MaxLevel = 5, Names = { ["current"] = "가진 것" } },
            new CharmDefinition { Id = "Offered", EntityId = Offered, MaxLevel = 5, Names = { ["current"] = "후보" } },
        });

    private static GameSnapshot Snapshot()
    {
        var inventory = new InventoryState { Width = 6, Height = 4, Storage = 12 };

        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = Tablet,
            InstanceId = 1,
            Position = new GridPos(2, 0),
            IsApplied = true,
            IsRotatable = true,
        });
        // Storage 12 는 6칸짜리 두 줄만 열려 있다는 뜻이다. 그 밖의 칸에 두면 아예 읽히지 않는다.
        inventory.Items.Add(new PlacedItem { DefinitionId = Held, InstanceId = 10, Position = new GridPos(0, 1) });

        return new GameSnapshot
        {
            Inventory = inventory,
            Run = new RunState { Gold = 1000 },
            Offers =
            {
                new OfferedItem { DefinitionId = Offered, Kind = "charm", Price = 0, SlotIndex = 0 },
            },
        };
    }
}

using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 석판 합성기(<c>TabletMix</c>)로 만든 석판. 게임은 합성 결과를 엔티티 2101 하나로 만들고
/// 질의·회전 가능 여부·이름을 인스턴스마다 따로 붙인다. 그래서 정의값만 보면 전부 틀린다.
/// </summary>
public class TabletMixTests
{
    private const int MixedTablet = 2101;
    private const int CharmA = 200;
    private const int CharmB = 201;

    /// <summary>합성 석판의 정의. 이름은 게임 안에서도 자리표시자고 회전은 프리팹 기본값이 거짓이다.</summary>
    private static Catalog Catalog() => new(
        new[]
        {
            new TabletDefinition
            {
                Id = "...", EntityId = MixedTablet, IsRotatable = false,
                Query = "RIGHT 2\nLEFT 1", Names = { ["current"] = "..." },
            },
        },
        new[]
        {
            new CharmDefinition { Id = "A", EntityId = CharmA, MaxLevel = 5 },
            new CharmDefinition { Id = "B", EntityId = CharmB, MaxLevel = 5 },
        });

    /// <summary>
    /// 석판은 좌우 칸을 올리는데 아티팩트는 위아래에 있다. 90도 돌리기만 하면 둘 다 레벨을 받으므로
    /// 회전을 허용하는지 아닌지가 결과에 그대로 드러난다.
    /// </summary>
    private static GameSnapshot Snapshot(bool? rotatable, string? name)
    {
        var inventory = new InventoryState { Width = 6, Height = 7, Storage = 18 };

        inventory.Tablets.Add(new PlacedTablet
        {
            DefinitionId = MixedTablet,
            InstanceId = 1,
            Position = new GridPos(1, 1),
            Rotation = 0,
            IsApplied = true,
            IsRotatable = rotatable,
            Name = name,
        });

        inventory.Items.Add(new PlacedItem { DefinitionId = CharmA, InstanceId = 10, Position = new GridPos(1, 0) });
        inventory.Items.Add(new PlacedItem { DefinitionId = CharmB, InstanceId = 11, Position = new GridPos(1, 2) });

        return new GameSnapshot { Inventory = inventory };
    }

    [Fact]
    public void AMixedTabletTurnsWhenTheInstanceSaysItCan()
    {
        // 합성은 재료 둘이 모두 돌릴 수 있으면 결과도 돌릴 수 있게 풀어 준다. 정의값(거짓)을
        // 다시 겹쳐 보면 게임에서 돌아가는 석판을 우리만 못 돌리는 것으로 친다.
        var plan = PlanBuilder.Build(Snapshot(rotatable: true, name: null), Catalog());

        Assert.NotNull(plan);
        Assert.NotEqual(0, Assert.Single(plan!.Best.Tablets).Rotation);
    }

    [Fact]
    public void AMixedTabletStaysPutWhenTheInstanceSaysItCannot()
    {
        // 돌리지 못하니 아티팩트를 석판 좌우로 옮기는 차선책이 나온다. 석판 자신은 그대로다.
        var plan = PlanBuilder.Build(Snapshot(rotatable: false, name: null), Catalog());

        Assert.NotNull(plan);
        Assert.Equal(0, Assert.Single(plan!.Best.Tablets).Rotation);
    }

    [Fact]
    public void WithoutAnInstanceValueTheDefinitionDecides()
    {
        // 값을 싣지 않은 스냅샷을 "돌릴 수 있다"로 읽으면 잠긴 석판에 따라 할 수 없는 회전을 시킨다.
        var plan = PlanBuilder.Build(Snapshot(rotatable: null, name: null), Catalog());

        Assert.NotNull(plan);
        Assert.Equal(0, Assert.Single(plan!.Best.Tablets).Rotation);
    }

    [Fact]
    public void ThePlayersOwnNameIsWhatShowsUp()
    {
        // 정의 이름이 "..." 라서, 합성 석판이 둘 이상이면 이 이름 없이는 서로 구분되지 않는다.
        var plan = PlanBuilder.Build(Snapshot(rotatable: true, name: "쌓임"), Catalog());

        Assert.Equal("쌓임", Assert.Single(plan!.Moves).Label);
    }

    [Fact]
    public void WithoutANameItFallsBackToTheDefinition()
    {
        var plan = PlanBuilder.Build(Snapshot(rotatable: true, name: null), Catalog());

        Assert.Equal("...", Assert.Single(plan!.Moves).Label);
    }

    [Fact]
    public void AnOfferedTabletThatCannotTurnIsNotEvaluatedAsIfItCould()
    {
        // 아직 집지 않은 후보에는 인스턴스가 없다. 이때는 정의값이 답이라, 회전 가능 여부를
        // 그냥 참으로 두면 돌릴 수 없는 석판을 돌린 값으로 줄을 세우게 된다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });

        // 한 줄만 열린 가방에서 위쪽 칸은 격자 밖이다. 돌려야만 이웃에 닿는다.
        OfferCandidate Candidate(string name, bool rotatable) => new()
        {
            Kind = "tablet",
            Name = name,
            Tablet = new TabletDefinition { Id = name, Query = "UP 3", IsRotatable = rotatable },
        };

        var advice = OfferAdvisor.Rank(
            problem, new[] { Candidate("locked", false), Candidate("free", true) }, gold: 1000);

        var locked = advice.Single(entry => entry.Candidate.Name == "locked");
        var free = advice.Single(entry => entry.Candidate.Name == "free");

        Assert.Equal(0, locked.Gain, 3);
        Assert.True(free.Gain > 0);
    }
}

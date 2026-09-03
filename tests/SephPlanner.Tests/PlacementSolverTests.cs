using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class PlacementSolverTests
{
    /// <summary>한 줄만 열린 작은 가방. 탐색이 금방 끝나면서도 배치의 우열은 갈린다.</summary>
    private static GridSpec OneRow => new(6, 7, 6);

    private static TabletSlot Tablet(int instanceId, string query) => new()
    {
        InstanceId = instanceId,
        Definition = new TabletDefinition { Id = "T" + instanceId, Query = query },
    };

    private static CharmSlot Charm(int instanceId, int maxLevel = 5) => new()
    {
        InstanceId = instanceId,
        Definition = new CharmDefinition { Id = "C" + instanceId, MaxLevel = maxLevel },
    };

    [Fact]
    public void CharmWhoseWeaponIsNotEquippedAddsNothing()
    {
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 2"));
        problem.Charms.Add(Charm(10));

        var withCharmOn = PlacementSolver.Solve(problem).Score;

        problem.Charms[0].IsDormant = true;
        var withCharmOff = PlacementSolver.Solve(problem).Score;

        // 켜진 아티팩트 하나(1)에 석판이 준 레벨 2를 더해 3이다. 꺼지면 아무것도 남지 않는다.
        Assert.Equal(3, withCharmOn, 3);
        Assert.Equal(0, withCharmOff, 3);
    }

    [Fact]
    public void ADormantCharmSaysItIsTheWeaponEvenOnADisabledCell()
    {
        // 어디로 옮겨도 켜지지 않는 이유를 먼저 알려야 한다.
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT X"));
        problem.Charms.Add(Charm(10));
        problem.Charms[0].IsDormant = true;

        var arrangement = PlacementSolver.Solve(problem);

        var position = arrangement.CharmPositions[10];
        Assert.Equal(CharmInactiveReason.Weapon, arrangement.InactiveCells[position]);
    }

    [Fact]
    public void LevelAboveTheCharmsCapIsNotWorthAnything()
    {
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 5"));
        problem.Charms.Add(Charm(10, maxLevel: 2));

        var arrangement = PlacementSolver.Solve(problem);

        // 상한이 2 라 레벨 5 중 2까지만 값이 된다. 켜진 몫 1 을 더해 3이다.
        Assert.Equal(3, arrangement.Score, 2);
        Assert.Equal(2, arrangement.EffectiveLevels[arrangement.CharmPositions[10]]);
    }

    [Fact]
    public void ACharmIsMovedOutOfACellThatWouldTurnItOff()
    {
        // 레벨 0 은 살아 있고 -1 은 꺼진다. 둘 다 점수에 보태는 레벨이 없다고 해서 같은 자리가
        // 아니다. 켜지는 칸으로 옮기라고 해야 한다.
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT -1"));
        problem.Charms.Add(Charm(10));

        problem.CurrentTablets[1] = new TabletSpot(new GridPos(0, 0), 0);
        problem.CurrentCharms[10] = new GridPos(1, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.NotEqual(new GridPos(1, 0), arrangement.CharmPositions[10]);
        Assert.Equal(1, arrangement.Score, 3);
        Assert.Empty(arrangement.InactiveCharms);
    }

    [Fact]
    public void AnAlreadyOptimalLayoutIsLeftAlone()
    {
        // 점수가 같은 배치가 여럿일 때 옮기라고 하면, 아무것도 달라지지 않았는데 제안이 계속 바뀐다.
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 2"));
        problem.Charms.Add(Charm(10));

        // 그냥 두면 솔버는 격자 앞쪽부터 채운다. 점수가 같은데도 옮기라고 하는지 보려면
        // 현재 배치를 일부러 뒤쪽에 둬야 한다.
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(3, 0), 0);
        problem.CurrentCharms[10] = new GridPos(4, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(3, 0), arrangement.Tablets[0].Position);
        Assert.Equal(new GridPos(4, 0), arrangement.CharmPositions[10]);
    }

    [Fact]
    public void TheScoreDoesNotDependOnTheOrderTheItemsArriveIn()
    {
        // 게임이 넘겨주는 순서는 물건을 옮기면 달라진다. 그것 때문에 제안이 흔들려서는 안 된다.
        var forward = Problem(reversed: false);
        var backward = Problem(reversed: true);

        Assert.Equal(PlacementSolver.Solve(forward).Score, PlacementSolver.Solve(backward).Score, 6);

        static PlacementProblem Problem(bool reversed)
        {
            var tablets = new List<TabletSlot> { Tablet(1, "RIGHT 2"), Tablet(2, "LEFT 1") };
            var charms = new List<CharmSlot> { Charm(10), Charm(11, maxLevel: 1), Charm(12) };
            if (reversed)
            {
                tablets.Reverse();
                charms.Reverse();
            }

            var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 12) };
            problem.Tablets.AddRange(tablets);
            problem.Charms.AddRange(charms);
            return problem;
        }
    }

    [Fact]
    public void EnchantIsAddedBeforeTheMultiplierNotAfter()
    {
        // 게임은 석판 몫과 인챈트를 더한 뒤에 배수 행렬을 곱한다. 순서를 뒤집으면 값이 달라진다.
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 2\nRIGHT MUL/2"));
        problem.Charms.Add(Charm(10, maxLevel: 10));
        problem.Charms[0].Enchant = 1;

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(6, arrangement.EffectiveLevels[arrangement.CharmPositions[10]]);
    }

    [Fact]
    public void ItemsThatOnlyTakeUpSpaceAreStillPlaced()
    {
        // 소비 아이템을 빼놓으면 솔버가 이미 찬 자리를 비었다고 보고 거기로 옮기라고 한다.
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 2"));
        problem.Charms.Add(Charm(10));
        problem.Charms.Add(new CharmSlot { InstanceId = 11, IsFiller = true });

        var arrangement = PlacementSolver.Solve(problem);

        Assert.True(arrangement.CharmPositions.ContainsKey(11));
        Assert.NotEqual(arrangement.CharmPositions[10], arrangement.CharmPositions[11]);
    }

    [Fact]
    public void ATabletWithoutRoomIsReportedNotSilentlyDropped()
    {
        // 열린 칸이 2개뿐인데 석판이 3개다. 하나는 놓을 수 없고, 그 사실이 점수와 함께
        // 조용히 사라지면 존재하는 석판을 무시한 배치를 최적이라고 말하게 된다.
        var problem = new PlacementProblem { Grid = new GridSpec(2, 1, 2) };
        problem.Tablets.Add(Tablet(1, "RIGHT 1"));
        problem.Tablets.Add(Tablet(2, "RIGHT 1"));
        problem.Tablets.Add(Tablet(3, "RIGHT 1"));

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(1, arrangement.UnplacedTablets);
        Assert.Equal(2, arrangement.Tablets.Count);
    }

    [Fact]
    public void ACompleteLayoutIsNeverTradedForAnIncompleteOne()
    {
        // 모든 석판이 실제로 격자에 있는 평범한 상태. 빠진 석판이 있다고 보고되면 안 된다.
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 2"));
        problem.Tablets.Add(Tablet(2, "LEFT 1"));
        problem.Charms.Add(Charm(10));

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(0, arrangement.UnplacedTablets);
        Assert.Equal(2, arrangement.Tablets.Count);
    }

    /// <summary>
    /// 직전 계획은 그때의 상황에서 나온 것이다. 그 사이 회전이 잠겼으면(저주) 계획의 회전 지시를
    /// 그대로 되살리면 안 된다 - 계획이 스스로를 되먹여 불가능한 지시가 영영 남는다.
    /// </summary>
    [Fact]
    public void APlannedRotationIsDroppedOnceTheTabletIsLocked()
    {
        var problem = new PlacementProblem { Grid = OneRow };
        var tablet = Tablet(1, "RIGHT 3");
        tablet.Rotatable = false;
        problem.Tablets.Add(tablet);
        problem.Charms.Add(Charm(10));

        // 맨 오른쪽에서 RIGHT 는 격자 밖이라 회전 2(LEFT)가 이득인데, 이제는 돌릴 수 없다.
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(5, 0), 0);
        problem.CurrentCharms[10] = new GridPos(4, 0);
        problem.PlannedTablets[1] = new TabletSpot(new GridPos(5, 0), 2);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(0, arrangement.Tablets[0].Rotation);
    }

    /// <summary>가방이 줄어 계획된 칸이 닫혔으면 그 칸을 다시 제안하지 않는다.</summary>
    [Fact]
    public void APlannedCellOutsideTheOpenGridIsDropped()
    {
        var problem = new PlacementProblem { Grid = OneRow };
        problem.Tablets.Add(Tablet(1, "RIGHT 3"));
        problem.Charms.Add(Charm(10));
        problem.CurrentTablets[1] = new TabletSpot(new GridPos(0, 0), 0);
        problem.CurrentCharms[10] = new GridPos(1, 0);

        // 두 번째 줄은 열려 있지 않다.
        problem.PlannedTablets[1] = new TabletSpot(new GridPos(3, 1), 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(0, arrangement.Tablets[0].Position.Y);
    }
}

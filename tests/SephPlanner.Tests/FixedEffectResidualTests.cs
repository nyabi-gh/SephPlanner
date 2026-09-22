using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 참가자 자리에서는 고정 각인 목록을 읽을 길이 없어 게임 행렬에서 되뺀다.
/// 되뺀 값은 언제나 행렬과 맞으므로, 진짜 고정 효과인지는 성질로 가른다.
/// </summary>
public class FixedEffectResidualTests
{
    private static GridSpec Grid => new(6, 4, 12);

    private static SimulationResult Observed(params FixedEffectCell[] cells) =>
        TabletSimulator.Run(new List<TabletPlacement>(), new GridOccupancy(), Grid, cells);

    private static GameEffectMatrices Game(
        Dictionary<GridPos, int> level,
        Dictionary<GridPos, int>? multiply = null,
        Dictionary<GridPos, int>? disable = null,
        Dictionary<GridPos, int>? ignoreCriteria = null,
        Dictionary<GridPos, int>? enchant = null)
    {
        static Func<GridPos, int> Read(Dictionary<GridPos, int>? source) =>
            position => source != null && source.TryGetValue(position, out var value) ? value : 0;

        return new GameEffectMatrices(
            Read(level), Read(multiply), Read(disable), Read(ignoreCriteria), Read(enchant));
    }

    [Fact]
    public void WhatTheTabletsDoNotExplainBecomesTheFixedLayer()
    {
        var observed = Observed(new FixedEffectCell { Position = new GridPos(0, 0), Level = 2 });
        var game = Game(new Dictionary<GridPos, int> { [new GridPos(0, 0)] = 3 });

        var residual = FixedEffectResidual.Extract(Grid, observed, game);

        Assert.Equal(FixedEffectResidualStatus.Extracted, residual.Status);
        var cell = Assert.Single(residual.Cells);
        Assert.Equal(new GridPos(0, 0), cell.Position);
        Assert.Equal(1, cell.Level);
    }

    [Fact]
    public void EnchantIsNotMistakenForAFixedEffect()
    {
        var game = Game(
            new Dictionary<GridPos, int> { [new GridPos(1, 0)] = 2 },
            enchant: new Dictionary<GridPos, int> { [new GridPos(1, 0)] = 2 });

        var residual = FixedEffectResidual.Extract(Grid, Observed(), game);

        Assert.Equal(FixedEffectResidualStatus.Extracted, residual.Status);
        Assert.Empty(residual.Cells);
    }

    [Fact]
    public void TheMultiplierIsDividedOutBeforeSubtracting()
    {
        // 게임은 곱한 값을 담아 둔다((석판 1 + 고정 1 + 인챈트 1) × 2 = 6).
        var observed = Observed(new FixedEffectCell { Position = new GridPos(2, 0), Level = 1, Multiply = 2 });
        var game = Game(
            new Dictionary<GridPos, int> { [new GridPos(2, 0)] = 6 },
            multiply: new Dictionary<GridPos, int> { [new GridPos(2, 0)] = 2 },
            enchant: new Dictionary<GridPos, int> { [new GridPos(2, 0)] = 1 });

        var residual = FixedEffectResidual.Extract(Grid, observed, game);

        var cell = Assert.Single(residual.Cells);
        Assert.Equal(1, cell.Level);
        Assert.Equal(0, cell.Multiply);
    }

    [Fact]
    public void AMatrixCaughtMidTransactionIsNotUsed()
    {
        var game = Game(
            new Dictionary<GridPos, int> { [new GridPos(0, 0)] = 3 },
            multiply: new Dictionary<GridPos, int> { [new GridPos(0, 0)] = 2 });

        var residual = FixedEffectResidual.Extract(Grid, Observed(), game);

        Assert.Equal(FixedEffectResidualStatus.Unsettled, residual.Status);
        Assert.Empty(residual.Cells);
    }

    [Fact]
    public void EffectsWeInventedAreNotHiddenInTheLayer()
    {
        var observed = Observed(new FixedEffectCell { Position = new GridPos(0, 0), Disable = 1 });

        var residual = FixedEffectResidual.Extract(Grid, observed, Game(new Dictionary<GridPos, int>()));

        Assert.Equal(FixedEffectResidualStatus.Overproduced, residual.Status);
        Assert.Contains("(0,0)", residual.Reason);
    }

    [Fact]
    public void TheLayerReproducesTheGameMatrix()
    {
        var tablet = new TabletPlacement
        {
            Definition = new TabletDefinition { Id = "t", Query = "RIGHT 2" },
            Position = new GridPos(0, 0),
        };
        var placements = new List<TabletPlacement> { tablet };
        var observed = TabletSimulator.Run(placements, new GridOccupancy(), Grid);
        var game = Game(new Dictionary<GridPos, int>
        {
            [new GridPos(1, 0)] = observed.LevelAt(new GridPos(1, 0)) + 3,
            [new GridPos(4, 0)] = 1,
        });

        var residual = FixedEffectResidual.Extract(Grid, observed, game);
        var replayed = TabletSimulator.Run(placements, new GridOccupancy(), Grid, residual.Cells);

        for (var index = 0; index < Grid.Storage; index++)
        {
            var position = Grid.ToPosition(index);
            Assert.Equal(game.Level(position), replayed.EffectiveLevel(position, game.Enchant(position)));
        }
    }

    [Fact]
    public void OutsideTheOpenedCellsIsNotExtracted()
    {
        // 잠긴 칸의 잔여값은 게임도 쓰지 않는다. 열린 칸까지만 본다.
        var game = Game(new Dictionary<GridPos, int> { [new GridPos(5, 3)] = 4 });

        var residual = FixedEffectResidual.Extract(Grid, Observed(), game);

        Assert.Empty(residual.Cells);
    }
}

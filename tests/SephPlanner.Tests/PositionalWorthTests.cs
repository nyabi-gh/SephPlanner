using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 자리가 대상을 정하는 아티팩트들. 규칙은 전부 게임 클래스에서 옮긴 것이라, 여기 고정한 것은
/// "우리 점수 모델이 이렇다"가 아니라 "게임이 이렇게 판정한다"이다.
/// </summary>
public class PositionalWorthTests
{
    private static CharmDefinition Needle(int dx = 0, int dy = -1) => new()
    {
        MaxLevel = 5,
        Behavior = "Charm_UpCharmDamage",
        DependencyOffsetX = dx,
        DependencyOffsetY = dy,
        DependencyBonusByLevel = { 5, 10, 15, 20, 30 },
    };

    private static CharmDefinition Attacker(params string[] categories) => new()
    {
        MaxLevel = 5,
        IsAttackable = true,
        Categories = new List<string>(categories),
    };

    /// <summary>능력치도 공격도 없는 아티팩트. 침의 대상이 되지 못한다.</summary>
    private static CharmDefinition Bystander() => new() { MaxLevel = 5 };

    /// <summary>
    /// 1x2 세로 격자에 침과 대상 하나씩. 침이 어느 칸을 고르는지만 본다 -
    /// 위 칸에 서면 오프셋이 격자 밖을 가리켜 대상이 없다.
    /// </summary>
    [Fact]
    public void ANeedleStandsUnderWhatItCanEnhance()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 2, 2) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Needle() });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Attacker() });

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(0, 1), arrangement.CharmPositions[1]);
        Assert.Equal(new GridPos(0, 0), arrangement.CharmPositions[2]);
    }

    /// <summary>
    /// 게임 <c>IsDependencyValid</c>는 공격하는 아티팩트(<c>IAttackableCharm</c>)나 다른 침만
    /// 대상으로 본다. 그것이 아니면 침은 어디에 서든 값어치가 0 이라, 대상이 될 수 없는 것을
    /// 위에 두고도 이득이 없다.
    /// </summary>
    [Fact]
    public void ANeedleGainsNothingFromAnUnattackableNeighbour()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 2, 2) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Needle() });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Bystander() });

        var withTarget = new PlacementProblem { Grid = new GridSpec(1, 2, 2) };
        withTarget.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Needle() });
        withTarget.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Attacker() });

        Assert.True(PlacementSolver.Solve(withTarget).Score > PlacementSolver.Solve(problem).Score);
    }

    /// <summary>
    /// 게임 <c>SearchCategory</c>는 침 위에 침이 있으면 계속 올라가 마지막 아티팩트를 대상으로
    /// 삼는다. 그래서 두 침을 한 아티팩트 아래 쌓는 것이 답이다.
    /// </summary>
    [Fact]
    public void NeedlesChainUpwardToTheSameTarget()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 3, 3) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Needle() });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Needle() });
        problem.Charms.Add(new CharmSlot { InstanceId = 3, Definition = Attacker() });

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(new GridPos(0, 0), arrangement.CharmPositions[3]);
        Assert.NotEqual(new GridPos(0, 0), arrangement.CharmPositions[1]);
        Assert.NotEqual(new GridPos(0, 0), arrangement.CharmPositions[2]);
    }

    /// <summary>
    /// 침은 대상의 카테고리를 물려받아 콤보 개수에 보탠다. 그래서 같은 값어치의 대상이라면
    /// 콤보가 걸린 쪽 아래에 서는 것이 낫다.
    /// </summary>
    [Fact]
    public void ANeedleInheritsTheTargetsCategory()
    {
        PlacementProblem Build(bool withCombos)
        {
            var problem = new PlacementProblem { Grid = new GridSpec(1, 2, 2) };
            problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = Needle() });
            problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Attacker("EMBER") });

            if (withCombos)
            {
                problem.ComboCounts = new Dictionary<string, int> { ["EMBER"] = 1 };
                problem.Combos = id => id == "EMBER"
                    ? new ComboDefinition { Id = "EMBER", Thresholds = { 2, 5 } }
                    : null;
            }
            return problem;
        }

        var with = PlacementSolver.Solve(Build(withCombos: true));
        var without = PlacementSolver.Solve(Build(withCombos: false));

        // 잉걸불 1개에서 2개째면 임계값에 닿는다.
        Assert.Equal(Worth.ComboThreshold, with.Score - without.Score, 3);
    }

    private static CharmDefinition TelescopeDef() => new()
    {
        MaxLevel = 5,
        Behavior = "Charm_PlanetModule",
        NeighborEnhanceCategory = "PLANET",
    };

    /// <summary>게임이 거대화하는 행성 - <c>PLANET</c> 카테고리이면서 소환 타입이다.</summary>
    private static CharmDefinition PlanetDef() => new()
    {
        MaxLevel = 5,
        Behavior = "Charm_SummonGreenBat",
        Categories = { "PLANET" },
    };

    /// <summary>
    /// 거대한 망원경(<c>Charm_PlanetModule</c>)은 이웃 여덟 칸의 행성을 강화한다. 1x3 격자에서
    /// 가운데 서면 둘을, 끝에 서면 하나만 감싼다.
    /// </summary>
    [Fact]
    public void ATelescopeSitsWhereItTouchesTheMostPlanets()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(3, 1, 3) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = TelescopeDef() });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = PlanetDef() });
        problem.Charms.Add(new CharmSlot { InstanceId = 3, Definition = PlanetDef() });

        Assert.Equal(new GridPos(1, 0), PlacementSolver.Solve(problem).CharmPositions[1]);
    }

    /// <summary>
    /// 게임 <c>SearchPlanet</c>은 <c>PLANET</c> 카테고리에 더해 그 칸의 Charm 이
    /// <c>Charm_SummonGreenBat</c> 일 것을 요구한다. 카탈로그의 <c>PLANET</c> 열하나 가운데 넷 -
    /// 망원경 자신, 혜성, 악보 '은하', 붉은행성 관찰일지 - 이 그 타입이 아니므로, 카테고리만 보면
    /// 망원경이 거대화하지도 못할 것을 감싸고 앉는다. 두 칸짜리 격자라 이웃은 하나뿐이고,
    /// 그 하나가 진짜 행성일 때만 점수가 오른다.
    /// </summary>
    /// <summary>두 칸짜리 격자에 망원경과 이웃 하나. 차이는 거대화 몫뿐이다.</summary>
    private static double ScoreBesideTelescope(CharmDefinition neighbour)
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 1, 2) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = TelescopeDef() });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = neighbour });
        return PlacementSolver.Solve(problem).Score;
    }

    [Fact]
    public void ATelescopeOnlyCountsPlanetsTheGameCanEnlarge()
    {
        static double Score(CharmDefinition neighbour) => ScoreBesideTelescope(neighbour);

        var planet = PlanetDef();
        var lookalike = new CharmDefinition
        {
            MaxLevel = 5,
            Behavior = "Charm_FlamePlanet",
            Categories = { "PLANET" },
        };

        var step = PositionalWorth.EnhanceStep * new CharmSlot { Definition = planet }.Worth.LevelStep;
        Assert.True(step > 0);
        Assert.Equal(step, Score(planet) - Score(lookalike), 6);
    }

    /// <summary>
    /// 거대화는 <c>GreenBat</c>이 쏘는 순간 <c>num += num * 0.5f</c>로 그때의 피해량에 곱으로
    /// 걸린다. 그래서 크기는 레벨별 피해량 표와 견주어 <b>그 행성의 레벨 몇 칸</b>으로 나온다 -
    /// 푸른 행성(10→30, 5레벨)은 레벨 한 칸이 4이고 거대화가 5라 1.25칸이다. 표가 없는 카탈로그
    /// 22 이하의 자료에서는 예전 어림값(레벨 한 칸)으로 물러선다.
    /// </summary>
    [Fact]
    public void TheEnlargementIsMeasuredFromThePlanetsDamageTable()
    {
        var bystander = new CharmDefinition { MaxLevel = 5, Behavior = "Charm_SummonGreenBat" };
        var guessed = PlanetDef();
        var measured = PlanetDef();
        measured.SummonDamageByLevel = new List<int> { 10, 14, 18, 22, 26, 30 };

        var levelStep = new CharmSlot { Definition = measured }.Worth.LevelStep;
        var baseline = ScoreBesideTelescope(bystander);

        Assert.Equal(PositionalWorth.EnhanceStep * levelStep, ScoreBesideTelescope(guessed) - baseline, 6);
        Assert.Equal(1.25 * levelStep, ScoreBesideTelescope(measured) - baseline, 6);
    }

    /// <summary>
    /// 헌신의 휘장(<c>Charm_CompanionChaos</c>)은 같은 행을 끝에서 끝까지 훑는다. 동료가 한 줄에
    /// 몰려 있는 격자에서는 그 줄에 선다.
    /// </summary>
    [Fact]
    public void ADevotionInsigniaJoinsTheRowItsCompanionsAreIn()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 2, 4) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition { MaxLevel = 5, Behavior = "Charm_CompanionChaos" },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 2,
            Definition = new CharmDefinition { MaxLevel = 5, IsCompanion = true },
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 3, Definition = Bystander() });
        problem.Charms.Add(new CharmSlot { InstanceId = 4, Definition = Bystander() });

        // 동료를 아랫줄에 묶어 두고 휘장이 따라오는지 본다.
        problem.CurrentCharms[2] = new GridPos(0, 1);
        problem.CurrentCharms[3] = new GridPos(0, 0);
        problem.CurrentCharms[4] = new GridPos(1, 0);

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(1, arrangement.CharmPositions[1].Y);
    }

    /// <summary>
    /// 캘세더니 열쇠(<c>Charm_3Elemental_ByRow</c>)는 <c>행 % 개수</c>로 카테고리를 갈아입는다.
    /// 빙하 콤보가 한 걸음 남았으면 빙하가 걸리는 줄에 서야 한다.
    /// </summary>
    [Fact]
    public void AChalcedonyKeyPicksTheRowThatAdvancesACombo()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 3, 3) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 1,
            Definition = new CharmDefinition
            {
                MaxLevel = 5,
                Behavior = "Charm_3Elemental_ByRow",
                LineCategories = { "EMBER", "GLACIER", "MAGITECH" },
            },
        });
        problem.Charms.Add(new CharmSlot { InstanceId = 2, Definition = Bystander() });
        problem.Charms.Add(new CharmSlot { InstanceId = 3, Definition = Bystander() });

        problem.ComboCounts = new Dictionary<string, int> { ["GLACIER"] = 3 };
        problem.Combos = id => id == "GLACIER"
            ? new ComboDefinition { Id = "GLACIER", Thresholds = { 4 } }
            : null;

        // lineCategory[y % 3] 이 GLACIER 가 되는 줄은 y = 1 이다.
        Assert.Equal(new GridPos(0, 1), PlacementSolver.Solve(problem).CharmPositions[1]);
    }

    /// <summary>
    /// 실제 카탈로그의 북향(1289·1290). 덤은 기본의 2.5배이고 조건은 레어도 언커먼 이하다.
    /// </summary>
    private static CharmDefinition ConditionalNeedle() => new()
    {
        MaxLevel = 2,
        Rarity = Rarity.Rare,
        Behavior = "Charm_UpCharmDamage",
        DependencyOffsetY = -1,
        DependencyBonusByLevel = { 6, 8, 10 },
        DependencyExtraByLevel = { 15, 20, 25 },
        HasDependencyCondition = true,
        DependencyMaxRarity = Rarity.Uncommon,
    };

    private static CharmSlot Target(int id, Rarity rarity, double worth) => new()
    {
        InstanceId = id,
        Definition = new CharmDefinition { MaxLevel = 2, IsAttackable = true, Rarity = rarity },
        Worth = new CharmWorth { Base = worth, PerLevel = worth },
    };

    /// <summary>
    /// 침이 주는 것은 대상의 피해에 대한 백분율이므로, 값싼 대상의 35%가 주력의 10%를 이기려면
    /// 그 대상이 그만큼 세야 한다. 레어도 덤만 세던 때는 값싼 쪽이 무조건 이겼다(3.5배).
    /// </summary>
    [Fact]
    public void ANeedleWeighsTheRarityBonusAgainstHowStrongTheTargetIs()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 2, 4) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = ConditionalNeedle() });
        problem.Charms.Add(Target(2, Rarity.Common, 1));
        problem.Charms.Add(Target(3, Rarity.Rare, 10));

        var arrangement = PlacementSolver.Solve(problem);
        var above = arrangement.CharmPositions[1].Offset(0, -1);

        Assert.Equal(above, arrangement.CharmPositions[3]);
    }

    /// <summary>
    /// 세기가 같으면 게임의 레어도 덤이 그대로 이긴다 - 이쪽은 우리 선호가 아니라 게임 규칙이다.
    /// </summary>
    [Fact]
    public void BetweenEquallyStrongTargetsTheRarityBonusStillDecides()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(2, 2, 4) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = ConditionalNeedle() });
        problem.Charms.Add(Target(2, Rarity.Common, 5));
        problem.Charms.Add(Target(3, Rarity.Rare, 5));

        var arrangement = PlacementSolver.Solve(problem);
        var above = arrangement.CharmPositions[1].Offset(0, -1);

        Assert.Equal(above, arrangement.CharmPositions[2]);
    }

    /// <summary>고를 대상이 하나뿐이면 몫이 1이라 값어치가 전과 같다.</summary>
    [Fact]
    public void OneTargetKeepsTheFullRarityBonus()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(1, 2, 2) };
        problem.Charms.Add(new CharmSlot { InstanceId = 1, Definition = ConditionalNeedle() });
        problem.Charms.Add(Target(2, Rarity.Common, 1));

        var arrangement = PlacementSolver.Solve(problem);

        Assert.Equal(
            problem.Charms[1].Worth.At(0) + problem.Charms[0].Worth.At(0) * 3.5,
            arrangement.Score, 8);
    }
}

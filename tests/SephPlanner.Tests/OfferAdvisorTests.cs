using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

public class OfferAdvisorTests
{
    private static PlacementProblem BaseProblem()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Charms.Add(new CharmSlot { InstanceId = 10, Definition = new CharmDefinition { MaxLevel = 5 } });
        problem.Charms.Add(new CharmSlot { InstanceId = 11, Definition = new CharmDefinition { MaxLevel = 5 } });
        return problem;
    }

    private static OfferCandidate Tablet(string name, string query, int price) => new()
    {
        Kind = "tablet",
        Name = name,
        Price = price,
        Tablet = new TabletDefinition { Id = name, Query = query },
    };

    /// <summary>비싼 쪽이 훨씬 좋고, 싼 쪽은 조금만 좋다.</summary>
    private static List<OfferCandidate> Candidates() => new()
    {
        Tablet("expensive", "HORIZONTAL 2", price: 500),
        Tablet("cheap", "RIGHT 1", price: 10),
    };

    /// <summary>
    /// 상자도 상점도 열지 않은 평상시다. 볼 것이 없으면 기준 배치조차 풀지 않아야 한다 -
    /// 그 한 번이 실측에서 100ms 대였고, 폴링마다 돌면 그대로 프레임이 된다.
    /// </summary>
    [Fact]
    public void WithNoCandidatesNothingIsSolved()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 6) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 1,
            Definition = new TabletDefinition { Id = "t", Query = "HORIZONTAL 2" },
        });
        for (var i = 0; i < 5; i++)
            problem.Charms.Add(new CharmSlot { InstanceId = i, Definition = new CharmDefinition { MaxLevel = 5 } });

        // 벽시계로 재면 콜드 JIT 과 병렬 실행 때문에 느린 기계에서 깨진다. 재려던 성질은
        // 시간이 아니라 "탐색을 한 번도 돌리지 않는다"이므로 그것을 그대로 센다.
        var layouts = new LayoutCache();
        var advice = OfferAdvisor.Rank(
            problem, System.Array.Empty<OfferCandidate>(), gold: 1000, layouts: layouts);

        Assert.Empty(advice);
        Assert.Equal(0, layouts.Searches);
        Assert.Equal(0, layouts.Reuses);
    }

    [Fact]
    public void WithEnoughGoldTheBestOfferComesFirst()
    {
        var advice = OfferAdvisor.Rank(BaseProblem(), Candidates(), gold: 1000);

        Assert.Equal("expensive", advice[0].Candidate.Name);
        Assert.All(advice, entry => Assert.True(entry.Affordable));
    }

    [Fact]
    public void AnOfferYouCannotAffordDropsBelowOneYouCan()
    {
        // 지우지는 않는다. 지금 못 살 뿐이지 알아 둘 값어치는 있다.
        var advice = OfferAdvisor.Rank(BaseProblem(), Candidates(), gold: 100);

        Assert.Equal("cheap", advice[0].Candidate.Name);
        Assert.True(advice[0].Affordable);

        Assert.Equal("expensive", advice[1].Candidate.Name);
        Assert.False(advice[1].Affordable);
        Assert.True(advice[1].Gain > advice[0].Gain);
    }

    [Fact]
    public void SomethingYouJustPickUpIsAlwaysAffordable()
    {
        // 상자와 바닥에 떨어진 것은 값이 0이라 소지금이 없어도 집을 수 있다.
        var free = new List<OfferCandidate> { Tablet("free", "HORIZONTAL 2", price: 0) };

        var advice = OfferAdvisor.Rank(BaseProblem(), free, gold: 0);

        Assert.True(advice[0].Affordable);
    }

    private static OfferCandidate PlainCharm(int definitionId) => new()
    {
        Kind = "charm",
        DefinitionId = definitionId,
        Name = "C" + definitionId,
        Charm = new CharmDefinition { MaxLevel = 5 },
    };

    [Fact]
    public void EqualOffersKeepAStableOrderRegardlessOfArrivalOrder()
    {
        // 게임의 오브젝트 열거 순서는 비보장이다. 증가분까지 같은 후보가 입력 순서로 줄을 서면
        // 아무것도 달라지지 않았는데 화면 순위가 흔들린다.
        var forward = OfferAdvisor.Rank(
            BaseProblem(), new List<OfferCandidate> { PlainCharm(7), PlainCharm(5) }, gold: 0);
        var backward = OfferAdvisor.Rank(
            BaseProblem(), new List<OfferCandidate> { PlainCharm(5), PlainCharm(7) }, gold: 0);

        Assert.Equal(
            forward.Select(entry => entry.Candidate.DefinitionId),
            backward.Select(entry => entry.Candidate.DefinitionId));
    }

    private static OfferCandidate Charm(string name, params string[] categories) => new()
    {
        Kind = "charm",
        Name = name,
        Charm = new CharmDefinition { MaxLevel = 5, Categories = new List<string>(categories) },
    };

    private static readonly Dictionary<string, ComboDefinition> Combos = new()
    {
        ["EMBER"] = new ComboDefinition
        {
            Id = "EMBER",
            Thresholds = { 2, 5, 8 },
            Names = { ["current"] = "잉걸불" },
        },
    };

    private static ComboDefinition? FindCombo(string id) => Combos.TryGetValue(id, out var combo) ? combo : null;

    [Fact]
    public void CompletingAComboOutranksAnEqualCharmWithoutOne()
    {
        // 두 아티팩트는 배치 점수가 같다. 잉걸불 4개를 모은 상태에서 5개째(임계값)를 채우는
        // 쪽이 위로 와야 하고, 화면에 보여줄 진행도 함께 나와야 한다.
        var candidates = new List<OfferCandidate> { Charm("plain"), Charm("ember", "EMBER") };
        var counts = new Dictionary<string, int> { ["EMBER"] = 4 };

        var advice = OfferAdvisor.Rank(BaseProblem(), candidates, gold: 0, counts, FindCombo);

        Assert.Equal("ember", advice[0].Candidate.Name);
        Assert.True(advice[0].ComboCompletes);
        Assert.Equal("잉걸불 5/5", advice[0].ComboText);
        Assert.Equal("", advice[1].ComboText);
    }

    [Fact]
    public void ProgressTowardAComboIsWorthLessThanCompletingIt()
    {
        var candidates = new List<OfferCandidate> { Charm("ember", "EMBER") };
        var counts = new Dictionary<string, int> { ["EMBER"] = 2 };

        var advice = OfferAdvisor.Rank(BaseProblem(), candidates, gold: 0, counts, FindCombo);

        Assert.False(advice[0].ComboCompletes);
        Assert.Equal("잉걸불 3/5", advice[0].ComboText);
        Assert.True(advice[0].ComboBonus > 0);
    }

    [Fact]
    public void APriorityCategoryCharmOutranksAnEqualOne()
    {
        // 배치 점수도 콤보 진행도 같은 두 아티팩트. 밀고 있는 빌드와 맞는 쪽이 위로 온다.
        var candidates = new List<OfferCandidate> { Charm("stray", "STURDY"), Charm("ember", "EMBER") };
        var counts = new Dictionary<string, int>();
        var priorities = new HashSet<string> { "EMBER" };

        var advice = OfferAdvisor.Rank(
            BaseProblem(), candidates, gold: 0, counts, FindCombo, priorities);

        Assert.Equal("ember", advice[0].Candidate.Name);
        Assert.True(advice[0].MatchesPriority);
        Assert.False(advice[1].MatchesPriority);
    }

    [Fact]
    public void APriorityComboCountsForMoreThanAPlainOne()
    {
        // 잉걸불을 밀고 있으면 잉걸불 진행이 다른 콤보 진행보다 크게 잡힌다.
        var candidates = new List<OfferCandidate> { Charm("ember", "EMBER") };
        var counts = new Dictionary<string, int> { ["EMBER"] = 2 };

        var plain = OfferAdvisor.Rank(BaseProblem(), candidates, 0, counts, FindCombo);
        var pushed = OfferAdvisor.Rank(
            BaseProblem(), candidates, 0, counts, FindCombo, new HashSet<string> { "EMBER" });

        Assert.True(pushed[0].ComboBonus > plain[0].ComboBonus);
    }

    [Fact]
    public void AFullBagNamesWhatMustGo()
    {
        // 빈 칸이 없는 격자. 새 아티팩트를 집으면 기존 것 하나가 자리를 내줘야 한다.
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 2) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition { MaxLevel = 5, Names = { ["current"] = "낡은 반지" } },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 11,
            Definition = new CharmDefinition { MaxLevel = 5, Rarity = Rarity.Eternal, Names = { ["current"] = "핵심" } },
        });

        var offered = new List<OfferCandidate>
        {
            new() { Kind = "charm", Name = "new", Charm = new CharmDefinition { MaxLevel = 5, Rarity = Rarity.Rare } },
        };

        var advice = OfferAdvisor.Rank(problem, offered, gold: 0);

        // 레어도가 낮은 쪽이 밀려나야 하고, 그 이름이 그대로 나와야 한다.
        Assert.Equal("낡은 반지", advice[0].Displaced);
        Assert.True(advice[0].CandidatePlaced);
        var displacement = Assert.IsType<OfferDisplacement>(advice[0].Displacement);
        Assert.Equal(10, displacement.InstanceId);
        Assert.Equal("charm", displacement.Kind);
    }

    [Fact]
    public void AWeakCandidateInAFullBagCannotDropItself()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 1) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition { MaxLevel = 5, Rarity = Rarity.Eternal },
        });
        var candidate = new OfferCandidate
        {
            Kind = "charm",
            Name = "dormant",
            Charm = new CharmDefinition { MaxLevel = 5 },
            CharmIsDormant = true,
        };

        var advice = Assert.Single(OfferAdvisor.Rank(problem, new[] { candidate }, gold: 0));

        Assert.True(advice.Available);
        Assert.True(advice.CandidatePlaced);
        Assert.Equal(10, advice.Displacement!.InstanceId);
        Assert.True(advice.Gain < 0, $"교체 손실이 음수여야 하는데 {advice.Gain}");
    }

    [Fact]
    public void ReplacingTheSameCategoryDoesNotInventComboProgress()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 1) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition
            {
                MaxLevel = 5,
                Categories = { "EMBER" },
            },
        });
        var counts = new Dictionary<string, int> { ["EMBER"] = 1 };

        var advice = Assert.Single(OfferAdvisor.Rank(
            problem, new[] { Charm("new-ember", "EMBER") }, 0, counts, FindCombo));

        Assert.Equal(10, advice.Displacement!.InstanceId);
        Assert.False(advice.ComboCompletes);
        Assert.False(advice.ComboLoses);
        Assert.Equal("", advice.ComboText);
        Assert.Equal(0, advice.ComboBonus);
    }

    [Fact]
    public void ReplacingAnotherCategoryAccountsForComboLoss()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 1) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition
            {
                MaxLevel = 5,
                Categories = { "EMBER" },
            },
        });
        var combos = new Dictionary<string, ComboDefinition>(Combos)
        {
            ["FROST"] = new ComboDefinition { Id = "FROST", Thresholds = { 2 } },
        };
        var counts = new Dictionary<string, int> { ["EMBER"] = 2, ["FROST"] = 0 };

        var advice = Assert.Single(OfferAdvisor.Rank(
            problem, new[] { Charm("frost", "FROST") }, 0, counts,
            id => combos.TryGetValue(id, out var combo) ? combo : null));

        Assert.True(advice.ComboLoses);
        Assert.Contains("잉걸불 1/2", advice.ComboText);
        Assert.True(advice.ComboBonus < 0);
    }

    [Fact]
    public void ATabletCandidateIsMandatoryInAFullBag()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 1) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition { MaxLevel = 5 },
        });
        var tablet = Tablet("offered-tablet", "RIGHT 1", price: 0);

        var advice = Assert.Single(OfferAdvisor.Rank(problem, new[] { tablet }, gold: 0));

        Assert.True(advice.CandidatePlaced);
        Assert.Equal(10, advice.Displacement!.InstanceId);
    }

    [Fact]
    public void AFullBagCanDisplaceAFiller()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 1) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            IsFiller = true,
            Definition = new CharmDefinition(),
        });

        var advice = Assert.Single(OfferAdvisor.Rank(
            problem, new[] { Charm("candidate") }, gold: 0));

        Assert.Equal("filler", advice.Displacement!.Kind);
        Assert.True(advice.CandidatePlaced);
    }

    [Fact]
    public void AFullBagCanDisplaceATablet()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 1) };
        problem.Tablets.Add(new TabletSlot
        {
            InstanceId = 10,
            Definition = new TabletDefinition { Id = "held" },
        });

        var advice = Assert.Single(OfferAdvisor.Rank(
            problem, new[] { Charm("candidate") }, gold: 0));

        Assert.Equal("tablet", advice.Displacement!.Kind);
        Assert.True(advice.CandidatePlaced);
    }

    [Fact]
    public void EqualDisplacementsUseTheLowestInstanceId()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 2) };
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 20,
            Definition = new CharmDefinition { MaxLevel = 5 },
        });
        problem.Charms.Add(new CharmSlot
        {
            InstanceId = 10,
            Definition = new CharmDefinition { MaxLevel = 5 },
        });

        var advice = Assert.Single(OfferAdvisor.Rank(
            problem, new[] { Charm("candidate") }, gold: 0));

        Assert.Equal(10, advice.Displacement!.InstanceId);
    }

    [Fact]
    public void NoRemovableSlotReturnsUnavailableInsteadOfAZeroPointRecommendation()
    {
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, 0) };

        var advice = Assert.Single(OfferAdvisor.Rank(
            problem, new[] { Charm("unavailable") }, gold: 0));

        Assert.False(advice.Available);
        Assert.False(advice.CandidatePlaced);
        Assert.Null(advice.Preview);
    }

    [Fact]
    public void ARarerCharmOutranksACommonOneOfEqualLevel()
    {
        var candidates = new List<OfferCandidate>
        {
            new() { Kind = "charm", Name = "common", Charm = new CharmDefinition { MaxLevel = 5 } },
            new() { Kind = "charm", Name = "eternal", Charm = new CharmDefinition { MaxLevel = 5, Rarity = Rarity.Eternal } },
        };

        var advice = OfferAdvisor.Rank(BaseProblem(), candidates, gold: 0);

        Assert.Equal("eternal", advice[0].Candidate.Name);
        Assert.True(advice[0].Gain > advice[1].Gain);
    }

    [Fact]
    public void ACandidateThatChangesNothingHasZeroGain()
    {
        // 효과가 꺼진(무기 불일치) 후보는 빈 칸에 놓일 뿐 점수를 바꾸지 않는다. 기준과 후보를
        // 서로 다른 탐색 강도로 풀면 이런 후보에도 강도 차이만큼 가짜 증가분이 나온다.
        var candidates = new List<OfferCandidate>
        {
            new()
            {
                Kind = "charm",
                Name = "dormant",
                Charm = new CharmDefinition { MaxLevel = 5 },
                CharmIsDormant = true,
            },
        };

        var advice = OfferAdvisor.Rank(BaseProblem(), candidates, gold: 0);

        Assert.True(Math.Abs(advice[0].Gain) < 0.001, $"증가분이 0이어야 하는데 {advice[0].Gain}");
    }

    [Fact]
    public void APastAllThresholdsComboAddsNothing()
    {
        // 임계값을 다 넘긴 카테고리는 더 모아도 변하는 게 없다.
        var candidates = new List<OfferCandidate> { Charm("ember", "EMBER") };
        var counts = new Dictionary<string, int> { ["EMBER"] = 8 };

        var advice = OfferAdvisor.Rank(BaseProblem(), candidates, gold: 0, counts, FindCombo);

        Assert.Equal("", advice[0].ComboText);
        Assert.Equal(0, advice[0].ComboBonus);
    }

    private const int WantedCharm = 4242;

    [Fact]
    public void AnArtifactTheImportedBuildWantsRisesAboveAnIdenticalOne()
    {
        // 배치 증가분이 똑같은 둘 사이에서는 빌드가 지목한 쪽이 위로 와야 가져온 의미가 있다.
        var candidates = new List<OfferCandidate>
        {
            new() { Kind = "charm", Name = "plain", Charm = new CharmDefinition { MaxLevel = 5 } },
            new()
            {
                Kind = "charm", Name = "wanted",
                Charm = new CharmDefinition { EntityId = WantedCharm, MaxLevel = 5 },
            },
        };

        var advice = OfferAdvisor.Rank(
            BaseProblem(), candidates, gold: 0, presetCharms: new[] { WantedCharm });

        Assert.Equal("wanted", advice[0].Candidate.Name);
        Assert.True(advice[0].MatchesPreset);
        Assert.False(advice[1].MatchesPreset);
    }

    [Fact]
    public void AnArtifactTheImportedBuildWantsRisesAboveOneWithMoreGain()
    {
        // 가산점으로 겨루게 두면 배치 이득이 큰 남에게 밀린다. 빌드가 지목한 것은 뜨기만 하면
        // 맨 위여야 "프리셋을 넣으면 그 아이템을 권한다"가 된다.
        var candidates = new List<OfferCandidate>
        {
            new()
            {
                Kind = "charm", Name = "strong",
                Charm = new CharmDefinition
                {
                    MaxLevel = 5, Behavior = "Charm_StatusInstance",
                    StatWorthByLevel = new List<double> { 10, 20, 30, 40, 50, 60 },
                    StatWorthCoverageKnown = true,
                },
            },
            new()
            {
                Kind = "charm", Name = "wanted",
                Charm = new CharmDefinition { EntityId = WantedCharm, MaxLevel = 5 },
            },
        };

        var advice = OfferAdvisor.Rank(
            BaseProblem(), candidates, gold: 0, presetCharms: new[] { WantedCharm });

        Assert.Equal("wanted", advice[0].Candidate.Name);
        Assert.True(advice[0].Gain < advice[1].Gain, $"wanted {advice[0].Gain} / strong {advice[1].Gain}");
    }

    [Fact]
    public void WithNoImportedBuildNothingIsMarked()
    {
        var candidates = new List<OfferCandidate>
        {
            new()
            {
                Kind = "charm", Name = "wanted",
                Charm = new CharmDefinition { EntityId = WantedCharm, MaxLevel = 5 },
            },
        };

        var advice = OfferAdvisor.Rank(BaseProblem(), candidates, gold: 0);

        Assert.False(advice[0].MatchesPreset);
        Assert.Equal(0, advice[0].ComboBonus);
    }
}

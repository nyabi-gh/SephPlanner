using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Tests;

/// <summary>
/// 풀이가 얼마나 일하는지를 고정한다.
///
/// <b>왜 이 테스트가 있는가.</b> 조언 하나의 값을 "판을 통째로 다시 푼 결과"로 정의해 두었더니,
/// 갈래가 늘어날 때마다 배치 탐색이 그만큼 다시 돌았다. 실측에서 42칸 가방이 꽉 찬 채 후보 8개를
/// 보면 탐색이 288번 돌아 <b>41초, 할당 48GB</b> 였고, 그동안 게임은 GC 에 잡혀 프레임이 무너졌다.
///
/// 시간이 아니라 <b>배치 탐색 횟수</b>로 고정하는 것은, 시간은 기계마다 달라도 이 횟수는 알고리즘의
/// 성질이라 어디서 재도 같기 때문이다. 이 수가 갈래 수를 따라 늘기 시작하면 그때가 회귀다.
/// </summary>
public class SolverCostTests
{
    private const int Tablets = 6;

    /// <summary>
    /// 가방이 꽉 찬 판. 후보를 집으려면 무엇이든 하나는 빠져야 한다.
    /// <paramref name="spare"/>를 주면 그만큼 자리가 남아 밀어내는 갈래가 생기지 않는다.
    /// </summary>
    private static PlacementProblem FullBag(int charmCount, int spare = 0)
    {
        var storage = Tablets + charmCount + spare;
        var problem = new PlacementProblem { Grid = new GridSpec(6, 7, storage) };

        var queries = new[] { "RIGHT 1", "HORIZONTAL 2", "UP 1", "O 2", "VERTICAL 1", "KNIGHTUPLEFT 2" };
        for (var i = 0; i < Tablets; i++)
        {
            problem.Tablets.Add(new TabletSlot
            {
                InstanceId = 900 + i,
                Definition = new TabletDefinition { Id = "t" + i, EntityId = 700 + i, Query = queries[i] },
            });
        }
        for (var i = 0; i < charmCount; i++)
        {
            problem.Charms.Add(new CharmSlot
            {
                InstanceId = i,
                Definition = new CharmDefinition { Id = "c" + i, EntityId = 200 + i, MaxLevel = 5 },
            });
        }
        return problem;
    }

    private static List<OfferCandidate> Candidates(int charms, int tablets)
    {
        var candidates = new List<OfferCandidate>();
        for (var i = 0; i < charms; i++)
        {
            candidates.Add(new OfferCandidate
            {
                Kind = "charm",
                Name = "offered-charm" + i,
                Charm = new CharmDefinition { Id = "oc" + i, EntityId = 300 + i, MaxLevel = 5, Rarity = Rarity.Rare },
            });
        }
        for (var i = 0; i < tablets; i++)
        {
            candidates.Add(new OfferCandidate
            {
                Kind = "tablet",
                Name = "offered-tablet" + i,
                Tablet = new TabletDefinition { Id = "ot" + i, EntityId = 400 + i, Query = "RIGHT 2" },
            });
        }
        return candidates;
    }

    /// <summary>
    /// 후보 추천의 배치 탐색은 <b>후보 수</b>를 따라야지 갈래 수를 따르면 안 된다.
    ///
    /// 후보 하나가 탐색을 쓰는 자리는 둘뿐이다. 석판 후보라 석판 구성이 달라질 때 한 번,
    /// 그리고 이긴 갈래가 석판을 밀어낸 갈래라 보고할 점수를 제대로 내야 할 때 한 번이다.
    /// 아티팩트를 밀어내는 갈래는 - 가방이 찼을 때 갈래의 대부분이다 - 기준 탐색을 그대로 쓴다.
    /// </summary>
    [Fact]
    public void RankingOffersSearchesLayoutsPerCandidateNotPerTrial()
    {
        var problem = FullBag(charmCount: 35);
        var candidates = Candidates(charms: 4, tablets: 4);
        var cache = new LayoutCache();

        var advice = OfferAdvisor.Rank(problem, candidates, gold: int.MaxValue, layouts: cache);

        Assert.Equal(candidates.Count, advice.Count);
        Assert.True(
            cache.Searches <= 1 + 2 * candidates.Count,
            $"후보 {candidates.Count}개(갈래는 후보마다 {problem.Charms.Count + problem.Tablets.Count}개)에 " +
            $"배치 탐색이 {cache.Searches}번 돌았다. 갈래마다 다시 푸는 옛 방식으로 돌아간 것이다.");

        // 돌려 쓰지 못했다면 위의 상한도 뜻이 없다. 갈래가 실제로 많았음을 함께 확인한다.
        Assert.True(cache.Reuses > 100, $"돌려 쓴 횟수가 {cache.Reuses}뿐이다. 갈래가 줄어든 것은 아닌지 보라.");
    }

    /// <summary>
    /// 가방에 든 것이 늘어도 탐색 횟수는 그대로여야 한다. 늘어나는 것은 "무엇을 밀어낼까"의
    /// 갈래뿐이고, 그것은 배정만 다시 풀면 되는 일이다.
    /// </summary>
    [Fact]
    public void AFullerBagDoesNotCostMoreLayoutSearches()
    {
        var small = new LayoutCache();
        OfferAdvisor.Rank(FullBag(charmCount: 10), Candidates(charms: 4, tablets: 0), int.MaxValue, layouts: small);

        var large = new LayoutCache();
        OfferAdvisor.Rank(FullBag(charmCount: 35), Candidates(charms: 4, tablets: 0), int.MaxValue, layouts: large);

        Assert.Equal(small.Searches, large.Searches);
        Assert.True(large.Reuses > small.Reuses, "갈래가 늘었으면 돌려 쓴 횟수는 늘어야 한다.");
    }

    private static Catalog MixCatalog() => new(
        new[] { new TabletDefinition { Id = "mixed", EntityId = TabletMix.ResultEntityId } },
        Array.Empty<CharmDefinition>());

    /// <summary>
    /// 후보 추천과 석판 합성은 <b>같은 기준 배치</b> 위에서 겨뤄야 한다.
    ///
    /// 둘은 탐색 강도를 저마다 적어 두고 기준을 따로 풀고 있었다. 강도가 <see cref="LayoutCache"/>의
    /// 열쇠에 들어가므로, 한쪽만 고치면 캐시가 조용히 갈라져 <b>두 조언이 서로 다른 배치를 기준으로
    /// 증가분을 말하게 된다.</b> 컴파일도 되고 테스트도 통과하는 종류의 사고라 여기서 붙잡는다.
    ///
    /// 자리를 남긴 판을 쓰는 것은 밀어내는 갈래를 없애기 위해서다. 아티팩트 후보는 석판 구성을
    /// 건드리지 않으므로, 두 조언이 같은 강도를 쓰는 한 석판 탐색은 처음 한 번이 전부다.
    /// </summary>
    [Fact]
    public void BothAdvisorsShareOneBaseLayoutSearch()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var shared = new LayoutCache();

        TabletMixAdvisor.Rank(problem, MixCatalog(), cost: 0, gold: 1000, limit: 1, layouts: shared);
        var afterMix = shared.Searches;

        OfferAdvisor.Rank(problem, Candidates(charms: 3, tablets: 0), int.MaxValue, layouts: shared);

        Assert.Equal(afterMix, shared.Searches);
    }

    /// <summary>
    /// 기준 배치는 판과 강도가 그대로일 때만 돌려 쓴다. 강도가 다른데도 남의 답을 주면, 기준과
    /// 후보가 다른 잣대로 풀리는 것을 막으려던 자리가 도리어 그것을 만들어 낸다.
    /// </summary>
    [Fact]
    public void TheBaselineIsReusedOnlyForTheSameProblemAndStrength()
    {
        var problem = FullBag(charmCount: 12, spare: 6);
        var cache = new LayoutCache();
        var advice = SolverOptions.ForAdvice(default);

        var first = cache.Baseline(problem, advice);

        Assert.Same(first, cache.Baseline(problem, SolverOptions.ForAdvice(default)));
        Assert.NotSame(first, cache.Baseline(problem, new SolverOptions { BeamWidth = 32 }));
        Assert.NotSame(first, cache.Baseline(FullBag(charmCount: 12, spare: 6), advice));
    }

    /// <summary>
    /// 합성 추천은 쌍마다 처음부터 풀지 않는다. 짐작으로 줄을 세우고 상위 몇만 다시 푼다 -
    /// 석판 다섯이면 쌍이 열인데, 예전에는 그 열을 전부 풀어 2.3초에 3.2GB 였다.
    /// </summary>
    [Fact]
    public void RankingMixesSearchesOnlyForTheOnesItShows()
    {
        var problem = FullBag(charmCount: 20);
        var cache = new LayoutCache();

        const int limit = 5;
        var advice = TabletMixAdvisor.Rank(
            problem, MixCatalog(), cost: 0, gold: 1000, limit: limit, layouts: cache);

        Assert.NotEmpty(advice);
        Assert.True(
            cache.Searches <= 1 + limit,
            $"쌍이 {Tablets * (Tablets - 1) / 2}개인데 배치 탐색이 {cache.Searches}번 돌았다. " +
            "보여줄 것만 다시 푸는 규약이 깨졌다.");
    }
}

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

    [Fact]
    public void WithEnoughGoldTheBestOfferComesFirst()
    {
        var advice = OfferAdvisor.Rank(BaseProblem(), baseScore: 0, Candidates(), gold: 1000);

        Assert.Equal("expensive", advice[0].Candidate.Name);
        Assert.All(advice, entry => Assert.True(entry.Affordable));
    }

    [Fact]
    public void AnOfferYouCannotAffordDropsBelowOneYouCan()
    {
        // 지우지는 않는다. 지금 못 살 뿐이지 알아 둘 값어치는 있다.
        var advice = OfferAdvisor.Rank(BaseProblem(), baseScore: 0, Candidates(), gold: 100);

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

        var advice = OfferAdvisor.Rank(BaseProblem(), baseScore: 0, free, gold: 0);

        Assert.True(advice[0].Affordable);
    }
}

using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public class AutoPlacePolicyTests
{
    [Fact]
    public void AVerifiedCurrentPlanIsAllowed()
    {
        Assert.True(AutoPlacePolicy.Evaluate(Context()).Allowed);
    }

    [Fact]
    public void AStalePlanIsDeniedBeforeItCanBeApplied()
    {
        var context = Context();
        context.Runner!.RequestedGeneration++;

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Contains("최신", decision.Reason);
    }

    [Fact]
    public void ARealtimeSimulationFailureUsesTheSameReason()
    {
        var context = Context();
        context.RuntimeVerification = PlanVerificationStatus.Failed;
        context.RuntimeVerificationReason = "시뮬레이터 불일치";

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Equal("시뮬레이터 불일치", decision.Reason);
    }

    [Fact]
    public void APlanFromAnotherCatalogGenerationIsDenied()
    {
        var context = Context();
        context.CatalogGeneration = "new-catalog";

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Contains("질의 검증", decision.Reason);
    }

    [Fact]
    public void ACoreVerificationFailureKeepsItsExactReason()
    {
        var context = Context();
        context.Runner!.Latest!.Verification = new PlanVerification
        {
            Status = PlanVerificationStatus.Failed,
            Reason = "비활성 행렬 불일치",
        };

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Equal("비활성 행렬 불일치", decision.Reason);
    }

    [Fact]
    public void AMultiplayerSessionIsDeniedUnlessItWasOpenedOnPurpose()
    {
        var context = Context();
        context.IsMultiplayer = true;

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Contains("멀티플레이", decision.Reason);
    }

    /// <summary>
    /// 호스트와 참가자를 가리지 않는다. 이동은 <c>CmdSwap</c>, 회전은 <c>CmdDoClickAction</c> 으로
    /// 게임이 클라이언트에게 열어 둔 길이 있다(docs/RESEARCH.md 의 "멀티플레이"). 한때 여기서
    /// 클라이언트를 막았는데, "호스트에서만 된다"를 정책이 아니라 사실로 잘못 알았기 때문이다.
    /// </summary>
    [Fact]
    public void OpeningMultiplayerAllowsHostAndClientAlike()
    {
        var context = Context();
        context.IsMultiplayer = true;
        context.AllowMultiplayer = true;

        Assert.True(AutoPlacePolicy.Evaluate(context).Allowed);
    }

    /// <summary>세션 자체가 없으면 쓰기가 나갈 곳이 없다.</summary>
    [Fact]
    public void NoNetworkSessionIsDenied()
    {
        var context = Context();
        context.SessionActive = false;

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Contains("네트워크 세션", decision.Reason);
    }

    private static AutoPlaceContext Context()
    {
        var plan = new Plan
        {
            RequestGeneration = 1,
            CatalogGeneration = "catalog",
            PlacementFingerprint = "placement",
            HasPlacementChanges = true,
            Verification = new PlanVerification
            {
                Status = PlanVerificationStatus.Passed,
                Reason = "ok",
            },
            Targets = { new PlanTarget { InstanceId = 1 } },
        };
        return new AutoPlaceContext
        {
            Runner = new PlanRunState
            {
                Latest = plan,
                RequestedGeneration = 1,
                PublishedGeneration = 1,
            },
            CatalogVerified = true,
            CatalogGeneration = "catalog",
            RuntimeVerification = PlanVerificationStatus.Passed,
            CurrentPlacementFingerprint = "placement",
            SessionActive = true,
        };
    }
}

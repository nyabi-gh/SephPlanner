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
        context.ServerActive = true;

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Contains("멀티플레이", decision.Reason);
    }

    [Fact]
    public void OpeningMultiplayerAllowsItOnTheHost()
    {
        var context = Context();
        context.IsMultiplayer = true;
        context.AllowMultiplayer = true;
        context.ServerActive = true;

        Assert.True(AutoPlacePolicy.Evaluate(context).Allowed);
    }

    /// <summary>
    /// 쓰기는 서버 API 라 클라이언트에서는 애초에 돌지 않는다. 허용을 켜도 여기서 막혀야 한다 -
    /// 선례가 된 커뮤니티 모드가 망가뜨린 곳이 정확히 클라이언트 쓰기다(docs/LEGAL.md).
    /// </summary>
    [Fact]
    public void OpeningMultiplayerStillDeniesAClient()
    {
        var context = Context();
        context.IsMultiplayer = true;
        context.AllowMultiplayer = true;
        context.ServerActive = false;

        var decision = AutoPlacePolicy.Evaluate(context);

        Assert.False(decision.Allowed);
        Assert.Contains("호스트", decision.Reason);
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
            ServerActive = true,
        };
    }
}

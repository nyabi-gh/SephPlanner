using SephPlanner.Core.Planning;

namespace SephPlanner.Core.Runtime
{
    public sealed class AutoPlaceContext
    {
        public PlanRunState? Runner { get; set; }
        public bool CatalogVerified { get; set; }
        public string CatalogGeneration { get; set; } = "";
        public PlanVerificationStatus RuntimeVerification { get; set; }
        public string RuntimeVerificationReason { get; set; } = "";
        public string CurrentPlacementFingerprint { get; set; } = "";
        public bool IsMultiplayer { get; set; }
        public bool AllowMultiplayer { get; set; }
        public bool ServerActive { get; set; }
    }

    public sealed class AutoPlaceDecision
    {
        private AutoPlaceDecision(bool allowed, string reason)
        {
            Allowed = allowed;
            Reason = reason;
        }

        public bool Allowed { get; }
        public string Reason { get; }

        public static AutoPlaceDecision Allow() => new AutoPlaceDecision(true, "자동 배치를 실행할 수 있습니다.");

        public static AutoPlaceDecision Deny(string reason) => new AutoPlaceDecision(false, reason);
    }

    public static class AutoPlacePolicy
    {
        public static AutoPlaceDecision Evaluate(AutoPlaceContext context)
        {
            var state = context.Runner;
            if (state is null) return AutoPlaceDecision.Deny("계획 데이터가 아직 준비되지 않았습니다.");
            if (state.Error is not null) return AutoPlaceDecision.Deny("계산 실패 - " + state.Error);
            if (state.IsBusy || state.HasPending || !state.IsCurrent)
                return AutoPlaceDecision.Deny("최신 게임 상태의 계획을 계산 중입니다.");

            var plan = state.Latest;
            if (plan is null) return AutoPlaceDecision.Deny("옮길 것이 없습니다.");
            if (plan.RequestGeneration != state.RequestedGeneration)
                return AutoPlaceDecision.Deny("최신 요청과 계획의 generation이 달라 자동 배치를 잠갔습니다.");
            if (!context.CatalogVerified || plan.CatalogGeneration != context.CatalogGeneration)
                return AutoPlaceDecision.Deny(
                    "석판 질의 검증이 끝나지 않아 자동 배치를 사용할 수 없습니다. F9로 데이터를 다시 만드세요.");
            if (!plan.Verification.Passed) return AutoPlaceDecision.Deny(plan.Verification.Reason);
            if (context.RuntimeVerification != PlanVerificationStatus.Passed)
            {
                return AutoPlaceDecision.Deny(
                    context.RuntimeVerificationReason.Length > 0
                        ? context.RuntimeVerificationReason
                        : "실시간 시뮬레이션 검증이 끝나지 않아 자동 배치를 사용할 수 없습니다.");
            }
            if (plan.PlacementFingerprint.Length == 0 ||
                plan.PlacementFingerprint != context.CurrentPlacementFingerprint)
                return AutoPlaceDecision.Deny("배치 계산 이후 게임 상태가 바뀌어 최신 계획을 기다립니다.");
            if (!plan.HasPlacementChanges || plan.Targets.Count == 0)
                return AutoPlaceDecision.Deny("옮길 것이 없습니다.");
            // 멀티 세션은 기본으로 잠근다. 개발사가 금지한 것은 아니고 인벤토리 동기화 구현을
            // 바꾸는 중이라 잠가 두는 편이 안전하다고 답했다(docs/LEGAL.md "받은 답변"). 그래서
            // 켜는 길은 두되 기본은 꺼짐이고, 켜도 아래 ServerActive 가 호스트로 한정한다.
            if (context.IsMultiplayer && !context.AllowMultiplayer)
                return AutoPlaceDecision.Deny("멀티플레이 세션에서는 자동 배치를 실행하지 않습니다.");
            if (!context.ServerActive)
                return AutoPlaceDecision.Deny("서버가 활성 상태가 아니라 자동 배치를 실행할 수 없습니다. (호스트에서만 동작)");

            return AutoPlaceDecision.Allow();
        }
    }
}

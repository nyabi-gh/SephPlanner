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

        /// <summary>네트워크 세션이 살아 있는가. 호스트든 참가자든 상관없다 - 쓰기가 나갈 곳이 있는지다.</summary>
        public bool SessionActive { get; set; }

        /// <summary>
        /// 앞선 적용이 아직 도는 중인가. 참가자 세션에서는 걸음마다 서버 왕복을 기다리므로 수 초가
        /// 걸린다. 그동안 안내 줄이 계속 키를 광고하면 눌러야만 진행 중인 줄 알게 된다.
        /// </summary>
        public bool Applying { get; set; }
        public bool RecoveryRequired { get; set; }
        public bool Previewing { get; set; }
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
            if (context.Applying) return AutoPlaceDecision.Deny("자동 배치가 아직 진행 중입니다.");
            if (context.RecoveryRequired)
                return AutoPlaceDecision.Deny("이전 명령의 서버 반영이 불확실해 자동 배치를 잠갔습니다. 방에 재접속한 뒤 시도하세요.");
            if (context.Previewing)
                return AutoPlaceDecision.Deny("후보 미리보기 중입니다. 현재 가방 배치로 돌아온 뒤 자동 배치를 실행하세요.");

            var state = context.Runner;
            if (state is null) return AutoPlaceDecision.Deny("계획 데이터가 아직 준비되지 않았습니다.");
            if (state.Error is not null) return AutoPlaceDecision.Deny("계산 실패 - " + state.Error);
            // 무엇을 해야 하는지까지 말한다. 이 거절은 잠깐 뒤면 저절로 풀리는 것이라, 이유만
            // 적어 두면 눌러도 아무 일이 없는 것으로 읽힌다.
            if (state.Latest is null && (state.IsBusy || state.HasPending || !state.IsCurrent))
                return AutoPlaceDecision.Deny("최신 게임 상태의 계획을 계산 중입니다. 잠시 뒤 다시 누르세요.");

            var plan = state.Latest;
            if (plan is null) return AutoPlaceDecision.Deny("옮길 것이 없습니다.");
            if (plan.RequestGeneration <= 0 || plan.RequestGeneration != state.PublishedGeneration)
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
            // 놓을 자리가 없는 석판이 있으면 계획이 목표를 비우므로, 사유를 가르지 않으면
            // "옮길 것이 없습니다"가 되어 화면의 경고와 딴소리를 하게 된다.
            if (plan.Best.UnplacedTablets > 0)
                return AutoPlaceDecision.Deny(
                    $"석판 {plan.Best.UnplacedTablets}개를 놓을 자리가 없어 자동 배치를 실행하지 않습니다.");
            if (!plan.HasPlacementChanges || plan.Targets.Count == 0)
                return AutoPlaceDecision.Deny("옮길 것이 없습니다.");
            // 멀티 세션은 기본으로 잠근다. 개발사가 금지한 것은 아니고 인벤토리 동기화 구현을
            // 바꾸는 중이라 잠가 두는 편이 안전하다고 답했다(docs/LEGAL.md "받은 답변"). 그래서
            // 켜는 길은 두되 기본은 꺼짐이다. 켜면 호스트와 참가자 양쪽에서 돈다 - 이동은
            // CmdSwap, 회전은 CmdDoClickAction 으로 게임 자신이 클라이언트에게 열어 둔 길이 있다
            // (docs/RESEARCH.md 의 "멀티플레이").
            if (context.IsMultiplayer && !context.AllowMultiplayer)
                return AutoPlaceDecision.Deny("멀티플레이 세션에서는 자동 배치를 실행하지 않습니다.");
            if (!context.SessionActive)
                return AutoPlaceDecision.Deny("네트워크 세션이 없어 자동 배치를 실행할 수 없습니다.");

            return AutoPlaceDecision.Allow();
        }
    }
}

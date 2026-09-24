namespace SephPlanner.Core.Planning
{
    public enum PlanVerificationStatus
    {
        Unavailable,
        Passed,
        Failed,
    }

    public sealed class PlanVerification
    {
        public const string GameNotRecalculated =
            "게임이 석판 효과를 아직 지금 가방에 맞춰 다시 계산하지 않았습니다. 아이템을 하나 옮기면 게임이 다시 계산합니다.";

        public PlanVerificationStatus Status { get; set; }
        public int LevelMismatches { get; set; }
        public int EffectiveLevelMismatches { get; set; }
        public int DisabledMismatches { get; set; }
        public int TabletMismatches { get; set; }
        public string Reason { get; set; } = "계획 검증이 아직 완료되지 않았습니다.";

        public bool Passed => Status == PlanVerificationStatus.Passed;
    }
}

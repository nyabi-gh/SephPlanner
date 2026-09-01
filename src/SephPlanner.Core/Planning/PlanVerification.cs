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
        public PlanVerificationStatus Status { get; set; }
        public int LevelMismatches { get; set; }
        public int EffectiveLevelMismatches { get; set; }
        public int DisabledMismatches { get; set; }
        public int TabletMismatches { get; set; }
        public string Reason { get; set; } = "계획 검증이 아직 완료되지 않았습니다.";

        public bool Passed => Status == PlanVerificationStatus.Passed;
    }
}

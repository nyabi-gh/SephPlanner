using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    public enum MagicSupportEffect
    {
        CooldownRecovery,
        ManaCostReduction,
    }

    /// <summary>지정 방향의 마법 대상과 효과 종류. 효과량 단위는 퍼센트다.</summary>
    public sealed class DirectedMagicSupport
    {
        public MagicSupportEffect Effect { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public List<double> AmountByLevel { get; set; } = new List<double>();
    }
}

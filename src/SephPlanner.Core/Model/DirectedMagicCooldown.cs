using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>지정 방향의 마법 하나에 주는 쿨다운 회복 속도 보너스. 단위는 퍼센트다.</summary>
    public sealed class DirectedMagicCooldown
    {
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public List<double> RecoveryByLevel { get; set; } = new List<double>();
    }
}

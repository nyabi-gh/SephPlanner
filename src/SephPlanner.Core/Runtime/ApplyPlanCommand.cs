using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 계산한 최종 배치와 계산 당시의 게임 상태. 적용 직전에 살아 있는 상태와 대조한다.
    /// </summary>
    public sealed class ApplyPlanCommand
    {
        public int ExpectedWidth { get; set; }
        public int ExpectedHeight { get; set; }
        public int ExpectedStorage { get; set; }
        public List<PlanTarget> Targets { get; set; } = new List<PlanTarget>();
    }

    /// <summary>인스턴스 하나가 최종적으로 있어야 할 자리.</summary>
    public sealed class PlanTarget
    {
        public int InstanceId { get; set; }
        public GridPos From { get; set; }
        public GridPos To { get; set; }
        public bool IsTablet { get; set; }

        /// <summary>석판만 의미가 있다.</summary>
        public int FromRotation { get; set; }

        /// <summary>석판만 의미가 있다.</summary>
        public int Rotation { get; set; }
    }
}

using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Ipc
{
    /// <summary>
    /// 오버레이가 플러그인에 보내는 "이 배치로 맞춰 달라"는 명령. 걸음 순서가 아니라 최종 배치를
    /// 보낸다. 명령이 만들어진 뒤 게임 상태가 바뀌었을 수 있으므로, 어떤 순서로 옮길지는
    /// 살아 있는 상태를 아는 플러그인이 정한다.
    /// </summary>
    public sealed class ApplyPlanCommand
    {
        public int ProtocolVersion { get; set; } = IpcContract.ProtocolVersion;
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

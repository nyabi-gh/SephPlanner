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
        public string ExpectedPlacementFingerprint { get; set; } = "";
        public string ExpectedPlanningContextFingerprint { get; set; } = "";
        public string ExpectedWeaponId { get; set; } = "";
        public string ExpectedCatalogGeneration { get; set; } = "";
        public List<PlanTarget> Targets { get; set; } = new List<PlanTarget>();

        /// <summary>
        /// 적용이 끝났을 때 칸마다 나와야 하는 레벨. 어긋나면 우리가 읽지 않는 효과가 걸려 있다는
        /// 뜻이다. 되돌릴 일은 아니지만(상태는 합법이다) 게임 규칙이 바뀐 것을 다음 폴링이 아니라
        /// 첫 적용에서 잡는 유일한 신호다.
        /// </summary>
        public Dictionary<GridPos, int> ExpectedCellLevels { get; set; } = new Dictionary<GridPos, int>();

        /// <summary>
        /// 목표까지 가는 길에 합치기 재료가 맞붙는다. 게임은 교환마다 합칠 덩어리를 다시 찾되 실제로 합치는 것은 다음
        /// 서버 <c>LateUpdate</c> 에서 그 덩어리가 아직 있을 때뿐이다(게임 1.1.4 <c>GridInventory.ReleasePermission</c>,
        /// <c>CombineArrangementBonus</c>). 호스트는 걸음 사이에 프레임을 넘기지 않아 중간 배치가 합쳐지지 않지만,
        /// 참가자는 걸음마다 서버 왕복을 기다리는 사이 합쳐진다.
        /// </summary>
        public bool RequiresImmediateWrites { get; set; }
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

        /// <summary>
        /// 옮길 수 없는 아이템의 자리(<see cref="PlacedItem.Immovable"/>). 목표는 언제나 제자리이고,
        /// 적용기는 이것을 보고 번호로 찾으려 들지 않는다 - 애초에 번호가 없어서 못 옮기는 것이다.
        /// </summary>
        public bool Immovable { get; set; }
    }
}

using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    public sealed class CharmSlot
    {
        public CharmDefinition Definition { get; set; } = new CharmDefinition();
        public int InstanceId { get; set; }

        /// <summary>인챈트 등으로 붙은 고정 레벨. 석판과 무관하게 더해진다.</summary>
        public int Enchant { get; set; }

        /// <summary>이 아티팩트를 얼마나 중요하게 볼지. 1이 기준이다.</summary>
        public double Weight { get; set; } = 1;

        /// <summary>
        /// 소비 아이템처럼 점수에 기여하지 않지만 칸은 차지하는 것. 무시하면 그 칸이 빈 칸으로
        /// 취급되어 솔버가 이미 찬 자리에 아티팩트를 놓으라고 한다.
        /// </summary>
        public bool IsFiller { get; set; }
    }

    public sealed class TabletSlot
    {
        public TabletDefinition Definition { get; set; } = new TabletDefinition();
        public int InstanceId { get; set; }
        public string? InstanceQuery { get; set; }
        public string? InstanceConditionQuery { get; set; }

        public TabletPlacement At(GridPos position, int rotation) => new TabletPlacement
        {
            Definition = Definition,
            Position = position,
            Rotation = rotation,
            InstanceQuery = InstanceQuery,
            InstanceConditionQuery = InstanceConditionQuery,
        };
    }

    public sealed class PlacementProblem
    {
        public GridSpec Grid { get; set; } = GridSpec.WithStorage(GridSpec.DefaultWidth * GridSpec.DefaultHeight);
        public List<CharmSlot> Charms { get; set; } = new List<CharmSlot>();
        public List<TabletSlot> Tablets { get; set; } = new List<TabletSlot>();
    }

    public sealed class SolverOptions
    {
        /// <summary>석판 배치 탐색에서 단계마다 남길 후보 수.</summary>
        public int BeamWidth { get; set; } = 400;

        /// <summary>정확한 배정까지 돌려볼 최종 후보 수.</summary>
        public int ExactCandidates { get; set; } = 100;

        /// <summary>조건 판정과 배정이 서로를 참조하므로 몇 번 되풀이해 수렴시킬지.</summary>
        public int FixpointIterations { get; set; } = 3;
    }

    public sealed class Arrangement
    {
        public List<TabletPlacement> Tablets { get; } = new List<TabletPlacement>();

        /// <summary>아티팩트 인스턴스 번호 → 배치된 칸.</summary>
        public Dictionary<int, GridPos> CharmPositions { get; } = new Dictionary<int, GridPos>();

        public double Score { get; set; }

        /// <summary>칸별 최종 레벨. 오버레이가 그대로 표시한다.</summary>
        public Dictionary<GridPos, int> Levels { get; } = new Dictionary<GridPos, int>();

        /// <summary>조건을 만족하지 못해 효과가 꺼진 아티팩트.</summary>
        public List<int> InactiveCharms { get; } = new List<int>();
    }
}

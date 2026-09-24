using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 콤보가 켜질 때 서버가 심고 꺼질 때 지우는 고정 각인(<c>ComboEffect_Mystic</c>). 가방에 쓸
    /// 때마다 게임이 콤보를 전부 껐다 켜므로 <b>몇 칸이 살아 있는지는 그 배치의 콤보 수가 정한다.</b>
    /// 그래서 고정 칸 효과(<see cref="FixedEffectCell"/>)와 달리 배치마다 다시 푼다.
    /// </summary>
    public sealed class ComboEngravingRule
    {
        public string Category { get; set; } = "";

        /// <summary>문턱 순서대로. 문턱마다 좌표 목록의 다음 칸들에 각인을 더 심는다.</summary>
        public List<ComboEngravingTier> Tiers { get; set; } = new List<ComboEngravingTier>();

        /// <summary>판마다 정해지는 각인 좌표(<c>GridInventory.mysticPositions</c>). 참가자에게도 동기화된다.</summary>
        public List<GridPos> Positions { get; set; } = new List<GridPos>();

        /// <summary>심는 석판의 효과 질의. 조건 질의가 있는 석판은 규칙으로 읽지 않는다.</summary>
        public string Query { get; set; } = "";
    }

    public sealed class ComboEngravingTier
    {
        public int Threshold { get; set; }
        public int Count { get; set; }
    }
}

using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 콤보(세트 효과) 정의. 게임의 <c>ItemCategoryEntity</c>에서 온다. 같은 카테고리의
    /// 아티팩트를 임계값만큼 모으면 효과가 발동하며, 개수는 배치 위치와 무관하게 격자에 있는
    /// 아티팩트 전체로 센다. 자리에 따라 카테고리가 바뀌는 열쇠·종이·침만 배치에 따라 개수가 달라진다.
    /// </summary>
    public sealed class ComboDefinition
    {
        /// <summary>게임 내부 카테고리 식별자. 아티팩트의 <c>Categories</c> 값과 같은 체계다.</summary>
        public string Id { get; set; } = "";

        /// <summary>발동 임계값들. 오름차순.</summary>
        public List<int> Thresholds { get; set; } = new List<int>();

        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();

        /// <summary>임계값마다 무슨 효과가 발동하는지. 게임의 콤보 패널이 쓰는 텍스트 그대로다.</summary>
        public List<ComboEffectLine> Effects { get; set; } = new List<ComboEffectLine>();
    }

    public sealed class ComboEffectLine
    {
        public int Threshold { get; set; }
        public string Text { get; set; } = "";
    }
}

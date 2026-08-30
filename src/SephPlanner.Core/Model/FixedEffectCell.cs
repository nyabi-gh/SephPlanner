namespace SephPlanner.Core.Model
{
    /// <summary>
    /// 자리와 값이 이미 확정된 칸 효과 하나. 게임의 고정 각인(<c>FixedEngraving</c>)에서 온다.
    /// 신비 콤보처럼 시스템이 만드는 효과로, 질의를 풀 필요 없이 절대 좌표에 값이 박혀 있고
    /// 플레이어가 옮길 수 없다. 배치 탐색에서는 주어진 조건으로만 쓰인다.
    /// </summary>
    public sealed class FixedEffectCell
    {
        public GridPos Position { get; set; }
        public int Level { get; set; }
        public int Multiply { get; set; }
        public int Disable { get; set; }
        public int IgnoreCriteria { get; set; }
    }
}

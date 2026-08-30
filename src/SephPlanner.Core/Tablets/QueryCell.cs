using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 질의 한 줄이 지목한 칸 하나. 게임의 <c>StoneTablet.AdditionMetadata</c>에 대응한다.
    /// </summary>
    public struct QueryCell
    {
        public GridPos Position;
        public string Value;
        public bool IsXWorldPosition;
        public bool IsYWorldPosition;
        public bool BorderTop;
        public bool BorderRight;
        public bool BorderBottom;
        public bool BorderLeft;

        public QueryCell(GridPos position, string value)
        {
            Position = position;
            Value = value;
            IsXWorldPosition = false;
            IsYWorldPosition = false;
            BorderTop = false;
            BorderRight = false;
            BorderBottom = false;
            BorderLeft = false;
        }
    }
}

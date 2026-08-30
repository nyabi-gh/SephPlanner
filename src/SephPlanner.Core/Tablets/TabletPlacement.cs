using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>격자 위의 석판 하나.</summary>
    public sealed class TabletPlacement
    {
        public TabletDefinition Definition { get; set; } = new TabletDefinition();
        public GridPos Position { get; set; }
        public int Rotation { get; set; }

        /// <summary>커스텀 석판은 인스턴스마다 질의가 달라 런타임 값을 받아 쓴다.</summary>
        public string? InstanceQuery { get; set; }
        public string? InstanceConditionQuery { get; set; }

        public string Query => InstanceQuery ?? Definition.Query;
        public string ConditionQuery => InstanceConditionQuery ?? Definition.ConditionQuery;
    }
}

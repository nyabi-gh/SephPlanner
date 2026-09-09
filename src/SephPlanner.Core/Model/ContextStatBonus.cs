using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    public enum StatCountSource { QuickSlotCharms, StoneTablets, RowCategory }

    public sealed class ContextStatBonus
    {
        public StatCountSource Source { get; set; }
        public int SlotCount { get; set; }
        public string Category { get; set; } = "";
        public string StatusId { get; set; } = "";
        public List<double> AmountByLevel { get; set; } = new List<double>();
        public double? WorthPerUnit { get; set; }
    }
}

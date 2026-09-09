using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    public sealed class CharmStatEffect
    {
        public string StatusId { get; set; } = "";
        public List<int> AmountByLevel { get; set; } = new List<int>();
        public double? WorthPerUnit { get; set; }
        public int Samples { get; set; }
    }
}

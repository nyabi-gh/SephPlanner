using System;

namespace SephPlanner.Core.Solver
{
    internal readonly struct PlacementQuality : IComparable<PlacementQuality>
    {
        internal readonly int RetentionFailures, ActivationFailures, HoldFailures, PositionFailures, ComboMatches, UnsafeEmpty, Waste;
        internal readonly double ComboProgress, Value, Familiarity;

        internal PlacementQuality(int retentionFailures, int activationFailures, int holdFailures,
            int comboMatches, double comboProgress, double value, int unsafeEmpty, int waste, double familiarity, int positionFailures = 0)
        {
            RetentionFailures = retentionFailures;
            ActivationFailures = activationFailures;
            HoldFailures = holdFailures;
            PositionFailures = positionFailures;
            ComboMatches = comboMatches;
            ComboProgress = comboProgress;
            Value = value;
            UnsafeEmpty = unsafeEmpty;
            Waste = waste;
            Familiarity = familiarity;
        }

        internal static PlacementQuality From(Arrangement value) => new PlacementQuality(
            value.UnretainedCharms.Count + value.UnapprovedDeactivations.Count, value.UnpreservedCharms.Count, value.UnheldCharms.Count,
            value.PriorityComboMatches, value.PriorityComboProgress, value.Score,
            value.UnsafeEmptyCells, value.WastedLevels, value.Preference, value.UnpositionedCharms.Count);

        public int CompareTo(PlacementQuality other)
        {
            var order = other.RetentionFailures.CompareTo(RetentionFailures);
            if (order != 0) return order;
            order = other.HoldFailures.CompareTo(HoldFailures);
            if (order != 0) return order;
            order = ComboMatches.CompareTo(other.ComboMatches);
            if (order != 0) return order;
            order = Compare(ComboProgress, other.ComboProgress);
            if (order != 0) return order;
            order = other.ActivationFailures.CompareTo(ActivationFailures);
            if (order != 0) return order;
            order = other.PositionFailures.CompareTo(PositionFailures);
            if (order != 0) return order;
            order = Compare(Value, other.Value);
            if (order != 0) return order;
            order = other.UnsafeEmpty.CompareTo(UnsafeEmpty);
            if (order != 0) return order;
            order = other.Waste.CompareTo(Waste);
            return order != 0 ? order : Compare(Familiarity, other.Familiarity);
        }

        private static int Compare(double left, double right) =>
            left == right ? 0 : Math.Abs(left - right) <= 1e-9 ? 0 : left.CompareTo(right);
    }

    internal readonly struct AssignmentCost : IComparable<AssignmentCost>
    {
        internal readonly double Priority, Position, Value, Safety, Waste, Stability;
        internal AssignmentCost(double priority, double value, double safety = 0, double waste = 0, double stability = 0, double position = 0)
        { Priority = priority; Position = position; Value = value; Safety = safety; Waste = waste; Stability = stability; }

        public int CompareTo(AssignmentCost other)
        {
            var order = Priority.CompareTo(other.Priority);
            if (order != 0) return order;
            order = Position.CompareTo(other.Position);
            if (order != 0) return order;
            order = Value.CompareTo(other.Value);
            if (order != 0) return order;
            order = Safety.CompareTo(other.Safety);
            if (order != 0) return order;
            order = Waste.CompareTo(other.Waste);
            return order != 0 ? order : Stability.CompareTo(other.Stability);
        }

        public static AssignmentCost operator +(AssignmentCost a, AssignmentCost b) =>
            new AssignmentCost(a.Priority + b.Priority, a.Value + b.Value, a.Safety + b.Safety, a.Waste + b.Waste, a.Stability + b.Stability, a.Position + b.Position);
        public static AssignmentCost operator -(AssignmentCost a, AssignmentCost b) =>
            new AssignmentCost(a.Priority - b.Priority, a.Value - b.Value, a.Safety - b.Safety, a.Waste - b.Waste, a.Stability - b.Stability, a.Position - b.Position);
    }
}

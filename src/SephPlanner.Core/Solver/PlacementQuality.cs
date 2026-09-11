using System;

namespace SephPlanner.Core.Solver
{
    internal readonly struct PlacementQuality : IComparable<PlacementQuality>
    {
        internal readonly int RetentionFailures, ActivationFailures, HoldFailures, ComboMatches, UnsafeEmpty, Waste;
        internal readonly double ComboProgress, Value, Familiarity;

        internal PlacementQuality(int retentionFailures, int activationFailures, int holdFailures,
            int comboMatches, double comboProgress, double value, int unsafeEmpty, int waste, double familiarity)
        {
            RetentionFailures = retentionFailures;
            ActivationFailures = activationFailures;
            HoldFailures = holdFailures;
            ComboMatches = comboMatches;
            ComboProgress = comboProgress;
            Value = value;
            UnsafeEmpty = unsafeEmpty;
            Waste = waste;
            Familiarity = familiarity;
        }

        internal static PlacementQuality From(Arrangement value) => new PlacementQuality(
            value.UnretainedCharms.Count + value.UnapprovedDeactivations.Count + value.WrongSideCharms.Count, value.UnpreservedCharms.Count, value.UnheldCharms.Count,
            value.PriorityComboMatches, value.PriorityComboProgress, value.Score,
            value.UnsafeEmptyCells, value.WastedLevels, value.Preference);

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
            order = Compare(Quantize(Value), Quantize(other.Value));
            if (order != 0) return order;
            order = other.UnsafeEmpty.CompareTo(UnsafeEmpty);
            if (order != 0) return order;
            order = other.Waste.CompareTo(Waste);
            return order != 0 ? order : Compare(Familiarity, other.Familiarity);
        }

        /// <summary>
        /// 점수를 견줄 때 쓰는 눈금. 이보다 작은 차이는 배치의 우열로 보지 않는다.
        ///
        /// 점수의 단위인 "레벨"은 능력치 환산율에서 나오고 그 환산율 자체가 잰 값이다. 잰 값
        /// 107종의 레벨당 값어치 중앙값이 1.50 인데 정의상 1.00 이어야 하므로 척도 전체가 한 번
        /// 부풀어 있고, 능력치별 환산율도 다시 재면 0.7~5 배까지 움직인다(2026-09-11 측정,
        /// <c>DataTool --values</c>). 그러니 0.01 짜리 점수 차이는 배치의 우열이 아니라 환산의
        /// 잡음이다. 그런 차이가 아래 단계 - 감점 빈칸 정리, 초과 강화, 자리 유지 - 를 전부 눌러
        /// 버리면 같은 가방에서도 배치가 이유 없이 흔들린다.
        ///
        /// 크기는 재서 정했다. 카탈로그 전체에서 이웃 레벨 사이의 값어치 차이 515 개 중 가장
        /// 작은 것이 0.1677(얼음 날개 레벨 2→3)이고, 눈금이 그 아래인 동안은 실제 레벨 한 칸의
        /// 차이를 하나도 삼키지 않는다. 눈금 이상으로 벌어진 차이는 항상 다른 칸에 들어가므로
        /// 레벨 한 칸의 우열은 그대로 남는다.
        ///
        /// 허용 오차(<c>|a-b| &lt; tol</c>)가 아니라 눈금으로 끊는 것은 추이성 때문이다. 허용
        /// 오차는 a≈b, b≈c 인데 a&lt;c 가 될 수 있어 정렬 결과가 비교 순서에 따라 달라진다.
        /// </summary>
        internal const double ValueStep = 0.1;

        private static double Quantize(double value) => Math.Floor(value / ValueStep) * ValueStep;

        private static int Compare(double left, double right) =>
            left == right ? 0 : Math.Abs(left - right) <= 1e-9 ? 0 : left.CompareTo(right);
    }

    internal readonly struct AssignmentCost : IComparable<AssignmentCost>
    {
        internal readonly double Priority, Value, Safety, Waste, Stability;
        internal AssignmentCost(double priority, double value, double safety = 0, double waste = 0, double stability = 0)
        { Priority = priority; Value = value; Safety = safety; Waste = waste; Stability = stability; }

        public int CompareTo(AssignmentCost other)
        {
            var order = Priority.CompareTo(other.Priority);
            if (order != 0) return order;
            order = Value.CompareTo(other.Value);
            if (order != 0) return order;
            order = Safety.CompareTo(other.Safety);
            if (order != 0) return order;
            order = Waste.CompareTo(other.Waste);
            return order != 0 ? order : Stability.CompareTo(other.Stability);
        }

        public static AssignmentCost operator +(AssignmentCost a, AssignmentCost b) =>
            new AssignmentCost(a.Priority + b.Priority, a.Value + b.Value, a.Safety + b.Safety, a.Waste + b.Waste, a.Stability + b.Stability);
        public static AssignmentCost operator -(AssignmentCost a, AssignmentCost b) =>
            new AssignmentCost(a.Priority - b.Priority, a.Value - b.Value, a.Safety - b.Safety, a.Waste - b.Waste, a.Stability - b.Stability);
    }
}

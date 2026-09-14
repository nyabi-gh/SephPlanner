using System;

namespace SephPlanner.Core.Solver
{
    internal readonly struct PlacementQuality : IComparable<PlacementQuality>
    {
        internal readonly int RetentionFailures, ActivationFailures, HoldFailures, ComboMatches, SupportMatches, UnsafeEmpty, Waste;
        internal readonly double ComboProgress, Value, Familiarity;

        internal PlacementQuality(int retentionFailures, int activationFailures, int holdFailures,
            int comboMatches, double comboProgress, int supportMatches, double value, int unsafeEmpty, int waste,
            double familiarity, double scoreStep)
        {
            // 눈금은 견줄 때가 아니라 만들 때 씌운다 - 견주는 쪽은 CompareTo 라 눈금을 받을 자리가 없다.
            value = scoreStep > 0 ? Math.Floor(value / scoreStep) * scoreStep : value;
            RetentionFailures = retentionFailures;
            ActivationFailures = activationFailures;
            HoldFailures = holdFailures;
            ComboMatches = comboMatches;
            ComboProgress = comboProgress;
            SupportMatches = supportMatches;
            Value = value;
            UnsafeEmpty = unsafeEmpty;
            Waste = waste;
            Familiarity = familiarity;
        }

        internal static PlacementQuality From(Arrangement value) => new PlacementQuality(
            value.UnretainedCharms.Count + value.UnapprovedDeactivations.Count + value.WrongSideCharms.Count, value.UnpreservedCharms.Count, value.UnheldCharms.Count,
            value.PriorityComboMatches, value.PriorityComboProgress, value.SupportTargetMatches, value.Score,
            value.UnsafeEmptyCells, value.WastedLevels, value.Preference, value.ScoreStep);

        /// <summary>
        /// 점수를 견줄 때 눈금으로 끊는 이유. 점수의 단위인 "레벨"은 능력치 환산율에서 나오고 그
        /// 환산율 자체가 잰 값이라, 아주 작은 점수 차이는 배치의 우열이 아니라 환산의 잡음이다.
        /// 그런 차이가 아래 단계 - 감점 빈칸 정리, 초과 강화, 자리 유지 - 를 전부 눌러 버리면
        /// 같은 가방에서도 배치가 이유 없이 흔들린다.
        ///
        /// 허용 오차(<c>|a-b| &lt; tol</c>)가 아니라 눈금인 것은 추이성 때문이다. 허용 오차는
        /// a≈b, b≈c 인데 a&lt;c 가 될 수 있어 정렬 결과가 비교 순서에 따라 달라진다.
        ///
        /// 눈금 크기는 <see cref="WorthScale.ScoreStep"/>이 카탈로그에서 재어 준다.
        /// </summary>
        public int CompareTo(PlacementQuality other)
        {
            var order = other.RetentionFailures.CompareTo(RetentionFailures);
            if (order != 0) return order;
            order = other.HoldFailures.CompareTo(HoldFailures);
            if (order != 0) return order;
            // 강화 대상 지정이 콤보 우선보다 앞선다. 둘은 침 하나를 두고 부딪힌다 - 침은 대상의
            // 카테고리를 물려받으므로(게임 SearchCategory) "밀고 있는 카테고리를 보이는 대상"과
            // "지정한 대상"이 다르면 한쪽만 고를 수 있다. 이름을 콕 집은 지정이 카테고리 선호보다
            // 구체적이고, 콤보의 값어치는 점수에 이미 들어 있어 완전히 사라지지도 않는다.
            // 뒤집혀 있던 동안 침은 0레벨짜리 마법서를 강화하고 "지정한 대상에 닿는 배치를 찾지
            // 못했다"고 말했다(2026-09-14 제보).
            order = SupportMatches.CompareTo(other.SupportMatches);
            if (order != 0) return order;
            order = ComboMatches.CompareTo(other.ComboMatches);
            if (order != 0) return order;
            order = Compare(ComboProgress, other.ComboProgress);
            if (order != 0) return order;
            order = other.ActivationFailures.CompareTo(ActivationFailures);
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

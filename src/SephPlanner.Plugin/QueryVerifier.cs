using System.Collections.Generic;
using System.Text;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// Core 의 질의 파서를 게임 원본 <c>StoneTablet.ParseQuery</c>와 전수 대조한다.
    /// 오버레이는 게임 없이 돌아가야 하므로 파서를 포팅할 수밖에 없고, 그 포팅이 맞다는 근거가 이 검증이다.
    /// </summary>
    internal static class QueryVerifier
    {
        private static readonly int[] StorageValues = { 6, 12, 18, 24, 30, 36, 42 };
        private const int MaxReportedMismatches = 20;

        public sealed class Report
        {
            public int Comparisons;
            public int Mismatches;
            public readonly List<string> Details = new List<string>();

            public bool Passed => Mismatches == 0;
        }

        public static Report Run(IEnumerable<TabletDefinition> tablets)
        {
            var report = new Report();
            foreach (var _ in RunBatched(tablets, report)) { }
            return report;
        }

        /// <summary>
        /// 석판 하나 분량씩 끊어 검증한다. 전수 대조는 십만 회 규모라 한 프레임에 다 돌리면
        /// 게임이 멈추므로, 코루틴이 한 번 받을 때마다 한 프레임 쉬는 식으로 소비한다.
        /// </summary>
        public static IEnumerable<object> RunBatched(IEnumerable<TabletDefinition> tablets, Report report)
        {
            foreach (var tablet in tablets)
            {
                CompareQuery(report, tablet, tablet.Query, "query");
                CompareQuery(report, tablet, tablet.ConditionQuery, "conditionQuery");
                yield return null;
            }
        }

        private static void CompareQuery(Report report, TabletDefinition tablet, string query, string label)
        {
            if (string.IsNullOrEmpty(query)) return;

            var rotations = tablet.IsRotatable ? 4 : 1;

            foreach (var storage in StorageValues)
            {
                var grid = GridSpec.WithStorage(storage);

                for (var rotation = 0; rotation < rotations; rotation++)
                {
                    for (var index = 0; index < storage; index++)
                    {
                        var origin = grid.ToPosition(index);
                        var expected = StoneTablet.ParseQuery(
                            query, grid.Width, grid.Height, storage,
                            new ItemPosition((sbyte)origin.X, (sbyte)origin.Y), rotation, out _);
                        var actual = TabletQuery.Parse(query, grid, origin, rotation);

                        report.Comparisons++;
                        var difference = FirstDifference(expected, actual);
                        if (difference == null) continue;

                        report.Mismatches++;
                        if (report.Details.Count < MaxReportedMismatches)
                        {
                            report.Details.Add(
                                $"{tablet.Id}.{label} storage={storage} rot={rotation} origin={origin}: {difference}");
                        }
                    }
                }
            }
        }

        private static string FirstDifference(
            IList<StoneTablet.AdditionMetadata> expected, IList<QueryCell> actual)
        {
            if (expected.Count != actual.Count)
                return $"칸 수 {expected.Count} != {actual.Count}";

            for (var i = 0; i < expected.Count; i++)
            {
                var e = expected[i];
                var a = actual[i];

                if (e.position.x != a.Position.X || e.position.y != a.Position.Y)
                    return $"[{i}] 위치 ({e.position.x},{e.position.y}) != {a.Position}";
                if (e.value != a.Value)
                    return $"[{i}] 값 {e.value} != {a.Value}";
                if (e.isXWorldPosition != a.IsXWorldPosition || e.isYWorldPosition != a.IsYWorldPosition)
                    return $"[{i}] 월드 좌표 플래그 불일치";
                if (e.borderTop != a.BorderTop || e.borderRight != a.BorderRight ||
                    e.borderBottom != a.BorderBottom || e.borderLeft != a.BorderLeft)
                    return $"[{i}] 테두리 플래그 불일치";
            }
            return null;
        }

        public static string Format(Report report)
        {
            var text = new StringBuilder();
            text.AppendLine($"비교 {report.Comparisons}건, 불일치 {report.Mismatches}건");
            foreach (var detail in report.Details) text.AppendLine("  " + detail);
            if (report.Mismatches > report.Details.Count)
                text.AppendLine($"  ... 외 {report.Mismatches - report.Details.Count}건");
            return text.ToString();
        }
    }
}

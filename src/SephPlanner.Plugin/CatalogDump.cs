using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임에서 읽은 정적 데이터와 질의 검증 결과를 진단 도구가 읽을 수 있게 저장한다.
    ///
    /// 질의 전수 검증을 한 프레임에 다 하면 게임이 멈추므로 코루틴으로 나눠 실행한다.
    /// </summary>
    internal static class CatalogDump
    {
        private sealed class VerificationStatus
        {
            public string GameVersion = "";
            public int Comparisons;
            public int Mismatches;
        }

        private static bool? _queryVerified;

        public static IEnumerator WriteRoutine(Action<string> report)
        {
            _queryVerified = null;
            Directory.CreateDirectory(PlannerData.DataDirectory);

            var tablets = ItemCatalog.LoadTablets();
            yield return null;
            var charms = ItemCatalog.LoadCharms();
            yield return null;
            var combos = ItemCatalog.LoadCombos();
            yield return null;
            var measurement = ItemCatalog.LoadStatMeasurement();
            yield return null;

            // 아티팩트가 레벨마다 실제로 주는 값어치를 콤보와 같은 환산율로 재어 정의에 실어 둔다.
            var worth = CharmStatWorth.Apply(charms, measurement);
            yield return null;

            WriteJson(PlannerData.TabletDbFile, tablets);
            WriteJson(PlannerData.CharmDbFile, charms);
            WriteJson(PlannerData.ComboDbFile, combos);
            WriteJson(PlannerData.StatMeasurementFile, measurement);
            yield return null;

            var verification = new QueryVerifier.Report();
            foreach (var _ in QueryVerifier.RunBatched(tablets, verification))
                yield return null;
            WriteText(PlannerData.VerificationReportFile, QueryVerifier.Format(verification));
            WriteJson(PlannerData.VerificationStatusFile, new VerificationStatus
            {
                GameVersion = Application.version,
                Comparisons = verification.Comparisons,
                Mismatches = verification.Mismatches,
            });
            WriteText(PlannerData.CatalogVersionFile, PlannerData.CatalogVersion.ToString());
            _queryVerified = verification.Passed;

            report($"석판 {tablets.Count}종, 아티팩트 {charms.Count}종, 콤보 {combos.Count}종, " +
                   $"아티팩트 가치 {worth.ByEntity.Count}종 측정. 질의 검증 " +
                   $"{verification.Comparisons}건 중 불일치 {verification.Mismatches}건.");
        }

        /// <summary>
        /// 다시 덤프할 필요가 없는지. 파일이 있는지만 보면 덤프에 항목이 늘어났을 때 예전 덤프를
        /// 가진 사람은 새 항목이 영영 빈 채로 남는다.
        /// </summary>
        public static bool HasCatalog()
        {
            var required = new[]
            {
                PlannerData.TabletDbFile,
                PlannerData.CharmDbFile,
                PlannerData.ComboDbFile,
                PlannerData.StatMeasurementFile,
                PlannerData.VerificationReportFile,
                PlannerData.VerificationStatusFile,
            };
            foreach (var file in required)
            {
                var path = Path.Combine(PlannerData.DataDirectory, file);
                if (!File.Exists(path) || new FileInfo(path).Length == 0) return false;
            }

            var verification = ReadVerificationStatus();
            if (verification == null || verification.GameVersion != Application.version ||
                verification.Comparisons <= 0)
                return false;

            var stamp = Path.Combine(PlannerData.DataDirectory, PlannerData.CatalogVersionFile);
            if (!File.Exists(stamp)) return false;

            return int.TryParse(File.ReadAllText(stamp).Trim(), out var version)
                   && version == PlannerData.CatalogVersion;
        }

        public static bool QueryVerificationPassed()
        {
            if (_queryVerified.HasValue) return _queryVerified.Value;

            var status = ReadVerificationStatus();
            _queryVerified = status != null && status.GameVersion == Application.version &&
                             status.Comparisons > 0 && status.Mismatches == 0;
            return _queryVerified.Value;
        }

        private static VerificationStatus ReadVerificationStatus()
        {
            var path = Path.Combine(PlannerData.DataDirectory, PlannerData.VerificationStatusFile);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<VerificationStatus>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private static void WriteJson(string fileName, object value) =>
            WriteText(fileName, JsonConvert.SerializeObject(value, Formatting.Indented));

        /// <summary>
        /// 임시 파일에 쓰고 바꿔치기해 중단되더라도 잘린 파일을 남기지 않는다.
        /// </summary>
        private static void WriteText(string fileName, string content)
        {
            var path = Path.Combine(PlannerData.DataDirectory, fileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, content);

            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}

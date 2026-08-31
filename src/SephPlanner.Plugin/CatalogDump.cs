using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Solver;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임에서 읽은 정적 데이터를 오버레이가 쓸 수 있는 위치에 저장한다.
    /// 저장 위치는 사용자 PC 안이며, 배포물에는 포함하지 않는다.
    ///
    /// 아이콘 인코딩과 질의 전수 검증이 무거워서 한 프레임에 다 하면 게임이 수 초 멈춘다.
    /// 그래서 코루틴으로 프레임에 나눠 돌린다. 첫 실행마다 모든 사용자가 겪는 경로다.
    /// </summary>
    internal static class CatalogDump
    {
        public static IEnumerator WriteRoutine(Action<string> report)
        {
            Directory.CreateDirectory(IpcContract.DataDirectory);

            var tablets = ItemCatalog.LoadTablets();
            yield return null;
            var charms = ItemCatalog.LoadCharms();
            yield return null;
            var combos = ItemCatalog.LoadCombos();
            yield return null;
            var measurement = ItemCatalog.LoadStatMeasurement();
            yield return null;

            // 아티팩트가 레벨마다 실제로 주는 값어치를 여기서 한 번 재어 정의에 실어 둔다.
            // 오버레이가 매번 다시 재지 않아도 되고, 콤보 가중치와 같은 환산율로 잰 값이 된다.
            var worth = CharmStatWorth.Apply(charms, measurement);
            yield return null;

            WriteJson(IpcContract.TabletDbFile, tablets);
            WriteJson(IpcContract.CharmDbFile, charms);
            WriteJson(IpcContract.ComboDbFile, combos);
            WriteJson(IpcContract.StatMeasurementFile, measurement);
            WriteText(IpcContract.CatalogVersionFile, IpcContract.CatalogVersion.ToString());
            yield return null;

            var icons = 0;
            foreach (var count in IconDump.WriteBatched())
            {
                icons = count;
                yield return null;
            }

            var verification = new QueryVerifier.Report();
            foreach (var _ in QueryVerifier.RunBatched(tablets, verification))
                yield return null;
            WriteText(IpcContract.VerificationReportFile, QueryVerifier.Format(verification));

            report($"석판 {tablets.Count}종, 아티팩트 {charms.Count}종, 콤보 {combos.Count}종, " +
                   $"아이콘 {icons}개 저장. 아티팩트 가치 {worth.ByEntity.Count}종 측정. " +
                   $"질의 검증 {verification.Comparisons}건 중 불일치 {verification.Mismatches}건.");
        }

        /// <summary>
        /// 다시 덤프할 필요가 없는지. 파일이 있는지만 보면 덤프에 항목이 늘어났을 때 예전 덤프를
        /// 가진 사람은 새 항목이 영영 빈 채로 남는다.
        /// </summary>
        public static bool HasCatalog()
        {
            if (!File.Exists(Path.Combine(IpcContract.DataDirectory, IpcContract.TabletDbFile))) return false;

            var stamp = Path.Combine(IpcContract.DataDirectory, IpcContract.CatalogVersionFile);
            if (!File.Exists(stamp)) return false;

            return int.TryParse(File.ReadAllText(stamp).Trim(), out var version)
                   && version >= IpcContract.CatalogVersion;
        }

        private static void WriteJson(string fileName, object value) =>
            WriteText(fileName, JsonConvert.SerializeObject(value, Formatting.Indented));

        /// <summary>
        /// 임시 파일에 쓰고 바꿔치기한다. 바로 덮어쓰면 오버레이가 반쯤 쓰인 파일을 읽을 수 있다.
        /// </summary>
        private static void WriteText(string fileName, string content)
        {
            var path = Path.Combine(IpcContract.DataDirectory, fileName);
            var temp = path + ".tmp";
            File.WriteAllText(temp, content);

            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}

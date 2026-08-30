using System.IO;
using Newtonsoft.Json;
using SephPlanner.Core.Ipc;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 게임에서 읽은 정적 데이터를 오버레이가 쓸 수 있는 위치에 저장한다.
    /// 저장 위치는 사용자 PC 안이며, 배포물에는 포함하지 않는다.
    /// </summary>
    internal static class CatalogDump
    {
        public static string Write()
        {
            Directory.CreateDirectory(IpcContract.DataDirectory);

            var tablets = ItemCatalog.LoadTablets();
            var charms = ItemCatalog.LoadCharms();

            WriteJson(IpcContract.TabletDbFile, tablets);
            WriteJson(IpcContract.CharmDbFile, charms);

            var report = QueryVerifier.Run(tablets);
            File.WriteAllText(
                Path.Combine(IpcContract.DataDirectory, IpcContract.VerificationReportFile),
                QueryVerifier.Format(report));

            return $"석판 {tablets.Count}종, 아티팩트 {charms.Count}종 저장. " +
                   $"질의 검증 {report.Comparisons}건 중 불일치 {report.Mismatches}건.";
        }

        public static bool HasCatalog() =>
            File.Exists(Path.Combine(IpcContract.DataDirectory, IpcContract.TabletDbFile));

        private static void WriteJson(string fileName, object value)
        {
            var path = Path.Combine(IpcContract.DataDirectory, fileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));
        }
    }
}

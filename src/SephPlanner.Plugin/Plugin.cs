using System;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 세피리아 상태를 읽어 SephPlanner 오버레이로 내보내는 브리지. 게임 상태를 변경하지 않는다.
    /// </summary>
    [BepInPlugin(PluginGuid, "SephPlanner Bridge", "0.1.0")]
    public sealed class SephPlannerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.nyabi.sephplanner.bridge";

        private SnapshotPipeServer _server;
        private ConfigEntry<float> _pollInterval;
        private ConfigEntry<KeyboardShortcut> _dumpKey;
        private ConfigEntry<float> _offerRadius;
        private ConfigEntry<KeyboardShortcut> _diagnosticsKey;
        private float _nextPoll;
        private string _lastJson;
        private bool _catalogChecked;
        private string _lastSimulationIssue;
        private int _verifiedTablets;

        private void Awake()
        {
            _pollInterval = Config.Bind(
                "General", "PollIntervalSeconds", 0.25f,
                "인벤토리를 다시 읽는 주기(초).");
            _dumpKey = Config.Bind(
                "General", "DumpCatalogKey", new KeyboardShortcut(KeyCode.F9),
                "석판/아티팩트 데이터를 다시 덤프하고 질의 파서를 검증하는 단축키.");
            _diagnosticsKey = Config.Bind(
                "General", "DumpInventoryKey", new KeyboardShortcut(KeyCode.F10),
                "인벤토리 내용을 그대로 파일로 남기는 단축키. 인식 문제를 확인할 때 쓴다.");
            _offerRadius = Config.Bind(
                "General", "OfferRadius", 12f,
                "선택지로 볼 상자/상점까지의 거리. 넓히면 멀리 있는 것까지 추천에 들어온다.");

            _server = new SnapshotPipeServer(Logger.LogInfo);
            Logger.LogInfo("SephPlanner 브리지 시작 (읽기 전용)");
        }

        private void Update()
        {
            if (_dumpKey.Value.IsDown()) DumpCatalog();
            if (_diagnosticsKey.Value.IsDown()) DumpInventory();

            // 리소스는 부팅 직후 준비되므로 첫 프레임에 확인한다.
            if (!_catalogChecked)
            {
                _catalogChecked = true;
                if (!CatalogDump.HasCatalog()) DumpCatalog();
            }

            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + Mathf.Max(0.05f, _pollInterval.Value);
            PublishSnapshot();
        }

        private void DumpCatalog()
        {
            try
            {
                Logger.LogInfo(CatalogDump.Write());
            }
            catch (Exception ex)
            {
                Logger.LogError("데이터 덤프 실패: " + ex);
            }
        }

        private void DumpInventory()
        {
            try
            {
                var path = GameReader.DumpInventory();
                Logger.LogInfo(path == null ? "읽을 인벤토리가 없습니다." : "인벤토리 덤프: " + path);
            }
            catch (Exception ex)
            {
                Logger.LogError("인벤토리 덤프 실패: " + ex);
            }
        }

        private void PublishSnapshot()
        {
            try
            {
                var snapshot = GameReader.Read(_offerRadius.Value);
                VerifySimulation();

                var timestamp = snapshot.TimestampMs;
                snapshot.TimestampMs = 0;
                var json = JsonConvert.SerializeObject(snapshot);
                if (json == _lastJson) return;
                _lastJson = json;

                snapshot.TimestampMs = timestamp;
                _server.Publish(JsonConvert.SerializeObject(snapshot));
            }
            catch (Exception ex)
            {
                // 게임 업데이트로 내부 구조가 바뀌면 여기서 터진다. 게임을 죽이지 않고 물러선다.
                Logger.LogError("스냅샷 생성 실패: " + ex);
                _nextPoll = Time.unscaledTime + 5f;
            }
        }

        private void VerifySimulation()
        {
            var issue = GameReader.CheckSimulation();
            if (issue != _lastSimulationIssue)
            {
                _lastSimulationIssue = issue;
                if (issue != null) Logger.LogWarning("시뮬레이터 불일치: " + issue);
            }

            // 일치할 때 아무것도 남기지 않으면 검증이 돌았는지조차 알 수 없다.
            var checkedTablets = SimulationVerifier.LastCheckedTablets;
            if (issue == null && checkedTablets > _verifiedTablets)
            {
                _verifiedTablets = checkedTablets;
                Logger.LogInfo($"시뮬레이터 검증 통과 (석판 {checkedTablets}개 동시 배치까지)");
            }
        }

        private void OnDestroy() => _server?.Dispose();
    }
}

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
        private float _nextPoll;
        private string _lastJson;
        private bool _catalogChecked;
        private string _lastSimulationIssue;

        private void Awake()
        {
            _pollInterval = Config.Bind(
                "General", "PollIntervalSeconds", 0.25f,
                "인벤토리를 다시 읽는 주기(초).");
            _dumpKey = Config.Bind(
                "General", "DumpCatalogKey", new KeyboardShortcut(KeyCode.F9),
                "석판/아티팩트 데이터를 다시 덤프하고 질의 파서를 검증하는 단축키.");

            _server = new SnapshotPipeServer(Logger.LogInfo);
            Logger.LogInfo("SephPlanner 브리지 시작 (읽기 전용)");
        }

        private void Update()
        {
            if (_dumpKey.Value.IsDown()) DumpCatalog();

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

        private void PublishSnapshot()
        {
            try
            {
                var snapshot = GameReader.TryRead();
                if (snapshot == null) return;

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
            if (issue == _lastSimulationIssue) return;

            _lastSimulationIssue = issue;
            if (issue != null) Logger.LogWarning("시뮬레이터 불일치: " + issue);
        }

        private void OnDestroy() => _server?.Dispose();
    }
}

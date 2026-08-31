using System;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 세피리아 상태를 읽어 SephPlanner 오버레이로 내보내고, 싱글플레이에서는 오버레이가 요청한
    /// 자동 배치를 게임 자체의 이동 경로로 적용하는 브리지. 멀티 세션에서는 읽기만 한다.
    /// </summary>
    [BepInPlugin(PluginGuid, "SephPlanner Bridge", "0.1.0")]
    public sealed class SephPlannerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.nyabi.sephplanner.bridge";

        private SnapshotPipeServer _server;
        private CommandPipeServer _commands;
        private ConfigEntry<float> _pollInterval;
        private ConfigEntry<KeyboardShortcut> _dumpKey;
        private ConfigEntry<float> _offerRadius;
        private ConfigEntry<KeyboardShortcut> _diagnosticsKey;
        private float _nextPoll;
        private string _lastJson;
        private bool _catalogChecked;
        private string _lastSimulationIssue;
        private string _lastSephiriteReport;
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
            _commands = new CommandPipeServer(Logger.LogInfo);
            Logger.LogInfo("SephPlanner 브리지 시작");
        }

        private void Update()
        {
            if (_dumpKey.Value.IsDown()) StartDump();
            if (_diagnosticsKey.Value.IsDown()) DumpInventory();

            // 리소스는 부팅 직후 준비되므로 첫 프레임에 확인한다.
            if (!_catalogChecked)
            {
                _catalogChecked = true;
                if (!CatalogDump.HasCatalog()) StartDump();
            }

            DrainCommands();

            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + Mathf.Max(0.05f, _pollInterval.Value);
            PublishSnapshot();
        }

        private void DrainCommands()
        {
            while (_commands.TryDequeue(out var pending))
            {
                // 파이프 쪽 응답 대기는 이미 시간을 넘겼다. 층 이동 등으로 한참 뒤에야 꺼낸
                // 명령을 실행하면 그 사이 바뀐 인벤토리에 낡은 배치를 적용하게 된다.
                if (pending.AgeSeconds > 10)
                {
                    Logger.LogInfo("오래된 자동 배치 명령을 건너뜁니다.");
                    pending.Complete("명령이 너무 오래 기다려 실행하지 않았습니다. 다시 시도하세요.");
                    continue;
                }

                string result;
                try
                {
                    result = PlanApplier.Apply(pending.Command);
                    Logger.LogInfo(result);
                }
                catch (Exception ex)
                {
                    Logger.LogError("자동 배치 실패: " + ex);
                    result = "자동 배치 중 오류가 났습니다. BepInEx 로그를 확인하세요.";
                }

                // 결과가 오버레이 화면까지 가야 한다. 로그에만 남기면 무음 실패가 된다.
                pending.Complete(result);

                // 적용 결과가 화면에 바로 보이도록 다음 폴링을 기다리지 않는다.
                _nextPoll = 0;
            }
        }

        private bool _dumping;

        /// <summary>
        /// 아이콘 인코딩과 질의 전수 검증이 무거워, 한 프레임에 다 하면 게임이 수 초 멈춘다.
        /// 코루틴으로 프레임에 나눠 돌린다.
        /// </summary>
        private void StartDump()
        {
            if (_dumping)
            {
                Logger.LogInfo("데이터 덤프가 이미 진행 중입니다.");
                return;
            }
            StartCoroutine(DumpRoutine());
        }

        private System.Collections.IEnumerator DumpRoutine()
        {
            _dumping = true;

            // 이터레이터 안에서 던진 예외를 그대로 두면 코루틴이 죽으면서 _dumping 이 영영 참으로
            // 남는다. 한 걸음씩 감싸서 실패해도 플래그를 되돌린다.
            var steps = CatalogDump.WriteRoutine(Logger.LogInfo);
            while (true)
            {
                try
                {
                    if (!steps.MoveNext()) break;
                }
                catch (Exception ex)
                {
                    Logger.LogError("데이터 덤프 실패: " + ex);
                    break;
                }
                yield return steps.Current;
            }
            _dumping = false;
        }

        private void DumpInventory()
        {
            try
            {
                var path = GameReader.DumpInventory(_offerRadius.Value);
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
                ReportSephirites(snapshot);

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

        /// <summary>
        /// 근처 세피라이트의 상태를 남긴다. 무엇이 왜 후보에 못 들어갔는지 나중에 읽을 수 있어야 한다.
        /// </summary>
        private void ReportSephirites(SephPlanner.Core.Ipc.GameSnapshot snapshot)
        {
            var report = OfferReader.LastSephiriteReport;
            if (report == _lastSephiriteReport) return;

            _lastSephiriteReport = report;
            Logger.LogInfo($"세피라이트 {(report.Length == 0 ? "없음" : report)} -> 후보 {snapshot.Offers.Count}개");
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
                Logger.LogInfo($"시뮬레이터 검증 통과 - 석판 {checkedTablets}개와 칸별 레벨까지 일치");
            }
        }

        private void OnDestroy()
        {
            _server?.Dispose();
            _commands?.Dispose();
        }
    }
}

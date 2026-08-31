using System;
using BepInEx;
using BepInEx.Configuration;
using Newtonsoft.Json;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Planning;
using SephPlanner.Plugin.Ui;
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
        private ConfigEntry<bool> _nativePanel;
        private ConfigEntry<float> _nativePanelX;
        private ConfigEntry<float> _nativePanelY;
        private ConfigEntry<float> _nativePanelWidth;
        private ConfigEntry<KeyboardShortcut> _expandKey;
        private ConfigEntry<KeyboardShortcut> _autoPlaceKey;
        private float _nextPoll;
        private string _lastJson;
        private bool _catalogChecked;
        private string _lastSimulationIssue;
        private string _lastSephiriteReport;
        private int _verifiedTablets;

        private readonly NativeHud _hud = new NativeHud();
        private PlanRunner _runner;
        private GameSnapshot _lastSnapshot;
        private string _lastPanelOrigin;
        private string _lastPanelBlocker;
        private bool _expanded;
        private bool _hadOffers;
        private string _autoPlaceResult = "";
        private float _autoPlaceShownUntil;

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
            _nativePanel = Config.Bind(
                "NativePanel", "Enabled", true,
                "게임 HUD 안에 점수 패널을 직접 그린다. 별도 오버레이 창과 함께 써도 된다.");
            _nativePanelX = Config.Bind(
                "NativePanel", "OffsetX", 20f,
                "패널의 왼쪽 위 기준 가로 위치. 게임 HUD 와 겹치면 옮긴다.");
            _nativePanelY = Config.Bind(
                "NativePanel", "OffsetY", -20f,
                "패널의 왼쪽 위 기준 세로 위치. 음수가 아래쪽이다.");
            _nativePanelWidth = Config.Bind(
                "NativePanel", "Width", 300f,
                "패널의 가로 폭(캔버스 단위). 글씨가 잘리면 넓힌다.");
            // 게임이 쓰지 않는 키로 고른다. 게임은 수정키를 보지 않으므로 Ctrl+Alt 를 붙여도
            // 글자 키는 게임 조작을 함께 발동시킨다(docs/RESEARCH.md 의 "게임 단축키").
            // F 키는 게임이 하나도 쓰지 않으며 F9/F10 이 이미 같은 이유로 쓰이고 있다.
            _expandKey = Config.Bind(
                "NativePanel", "ExpandKey", new KeyboardShortcut(KeyCode.F7),
                "패널을 접고 펴는 단축키. 상자·상점을 열면 저절로 펼쳐진다.");
            _autoPlaceKey = Config.Bind(
                "NativePanel", "AutoPlaceKey", new KeyboardShortcut(KeyCode.F8),
                "제안된 배치를 게임에 적용하는 단축키. 싱글플레이에서만 동작한다.");

            _server = new SnapshotPipeServer(Logger.LogInfo);
            _commands = new CommandPipeServer(Logger.LogInfo);
            Logger.LogInfo("SephPlanner 브리지 시작");
        }

        private void Update()
        {
            if (_dumpKey.Value.IsDown()) StartDump();
            if (_diagnosticsKey.Value.IsDown()) DumpInventory();
            if (_nativePanel.Value)
            {
                if (_expandKey.Value.IsDown()) _expanded = !_expanded;
                if (_autoPlaceKey.Value.IsDown()) AutoPlace();
            }

            // 리소스는 부팅 직후 준비되므로 첫 프레임에 확인한다.
            if (!_catalogChecked)
            {
                _catalogChecked = true;
                if (!CatalogDump.HasCatalog()) StartDump();
            }

            DrainCommands();
            UpdateNativePanel();

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

            // 정의가 바뀌었을 수 있으므로 인게임 풀이가 쓰던 카탈로그를 버리고 다시 짓게 한다.
            CatalogSource.Invalidate();
            _runner = null;

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
                var changed = json != _lastJson;
                _lastJson = json;

                snapshot.TimestampMs = timestamp;
                FeedNativePanel(snapshot, changed);
                if (!changed) return;

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

        /// <summary>
        /// 인게임 화면이 쓸 배치를 푼다. 오버레이가 붙어 있든 아니든 도는데, 스냅샷이 그대로면
        /// 다시 풀지 않는다 - 같은 답을 얻자고 매번 빔 서치를 돌릴 이유가 없다.
        /// </summary>
        private void FeedNativePanel(GameSnapshot snapshot, bool changed)
        {
            _lastSnapshot = snapshot;
            if (!_nativePanel.Value) return;

            if (_runner == null)
            {
                // 카탈로그를 짓는 일은 리소스를 통째로 훑는 것이라 무겁다. 덤프가 끝나 정의가
                // 갖춰진 뒤에 한 번만 짓는다.
                if (_dumping || !CatalogDump.HasCatalog()) return;
                _runner = new PlanRunner(CatalogSource.Get(), new PlanPreferences());
            }

            if (changed || _runner.Latest == null) _runner.Submit(snapshot);
        }

        /// <summary>
        /// 화면을 만들고 최신 배치를 그린다. 만드는 것은 UI 가 준비된 뒤라야 되고, 씬이 바뀌면
        /// 화면도 함께 사라지므로 매 프레임 살아 있는지 확인해 다시 만든다.
        /// </summary>
        private void UpdateNativePanel()
        {
            if (!_nativePanel.Value)
            {
                _hud.SetVisible(false);
                return;
            }

            if (!_hud.TryCreate(_nativePanelX.Value, _nativePanelY.Value, _nativePanelWidth.Value))
            {
                // 런이 도는데도 못 붙었으면 무엇이 없어서인지 한 번은 남긴다. 조용히 안 뜨면
                // 게임 안에서는 확인할 길이 없다.
                if (_lastSnapshot?.Inventory != null && _hud.Blocker != _lastPanelBlocker)
                {
                    _lastPanelBlocker = _hud.Blocker;
                    Logger.LogWarning("인게임 화면을 만들지 못했습니다 - " + _hud.Blocker);
                }
                return;
            }
            if (_hud.Origin != _lastPanelOrigin)
            {
                _lastPanelOrigin = _hud.Origin;
                Logger.LogInfo("인게임 화면 생성 - " + _hud.Origin);
            }
            _hud.SetVisible(true);

            // 런 밖이나 죽은 뒤에는 보여 줄 배치가 없다. 직전 런의 점수를 남겨 두면 거짓말이 된다.
            // "탐험"은 게임 자체가 쓰는 말이다.
            if (_lastSnapshot?.Inventory == null)
            {
                _hud.RenderNotice("탐험 중이 아닙니다.");
                return;
            }

            var error = _runner?.Error;
            if (error != null)
            {
                _hud.RenderNotice("계산 실패 - " + error);
                return;
            }

            var plan = _runner?.Latest;
            if (plan == null)
            {
                _hud.RenderNotice(_runner == null ? "데이터 준비 중" : "계산 중");
                return;
            }

            AutoExpand(plan);
            _hud.Render(_lastSnapshot, plan, _expanded, Hint());
        }

        /// <summary>
        /// 무엇을 집을지 고르는 순간에는 격자와 후보를 다 봐야 한다. 상자나 상점이 열리면 저절로
        /// 펼치고 닫히면 되돌린다. 그 사이에 직접 접거나 편 것은 상황이 바뀔 때까지 그대로 둔다.
        /// </summary>
        private void AutoExpand(Plan plan)
        {
            var hasOffers = plan.Offers.Count > 0;
            if (hasOffers == _hadOffers) return;

            _hadOffers = hasOffers;
            _expanded = hasOffers;
        }

        /// <summary>
        /// 아래 한 줄. 누를 것이 없는 화면이라 무엇을 눌러야 하는지는 여기서만 알 수 있다.
        /// 자동 배치 결과도 잠깐 이 자리에 띄운다 - 로그에만 남기면 무음 실패가 된다.
        /// </summary>
        private string Hint()
        {
            if (Time.unscaledTime < _autoPlaceShownUntil) return _autoPlaceResult;

            var expand = Describe(_expandKey.Value) + (_expanded ? " 접기" : " 펼치기");
            if (_lastSnapshot != null && _lastSnapshot.IsMultiplayer) return expand;

            return expand + "   " + Describe(_autoPlaceKey.Value) + " 자동 배치";
        }

        private static string Describe(KeyboardShortcut shortcut)
        {
            var text = "";
            foreach (var modifier in shortcut.Modifiers) text += Short(modifier) + "+";
            return text + Short(shortcut.MainKey);
        }

        private static string Short(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftControl:
                case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt: return "Alt";
                case KeyCode.LeftShift:
                case KeyCode.RightShift: return "Shift";
                case KeyCode.Return: return "Enter";
                default: return key.ToString();
            }
        }

        /// <summary>
        /// 제안된 배치를 게임에 적용한다. 화면이 마우스를 받지 않으므로 버튼이 아니라 단축키다.
        /// 멀티 잠금과 사전 검증은 <see cref="PlanApplier"/>가 한다.
        /// </summary>
        private void AutoPlace()
        {
            var plan = _runner?.Latest;
            if (plan == null || plan.Targets.Count == 0 || plan.Moves.Count == 0)
            {
                Report("옮길 것이 없습니다.");
                return;
            }

            try
            {
                var result = PlanApplier.Apply(new ApplyPlanCommand { Targets = plan.Targets });
                Logger.LogInfo(result);
                Report(result);
            }
            catch (Exception ex)
            {
                Logger.LogError("자동 배치 실패: " + ex);
                Report("자동 배치 중 오류가 났습니다. BepInEx 로그를 확인하세요.");
            }

            // 적용 결과가 화면에 바로 보이도록 다음 폴링을 기다리지 않는다.
            _nextPoll = 0;
        }

        private void Report(string message)
        {
            _autoPlaceResult = message;
            _autoPlaceShownUntil = Time.unscaledTime + 6f;
        }

        private void OnDestroy()
        {
            _hud.Destroy();
            _server?.Dispose();
            _commands?.Dispose();
        }
    }
}

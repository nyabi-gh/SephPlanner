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
        private PluginSettings _settings;
        private float _nextPoll;
        private string _lastJson;
        private bool _catalogChecked;
        private string _lastSimulationIssue;
        private string _lastSephiriteReport;
        private int _verifiedTablets;

        private readonly NativeHud _hud = new NativeHud();
        private OptionsTab _options;
        private PlanRunner _runner;
        private GameSnapshot _lastSnapshot;
        private string _lastPanelOrigin;
        private string _lastPanelBlocker;
        private string _lastTabOrigin;
        private string _lastTabBlocker;
        private string _builtLayout;
        private bool _tabFailed;
        private float _appliedOpacity = -1f;
        private bool _expanded;
        private bool _hadOffers;
        private bool _moving;
        private string _autoPlaceResult = "";
        private float _autoPlaceShownUntil;

        private void Awake()
        {
            _settings = new PluginSettings(Config, Logger.LogInfo);
            _options = new OptionsTab(Logger.LogInfo);
            _server = new SnapshotPipeServer(Logger.LogInfo);
            _commands = new CommandPipeServer(Logger.LogInfo);
            Logger.LogInfo("SephPlanner 브리지 시작");
        }

        private void Update()
        {
            if (_settings.DumpKey.Value.IsDown()) StartDump();
            if (_settings.InventoryDumpKey.Value.IsDown()) DumpInventory();
            if (_settings.Panel.Value)
            {
                if (_settings.ExpandKey.Value.IsDown()) _expanded = !_expanded;
                if (_settings.AutoPlaceKey.Value.IsDown()) AutoPlace();
                if (_settings.OpacityKey.Value.IsDown()) _settings.CycleOpacity();
                if (_settings.MoveKey.Value.IsDown()) ToggleMove();
                if (_moving) _hud.DragTo(Cursor());
            }

            // 리소스는 부팅 직후 준비되므로 첫 프레임에 확인한다.
            if (!_catalogChecked)
            {
                _catalogChecked = true;
                if (!CatalogDump.HasCatalog()) StartDump();
            }

            DrainCommands();
            UpdateNativePanel();
            UpdateOptionsTab();

            if (Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + Mathf.Max(0.05f, _settings.PollInterval.Value);
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
                var path = GameReader.DumpInventory(_settings.OfferRadius.Value);
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
                var snapshot = GameReader.Read(_settings.OfferRadius.Value);
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
            if (!_settings.Panel.Value) return;

            if (_runner == null)
            {
                // 카탈로그를 짓는 일은 리소스를 통째로 훑는 것이라 무겁다. 덤프가 끝나 정의가
                // 갖춰진 뒤에 한 번만 짓는다.
                if (_dumping || !CatalogDump.HasCatalog()) return;
                _runner = new PlanRunner(CatalogSource.Get());
            }

            if (changed || _runner.Latest == null) _runner.Submit(snapshot, Preferences());
        }

        /// <summary>
        /// 지금 설정으로 푼다. 후보 추천을 끄면 가리는 것이 아니라 계산 자체를 건너뛴다 -
        /// 배치(정렬)는 손으로도 할 수 있는 일의 대행이지만 무엇을 집을지에 대한 조언은 판단을
        /// 빌려주는 것이라, 끄겠다고 한 사람에게는 답을 만들지도 않는 편이 정직하다.
        /// </summary>
        private PlanPreferences Preferences() =>
            new PlanPreferences { Recommendations = _settings.Recommendations.Value };

        /// <summary>
        /// 화면을 만들고 최신 배치를 그린다. 만드는 것은 UI 가 준비된 뒤라야 되고, 씬이 바뀌면
        /// 화면도 함께 사라지므로 매 프레임 살아 있는지 확인해 다시 만든다.
        /// </summary>
        private void UpdateNativePanel()
        {
            if (!_settings.Panel.Value)
            {
                _hud.SetVisible(false);
                return;
            }

            // 자리·크기는 지을 때 한 번 정해진다. 설정 탭이나 설정 파일에서 바뀌었으면 지금 것을
            // 버리고 다시 짓는 것이, 살아 있는 화면을 부분부분 고치는 것보다 어긋날 여지가 없다.
            if (_hud.IsAlive && _builtLayout != _settings.LayoutSignature) _hud.Destroy();

            if (!_hud.TryCreate(
                    _settings.Corner.Value,
                    new Vector2(_settings.MarginX.Value, _settings.MarginY.Value),
                    _settings.Width.Value, _settings.Scale.Value))
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

            if (_builtLayout != _settings.LayoutSignature)
            {
                _builtLayout = _settings.LayoutSignature;
                _appliedOpacity = -1f;
                if (_hud.Origin != _lastPanelOrigin)
                {
                    _lastPanelOrigin = _hud.Origin;
                    Logger.LogInfo("인게임 화면 생성 - " + _hud.Origin);
                }
            }

            // 불투명도는 짓지 않고도 바뀐다(단축키·설정 탭·설정 파일). 값이 달라졌을 때만 건다.
            if (Mathf.Abs(_appliedOpacity - _settings.Opacity.Value) > 0.001f)
            {
                _appliedOpacity = _settings.Opacity.Value;
                _hud.SetOpacity(_appliedOpacity);
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
        /// 게임 설정 창에 우리 탭을 붙인다. 화면이 꺼져 있어도 붙인다 - 다시 켜는 자리가 거기다.
        /// </summary>
        private void UpdateOptionsTab()
        {
            if (_tabFailed) return;

            // 줄 목록을 만드는 것은 붙일 때 한 번이면 된다. 이미 붙어 있는데 매 프레임 만들면
            // 쓰지도 않을 것을 프레임마다 쌓는 셈이다.
            if (_options.IsAttached)
            {
                _options.Update();
                return;
            }

            try
            {
                if (!_options.TryAttach(_settings.Rows()))
                {
                    // 설정 창은 씬에 따라 없을 수 있으므로 경고가 아니라 기록이다.
                    if (_options.Blocker != _lastTabBlocker)
                    {
                        _lastTabBlocker = _options.Blocker;
                        Logger.LogInfo("설정 탭을 붙이지 못했습니다 - " + _options.Blocker);
                    }
                    return;
                }

                if (_options.Origin != _lastTabOrigin)
                {
                    _lastTabOrigin = _options.Origin;
                    Logger.LogInfo("설정 탭 생성 - " + _options.Origin);
                }
            }
            catch (Exception ex)
            {
                // 게임 설정 창의 구조가 바뀌면 여기서 터진다. 설정 탭은 곁다리이므로 게임도
                // 나머지 기능도 함께 죽이지 않는다. 매 프레임 같은 예외를 되풀이하며 로그를
                // 채우지 않도록 한 번 실패하면 더 시도하지 않는다.
                _tabFailed = true;
                Logger.LogError("설정 탭 실패: " + ex);
            }
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
            // 이동 중에는 커서 좌표를 그대로 보여준다. 화면이 따라오지 않을 때 커서를 못 읽는
            // 것인지 자리가 안 먹는 것인지, 로그를 뒤지지 않고 화면에서 바로 갈린다.
            if (_moving)
            {
                var cursor = Cursor();
                return $"이동 중 - {Describe(_settings.MoveKey)} 로 고정   커서 {cursor.x:0},{cursor.y:0}";
            }

            if (Time.unscaledTime < _autoPlaceShownUntil) return _autoPlaceResult;

            var expand = Describe(_settings.ExpandKey) + (_expanded ? " 접기" : " 펼치기");
            var look = Describe(_settings.OpacityKey) + " 불투명도   " + Describe(_settings.MoveKey) + " 이동";
            if (_lastSnapshot != null && _lastSnapshot.IsMultiplayer) return expand + "   " + look;

            return expand + "   " + Describe(_settings.AutoPlaceKey) + " 자동 배치   " + look;
        }

        private static string Describe(ConfigEntry<KeyboardShortcut> entry) =>
            PluginSettings.Describe(entry.Value);

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

        /// <summary>
        /// 커서 위치. 게임이 새 InputSystem 을 쓰므로 그쪽을 먼저 본다. 구식 <c>Input</c> 은
        /// 프로젝트 설정에 따라 마우스만 죽어 있을 수 있고, 그러면 화면이 커서를 따라오지 않고
        /// 제자리에 선다. 단축키가 구식으로도 잘 먹으므로 폴백으로 남긴다.
        /// </summary>
        private static Vector2 Cursor()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.position.ReadValue() : (Vector2)Input.mousePosition;
        }

        /// <summary>
        /// 화면을 커서로 옮긴다. 끌어서 옮기려면 마우스를 받아야 하는데 그러면 게임 조작을
        /// 가로채게 되므로, 잡고 놓는 것만 단축키로 하고 그 사이에는 커서 위치를 읽기만 한다.
        /// </summary>
        private void ToggleMove()
        {
            if (!_hud.IsAlive) return;

            _moving = !_moving;
            if (_moving)
            {
                _hud.BeginDrag(Cursor());
                Report("이동 중 - 마우스로 옮기고 " + Describe(_settings.MoveKey) + " 로 고정");
                Logger.LogInfo($"이동 모드 시작 - 커서 {Cursor()}");
                return;
            }

            var margin = _hud.Margin;
            _settings.MarginX.Value = margin.x;
            _settings.MarginY.Value = margin.y;

            // 화면은 이미 그 자리에 가 있다. 여백이 바뀌었다고 다시 짓게 두면 같은 자리에
            // 같은 것을 짓느라 한 프레임 깜빡일 뿐이다.
            _builtLayout = _settings.LayoutSignature;
            Report("자리를 기억했습니다.");

            // 커서가 움직이지 않으면 화면도 제자리에 선다. 그때 무엇이 잘못인지는 좌표를 봐야 안다.
            Logger.LogInfo($"이동 모드 끝 - 커서 {Cursor()} 여백 {margin}");
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

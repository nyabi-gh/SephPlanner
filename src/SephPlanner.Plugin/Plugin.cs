using System;
using System.Collections.Generic;
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
        private SettingsWindow _window;
        private BuildWindow _build;
        private PluginPreferences _prefs;
        private PlanRunner _runner;
        private GameSnapshot _lastSnapshot;
        private string _lastPanelOrigin;
        private string _lastPanelBlocker;
        private string _lastRenderError;
        private string _lastWindowOrigin;
        private string _lastCatalogError = "";
        private string _lastWindowBlocker;
        private string _builtLayout;
        private float _appliedOpacity = -1f;
        private bool _expanded;
        private bool _hidden;
        private bool _hadOffers;
        private bool _moving;

        /// <summary>지금 미리보고 있는 후보. 빈 문자열이면 미리보기가 꺼져 있다.</summary>
        private string _previewKey = "";

        /// <summary>마지막으로 풀 때 쓴 빌드 지정. 달라졌으면 스냅샷이 그대로여도 다시 푼다.</summary>
        private int _solvedRevision = -1;

        /// <summary>솔버가 바빠 받아주지 못한 변경이 남아 있다. 받아줄 때까지 다시 낸다.</summary>
        private bool _resubmit;
        private string _autoPlaceResult = "";
        private float _autoPlaceShownUntil;

        private void Awake()
        {
            _settings = new PluginSettings(Config, Logger.LogInfo);
            _prefs = PluginPreferences.Load(Logger.LogWarning);
            _window = new SettingsWindow(_settings.Rows);
            _build = new BuildWindow(_prefs, CurrentBuild);
            _server = new SnapshotPipeServer(Logger.LogInfo);
            _commands = new CommandPipeServer(Logger.LogInfo);
            Logger.LogInfo("SephPlanner 브리지 시작");
        }

        private void Update()
        {
            if (_settings.DumpKey.Value.IsDown()) StartDump();
            if (_settings.InventoryDumpKey.Value.IsDown()) DumpInventory();
            // 화면 스위치 밖이어야 한다. 화면을 끈 뒤 이 키까지 죽으면 되켤 길이 없다.
            if (_settings.SettingsKey.Value.IsDown()) ToggleWindow(_window, _settings.SettingsKey, "설정 창");
            if (_settings.BuildKey.Value.IsDown()) ToggleWindow(_build, _settings.BuildKey, "빌드 창");
            if (_settings.Panel.Value)
            {
                if (_settings.PreviewKey.Value.IsDown()) CyclePreview();
                if (_settings.HideKey.Value.IsDown()) ToggleHidden();
                if (_settings.ExpandKey.Value.IsDown())
                {
                    _expanded = !_expanded;

                    // 접으면 안내 줄이 사라진다. 접는 순간만큼은 어떻게 되돌리는지 보여야 한다.
                    Report(Guide());
                }
                if (_settings.AutoPlaceKey.Value.IsDown()) AutoPlace();
                if (_settings.OpacityKey.Value.IsDown()) _settings.CycleOpacity();
                if (_settings.MoveKey.Value.IsDown()) ToggleMove();
                if (_moving) _hud.DragTo(Cursor());

                // 커서를 읽기만 한다. 그려 둔 사각형과 겹치는지 우리가 세므로 raycastTarget 을
                // 켤 필요가 없고, HUD 가 게임 입력을 가져가지 않는다는 보장이 그대로 남는다.
                _hud.UpdateHover(Cursor(), !_hidden && !_moving && !_window.IsOpen && !_build.IsOpen);
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

                // 부팅 직후에는 게임의 로컬라이제이션이 아직 준비되지 않아 지을 수 없다.
                // 그때는 물러서서 다음 폴링에 다시 짓는다.
                var catalog = CatalogSource.Get();
                if (catalog == null)
                {
                    if (CatalogSource.LastError != _lastCatalogError)
                    {
                        _lastCatalogError = CatalogSource.LastError;
                        Logger.LogInfo("데이터를 아직 짓지 못했습니다. 다음에 다시 시도합니다 - " +
                                       CatalogSource.LastError);
                    }
                    return;
                }
                _lastCatalogError = "";
                _runner = new PlanRunner(catalog);
            }

            // 빌드 지정이 바뀌면 스냅샷이 그대로여도 답이 달라진다. 그대로 두면 창에서 콤보를
            // 켠 것이 다음에 물건을 옮길 때까지 아무 일도 하지 않는 것처럼 보인다.
            var stale = _solvedRevision != _prefs.Revision;
            if (!changed && !stale && !_resubmit && _runner.Latest != null) return;

            // 거절된 변경은 _lastJson 이 이미 갱신돼 다음 폴링에 "그대로"로 보인다.
            // 받아들여질 때까지 최신 스냅샷으로 다시 낸다.
            if (_runner.Submit(snapshot, Preferences()))
            {
                _solvedRevision = _prefs.Revision;
                _resubmit = false;
            }
            else
            {
                _resubmit = true;
            }
        }

        /// <summary>빌드 창이 목록을 채울 재료. 창은 열려 있는 동안 시간이 멈추므로 그때 한 번 읽는다.</summary>
        private BuildContext CurrentBuild() => new BuildContext
        {
            Catalog = _runner != null ? CatalogSource.Get() : null,
            Snapshot = _lastSnapshot,
            Plan = _runner != null ? _runner.Latest : null,
            Recommendations = _settings.Recommendations.Value,
        };

        /// <summary>
        /// 지금 설정으로 푼다. 후보 추천을 끄면 가리는 것이 아니라 계산 자체를 건너뛴다 -
        /// 배치(정렬)는 손으로도 할 수 있는 일의 대행이지만 무엇을 집을지에 대한 조언은 판단을
        /// 빌려주는 것이라, 끄겠다고 한 사람에게는 답을 만들지도 않는 편이 정직하다.
        /// </summary>
        private PlanPreferences Preferences() =>
            _prefs.ToPreferences(_settings.Recommendations.Value);

        /// <summary>
        /// 화면을 만들고 최신 배치를 그린다. 만드는 것은 UI 가 준비된 뒤라야 되고, 씬이 바뀌면
        /// 화면도 함께 사라지므로 매 프레임 살아 있는지 확인해 다시 만든다.
        /// </summary>
        private void UpdateNativePanel()
        {
            if (!_settings.Panel.Value || _hidden)
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

                // 접힌 화면은 안내 줄을 물고 있지 않다. 처음 뜰 때 잠깐 보여 주지 않으면
                // 무엇을 눌러야 하는지 알 길이 없다.
                Report(Guide());
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
                _hud.RenderNotice(Waiting());
                return;
            }

            AutoExpand(plan);

            var preview = PreviewName(plan);
            try
            {
                Render(plan, preview);
            }
            catch (Exception ex)
            {
                // 유니티 쪽 예외는 Player.log 에만 쌓여 우리 로그가 조용하다. 매 프레임 도는
                // 자리라 같은 예외는 한 번만 남긴다.
                var message = ex.GetType().Name + ": " + ex.Message;
                if (message == _lastRenderError) return;

                _lastRenderError = message;
                Logger.LogError("화면 그리기 실패 - " + ex);
            }
        }

        private void Render(Plan plan, string preview)
        {
            _hud.Render(new HudFrame
            {
                Snapshot = _lastSnapshot,
                Plan = plan,
                Catalog = CatalogSource.Get(),
                Prefs = _prefs,
                Values = CharmValueSource.Book,
                Expanded = _expanded,
                Recommendations = _settings.Recommendations.Value,
                Hint = Hint(plan, preview),
                HintIsPreview = preview != null,
                PreviewKey = _previewKey,
            });
        }

        /// <summary>
        /// 배치가 없을 때 무엇을 기다리는 중인지.
        ///
        /// 예전에는 전부 "계산 중"이라고 했는데, 풀 것이 없어서 답이 안 나온 경우까지 그렇게
        /// 말해서 영영 계산만 하는 것처럼 보였다. 탐험을 새로 시작해도 아티팩트를 하나 줍기
        /// 전까지는 풀 것이 없으므로 늘 그 상태다.
        /// </summary>
        private string Waiting()
        {
            if (_runner == null) return "데이터 준비 중";

            switch (_runner.Blocker)
            {
                case PlanBlocker.NoCharms:
                    return "가방에 아티팩트가 없습니다. 하나 주우면 배치를 계산합니다.";

                case PlanBlocker.UnknownItems:
                    return "가방의 물건 중 아는 아티팩트가 없습니다. " +
                           Describe(_settings.DumpKey) + " 로 데이터를 다시 만들어 보세요.";

                case PlanBlocker.NoInventory:
                    return "가방을 읽지 못했습니다.";

                default:
                    return "계산 중";
            }
        }

        /// <summary>
        /// 지금 미리보고 있는 후보의 이름. 고른 것이 새 계획에서 사라졌으면 미리보기를 접는다 -
        /// 상자를 닫았는데 없는 후보의 격자를 계속 보여주면 그것이 지금 배치인 줄 알게 된다.
        /// </summary>
        private string PreviewName(Plan plan)
        {
            if (_previewKey.Length == 0) return null;

            foreach (var advice in plan.Offers)
            {
                if (advice.Key == _previewKey && advice.Preview != null) return advice.Candidate.Name;
            }

            _previewKey = "";
            return null;
        }

        /// <summary>
        /// 후보를 차례로 미리본다. 마지막 다음은 미리보기 없음이라, 한 키만으로 켜고 끌 수 있다.
        /// 자리를 번호가 아니라 후보의 열쇠로 기억하는 것은, 계획이 다시 풀려 순서가 달라져도
        /// 보고 있던 것을 계속 보고 있어야 하기 때문이다.
        /// </summary>
        private void CyclePreview()
        {
            var plan = _runner != null ? _runner.Latest : null;
            var keys = new List<string>();
            if (plan != null)
            {
                for (var i = 0; i < plan.Offers.Count && i < NativeHud.OfferRows; i++)
                {
                    if (plan.Offers[i].Preview != null) keys.Add(plan.Offers[i].Key);
                }
            }

            if (keys.Count == 0)
            {
                _previewKey = "";
                Report(_settings.Recommendations.Value
                    ? "미리볼 후보가 없습니다."
                    : "후보 추천이 꺼져 있습니다. " + Describe(_settings.SettingsKey) + " 에서 켤 수 있습니다.");
                return;
            }

            var index = keys.IndexOf(_previewKey);
            _previewKey = index + 1 < keys.Count ? keys[index + 1] : "";

            // 펼쳐 두지 않으면 격자가 보이지 않아 미리보기가 아무것도 바꾸지 않는 것처럼 보인다.
            if (_previewKey.Length > 0) _expanded = true;
        }

        /// <summary>
        /// 창을 열고 닫는다. 화면이 꺼져 있어도 열린다 - 다시 켜는 자리가 거기다.
        /// </summary>
        private void ToggleWindow(PlannerWindow window, ConfigEntry<KeyboardShortcut> key, string label)
        {
            try
            {
                window.Toggle(Describe(key) + " 또는 ESC 로 닫기");

                if (window.Blocker.Length > 0)
                {
                    var blocker = label + " - " + window.Blocker;
                    if (blocker == _lastWindowBlocker) return;

                    _lastWindowBlocker = blocker;
                    Logger.LogWarning(label + "을 만들지 못했습니다 - " + window.Blocker);
                    return;
                }

                var origin = label + " - " + window.Origin;
                if (origin != _lastWindowOrigin)
                {
                    _lastWindowOrigin = origin;
                    Logger.LogInfo(label + " 생성 - " + window.Origin);
                }
            }
            catch (Exception ex)
            {
                // 창은 곁다리다. 여기서 터져도 표시와 자동 배치는 계속 돌아야 한다.
                Logger.LogError(label + " 실패: " + ex);
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
        private string Hint(Plan plan, string preview)
        {
            // 이동 중에는 커서 좌표를 그대로 보여준다. 화면이 따라오지 않을 때 커서를 못 읽는
            // 것인지 자리가 안 먹는 것인지, 로그를 뒤지지 않고 화면에서 바로 갈린다.
            if (_moving)
            {
                var cursor = Cursor();
                return $"이동 중 - {Describe(_settings.MoveKey)} 로 고정   커서 {cursor.x:0},{cursor.y:0}";
            }

            if (Time.unscaledTime < _autoPlaceShownUntil) return _autoPlaceResult;

            // 미리보기는 화면이 지금 무엇을 그리고 있는지를 바꾼다. 그 사실이 늘 보이지 않으면
            // 미리보기 격자를 지금 배치로 착각하게 된다.
            if (preview != null)
            {
                return $"미리보기 - {preview} 을(를) 집었을 때   금색 테두리 = 달라지는 자리   " +
                       Describe(_settings.PreviewKey) + " 로 다음 후보";
            }

            // 접었을 때는 안내 줄도 접는다. 게임 화면을 가리지 않는 것이 접는 이유인데 안내가
            // 늘 붙어 있으면 줄어드는 것이 반뿐이다. 키를 누르면 잠깐 다시 뜬다.
            return _expanded ? Guide() : "";
        }

        /// <summary>
        /// 무엇을 누르면 되는지. 지금 할 수 있는 것만 적는다 - 멀티에서 자동 배치를, 후보가
        /// 없을 때 미리보기를 적어 두면 눌러도 아무 일이 없는 키를 알려주는 셈이 된다.
        /// </summary>
        private string Guide()
        {
            var plan = _runner != null ? _runner.Latest : null;
            var text = Describe(_settings.ExpandKey) + (_expanded ? " 접기" : " 펼치기");

            if (_lastSnapshot == null || !_lastSnapshot.IsMultiplayer)
                text += "   " + Describe(_settings.AutoPlaceKey) + " 자동 배치";

            if (plan != null && plan.Offers.Count > 0)
                text += "   " + Describe(_settings.PreviewKey) + " 후보 미리보기";

            return text +
                   "   " + Describe(_settings.BuildKey) + " 빌드" +
                   "   " + Describe(_settings.OpacityKey) + " 불투명도" +
                   "   " + Describe(_settings.MoveKey) + " 이동" +
                   "   " + Describe(_settings.HideKey) + " 숨기기" +
                   "   " + Describe(_settings.SettingsKey) + " 설정";
        }

        /// <summary>
        /// 화면을 통째로 숨긴다. 계산은 그대로 돌려 둔다 - 다시 켰을 때 "계산 중"부터 보는
        /// 대신 곧바로 최신 배치가 뜬다. 아예 쓰지 않을 것이면 설정에서 끄는 쪽이 맞다.
        /// </summary>
        private void ToggleHidden()
        {
            _hidden = !_hidden;
            if (_hidden)
            {
                // 숨긴 뒤에는 화면에 알릴 자리가 없다. 되돌릴 키는 로그에만 남는다.
                Logger.LogInfo($"인게임 화면을 숨겼습니다. {Describe(_settings.HideKey)} 로 다시 보입니다.");
                return;
            }

            Report(Guide());
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
            _window.Destroy();
            _build.Destroy();
            _server?.Dispose();
            _commands?.Dispose();
        }
    }
}

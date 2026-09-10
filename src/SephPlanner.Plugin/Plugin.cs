using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using Mirror;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Plugin.Ui;
using UnityEngine;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 세피리아 상태를 읽어 게임 HUD에 배치와 추천을 표시하고, 싱글플레이에서는 제안된 배치를
    /// 게임 자체의 이동 경로로 적용한다. 멀티 세션에서는 읽기만 한다.
    /// </summary>
    [BepInPlugin(PluginGuid, "SephPlanner", "0.3.0")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001", Justification = "Unity의 OnDestroy에서 계산 작업을 정리합니다.")]
    public sealed class SephPlannerPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "dev.nyabi.sephplanner.bridge";

        private PluginSettings _settings;
        private float _nextPoll;
        private bool _catalogChecked;
        private bool _catalogRetryArmed;
        private bool _inRun;
        private PlanVerificationStatus _simulationVerification;
        private string _simulationReason = "실시간 시뮬레이션 검증이 아직 완료되지 않았습니다.";
        private string _lastSephiriteReport;
        private int _verifiedTablets;

        private readonly NativeHud _hud = new NativeHud();
        private SettingsWindow _window;
        private BuildWindow _build;
        private PluginPreferences _prefs;
        private PlanRunner _runner;
        private int _submittedPreferencesRevision = -1;
        private GameSnapshot _lastSnapshot;
        private DiagnosticConsentWindow _diagnosticWindow;
        private DiagnosticCapture _pendingDiagnostic;
        private DiagnosticText _diagnosticLog;
        private DiagnosticUploadClient _diagnosticClient;
        private CancellationTokenSource _diagnosticCancellation;
        private Task<string> _diagnosticTask;
        private readonly DiagnosticUploadThrottle _diagnosticThrottle = new DiagnosticUploadThrottle(DiagnosticUploadThrottle.DefaultInterval);
        private string _diagnosticNotice;
        private float _diagnosticNoticeUntil;
        private string _lastPanelOrigin;
        private string _lastPanelBlocker;
        private readonly Dictionary<string, string> _lastErrors = new Dictionary<string, string>();
        private string _lastWindowOrigin;
        private string _lastCatalogError = "";
        private string _lastWindowBlocker;
        private PanelLayout _builtLayout;
        private float _appliedOpacity = -1f;
        private bool _expanded;
        private bool _hidden;
        private readonly AdvicePanelExpansion _adviceExpansion = new AdvicePanelExpansion();
        private bool _moving;
        private bool _panelEnabledLastFrame;
        private bool _panelAwaitingRefresh;

        /// <summary>지금 미리보고 있는 후보. 빈 문자열이면 미리보기가 꺼져 있다.</summary>
        private string _previewKey = "";

        private string _currentPlacementFingerprint = "";
        private string _currentCatalogGeneration = "";
        private string _autoPlaceResult = "";
        private float _autoPlaceShownUntil;

        private void Awake()
        {
            _settings = new PluginSettings(Config, Logger.LogInfo, () => OpenDiagnosticConsent(null));
            _prefs = PluginPreferences.Load(Logger.LogWarning);
            _window = new SettingsWindow(_settings.Rows);
            _build = new BuildWindow(_prefs, CurrentBuild);
            _diagnosticWindow = new DiagnosticConsentWindow(ChooseDiagnosticConsent);
            _diagnosticLog = new DiagnosticText(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Paths.GameRootPath);
            _diagnosticClient = new DiagnosticUploadClient();
            _settings.DiagnosticConsent.SettingChanged += (_, _) =>
            {
                if (!_settings.DiagnosticUploadAllowed) _diagnosticCancellation?.Cancel();
            };
            Logger.LogEvent += CaptureOwnLog;
            Logger.LogInfo(PluginIdentity.Describe());
        }

        private void Update()
        {
            var panelEnabled = _settings.Panel.Value;
            if (panelEnabled && !_panelEnabledLastFrame) _panelAwaitingRefresh = true;
            _panelEnabledLastFrame = panelEnabled;

            FrameCost.CountFrame();

            // 단축키·화면 쪽에서 난 예외가 폴링까지 굶기면 안 된다. Update 안의 예외는 유니티가
            // Player.log 에만 쌓고 우리 로그는 조용하므로, 여기서 잡아 같은 것 한 번씩 남긴다.
            Guarded(HandleInput, "입력 처리");
            Guarded(() => _build.RefreshIfChanged(), "빌드 결과 갱신");
            Guarded(CheckDiagnosticUpload, "진단 전송 상태");

            var panelStarted = FrameCost.Now;
            Guarded(UpdateNativePanel, "화면 갱신");
            FrameCost.Panel.Add(panelStarted);

            ReportFrameCost();

            if (Time.unscaledTime < _nextPoll) return;
            // 하한은 설정의 범위가 지킨다. 여기서 다시 자르면 그 범위가 무슨 값인지 두 군데에 적힌다.
            _nextPoll = Time.unscaledTime + _settings.PollInterval.Value;

            // 적용 중에는 격자가 걸음마다 바뀐다. 그때마다 다시 풀면 돌고 있던 빔 서치를 취소하고
            // 새로 시작하는 일을 초당 몇 번씩 하게 된다 - 하필 서버 왕복을 기다리는 동안이다.
            // 끝나면 AutoPlaceFinished 가 _nextPoll 을 0 으로 두어 곧바로 다시 읽는다.
            if (PlanApplier.InProgress) return;
            PollGameState();
        }

        private float _nextCostReport;

        /// <summary>
        /// 메인 스레드 부담을 이따금 로그에 남긴다. F10 을 누르지 않아도 숫자가 남아야, 끊긴다는
        /// 제보를 받았을 때 추측 대신 로그로 답할 수 있다.
        /// </summary>
        private void ReportFrameCost()
        {
            if (Time.unscaledTime < _nextCostReport) return;

            // 첫 보고는 한 바퀴 돌고 나서 한다. 부팅 직후의 몇 프레임은 대표값이 아니다.
            var first = _nextCostReport == 0f;
            _nextCostReport = Time.unscaledTime + 300f;
            if (first || FrameCost.Poll.Count == 0) return;

            Logger.LogInfo(FrameCost.Summary());
        }

        /// <summary>
        /// 이 자리에서 처음 보는 오류인가. 매 프레임 도는 자리들이라 같은 것을 되풀이해 남기면
        /// 로그가 못 쓰게 된다.
        ///
        /// <b>자리마다 따로 센다.</b> 예전에는 입력·화면 갱신·그리기가 마지막 오류 하나를 함께
        /// 썼는데, 서로 다른 둘이 번갈아 나면 억제가 아예 듣지 않아 프레임마다 두 줄씩 쌓였다.
        /// </summary>
        private bool NewError(string label, string message)
        {
            if (_lastErrors.TryGetValue(label, out var previous) && previous == message) return false;

            _lastErrors[label] = message;
            return true;
        }

        private void Guarded(Action step, string label)
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                if (!NewError(label, ex.GetType().Name + ": " + ex.Message)) return;

                Logger.LogError(label + " 실패 - " + ex);
            }
        }

        private void HandleInput()
        {
            if (_window.PollShortcutCapture()) return;
            if (_settings.DumpKey.Value.IsDown()) StartDump();
            if (_settings.InventoryDumpKey.Value.IsDown()) DumpInventory();
            // 화면 스위치 밖이어야 한다. 화면을 끈 뒤 이 키까지 죽으면 되켤 길이 없다.
            if (_settings.SettingsKey.Value.IsDown()) ToggleWindow(_window, _settings.SettingsKey, "설정 창");
            if (_settings.BuildKey.Value.IsDown()) ToggleWindow(_build, _settings.BuildKey, "빌드 창");
            if (_build.IsOpen && !_window.IsOpen && RightClicked()) _build.RightClick(Cursor());
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
                _hud.UpdateHover(Cursor(), !_hidden && !_moving && !_window.IsOpen && !_build.IsOpen && !_diagnosticWindow.IsOpen);
            }

            // 리소스는 부팅 직후 준비되므로 첫 프레임에 확인한다.
            if (!_catalogChecked)
            {
                _catalogChecked = true;
                _catalogRetryArmed = true;
            }

            if (_catalogRetryArmed && !_dumping)
            {
                _catalogRetryArmed = false;
                if (!CatalogDump.HasCatalog()) StartDump();
            }
        }

        /// <summary>
        /// 카탈로그가 없는 채로 남았으면 다음 런 시작에 한 번 더 시도한다. 부팅 때의 확인은
        /// 한 번뿐이라, 그때 실패하면 F9 를 누르기 전까지 아무것도 되지 않는다.
        /// </summary>
        private void ArmCatalogRetry(GameSnapshot snapshot)
        {
            var inRun = snapshot?.Inventory != null;
            if (inRun && !_inRun) _catalogRetryArmed = true;
            _inRun = inRun;
        }

        private bool _dumping;

        /// <summary>
        /// 질의 전수 검증이 무거워, 한 프레임에 다 하면 게임이 수 초 멈춘다.
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
            string generation;
            try
            {
                generation = CatalogDump.BeginRefresh();
            }
            catch (Exception ex)
            {
                Logger.LogError("데이터 덤프를 시작하지 못했습니다: " + ex);
                _dumping = false;
                yield break;
            }

            // 정의가 바뀌었을 수 있으므로 인게임 풀이가 쓰던 카탈로그를 버리고 다시 짓게 한다.
            CatalogSource.Invalidate();
            _runner?.Dispose();
            _runner = null;

            var readyDeadline = Time.realtimeSinceStartup + 30f;
            while (CatalogSource.Get() == null)
            {
                if (Time.realtimeSinceStartup >= readyDeadline)
                {
                    Logger.LogError("데이터 덤프 보류 - 카탈로그 준비 실패: " + CatalogSource.LastError +
                                    ". 게임 화면이 열린 뒤 F9로 다시 시도하세요.");
                    FailDump(generation, "카탈로그 준비 실패: " + CatalogSource.LastError);
                    _dumping = false;
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }

            // 이터레이터 안에서 던진 예외를 그대로 두면 코루틴이 죽으면서 _dumping 이 영영 참으로
            // 남는다. 한 걸음씩 감싸서 실패해도 플래그를 되돌린다.
            var steps = CatalogDump.WriteRoutine(Logger.LogInfo, generation);
            var completed = false;
            while (true)
            {
                try
                {
                    if (!steps.MoveNext())
                    {
                        completed = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError("데이터 덤프 실패: " + ex);
                    FailDump(generation, ex.Message);
                    break;
                }
                yield return steps.Current;
            }
            if (completed) CatalogSource.Invalidate();
            _dumping = false;
        }

        private void FailDump(string generation, string reason)
        {
            try
            {
                CatalogDump.FailRefresh(generation, reason);
            }
            catch (Exception ex)
            {
                Logger.LogError("카탈로그 실패 상태도 기록하지 못했습니다: " + ex);
            }
        }

        private void DumpInventory()
        {
            if (_diagnosticTask != null || _diagnosticWindow.IsOpen)
            {
                Report("진단을 처리 중입니다. 전송 또는 선택이 끝난 뒤 다시 눌러 주세요.");
                return;
            }
            try
            {
                var capture = new DiagnosticCapture(_diagnosticLog, Logger.LogWarning);
                var avatar = GameReader.FindLocalPlayer();
                capture.Collect("inventory-dump.txt", () => avatar?.Inventory == null ? null :
                    InventoryDiagnostics.Write(avatar.Inventory, avatar, _settings.OfferRadius.Value, capture.DirectoryPath), legacy: true);
                capture.Collect("inventory-snapshot.json", () => avatar?.Inventory == null ? null :
                    InventoryDiagnostics.WriteSnapshot(GameReader.Read(_settings.OfferRadius.Value), capture.DirectoryPath), legacy: true);
                capture.Collect("plan.replay", () =>
                {
                    var replay = _runner?.CaptureReplay();
                    if (replay == null) return null;
                    replay.Producer = PluginIdentity.Describe();
                    var directory = Path.Combine(PlannerData.DataDirectory, "reproductions");
                    Directory.CreateDirectory(directory);
                    var original = Path.Combine(directory, CatalogBundleStore.NewGeneration() + ".replay");
                    PlanReplayFile.Write(original, Newtonsoft.Json.JsonConvert.SerializeObject(replay, Newtonsoft.Json.Formatting.Indented));
                    replay.LatestError = _diagnosticLog.Redact(replay.LatestError);
                    var path = Path.Combine(capture.DirectoryPath, "plan.replay");
                    PlanReplayFile.Write(path, Newtonsoft.Json.JsonConvert.SerializeObject(replay, Newtonsoft.Json.Formatting.Indented));
                    return path;
                });
                capture.Finish(PluginIdentity.Describe(), ReplayPreferences.From(_prefs.ToPreferences(_settings.Recommendations.Value)));
                Logger.LogInfo("이번 진단 보관 위치: " + capture.DirectoryPath);
                if (_settings.DiagnosticUploadAllowed) StartDiagnosticUpload(capture);
                else if (!_settings.DiagnosticChoiceMade.Value || _settings.DiagnosticConsent.Value.Length > 0) OpenDiagnosticConsent(capture);
                else ReportDiagnostic(capture.HasFailures
                    ? "진단 일부 저장에 실패했습니다. 저장한 자료와 실패 기록은 로컬에 보관했습니다."
                    : "진단을 로컬에 저장했습니다. F3에서 진단 전송을 켤 수 있습니다.");
            }
            catch (Exception ex)
            {
                Logger.LogError("인벤토리 덤프 실패: " + ex);
                ReportDiagnostic("진단 또는 재현 자료 저장에 실패했습니다. BepInEx 로그를 확인하세요.");
            }
        }

        private void CaptureOwnLog(object sender, BepInEx.Logging.LogEventArgs args) =>
            _diagnosticLog.Append(DateTime.UtcNow.ToString("O") + " [" + args.Level + "] " + args.Data);

        private void OpenDiagnosticConsent(DiagnosticCapture capture)
        {
            _pendingDiagnostic = capture;
            if (!_diagnosticWindow.IsOpen) _diagnosticWindow.Toggle("ESC: 이번에는 전송하지 않기");
            if (_diagnosticWindow.Blocker.Length > 0)
            {
                Logger.LogWarning("진단 전송 안내를 열지 못했습니다: " + _diagnosticWindow.Blocker);
                ReportDiagnostic("전송 안내를 열지 못했습니다. 수집한 진단은 로컬에 보관합니다.");
            }
        }

        private void ChooseDiagnosticConsent(bool allowed)
        {
            try { _settings.SetDiagnosticConsent(allowed); }
            catch (Exception ex)
            {
                Logger.LogError("진단 전송 설정 저장 실패: " + ex);
                ReportDiagnostic("전송 설정을 저장하지 못했습니다. 이번 진단은 로컬에 보관합니다.");
                return;
            }
            var capture = _pendingDiagnostic;
            _pendingDiagnostic = null;
            if (allowed && capture != null) StartDiagnosticUpload(capture);
            else ReportDiagnostic(allowed ? "이후 F10 진단을 비공개 서버로 전송합니다." :
                capture?.HasFailures == true ? "진단 일부 저장에 실패했습니다. 실패 기록은 로컬에 보관합니다." : "진단은 로컬에만 저장합니다.");
            _window.Refresh();
        }

        private void StartDiagnosticUpload(DiagnosticCapture capture)
        {
            if (!_settings.DiagnosticUploadAllowed || _diagnosticTask != null) return;
            if (!_diagnosticThrottle.TryStart(out var remaining))
            {
                var message = "이번 진단은 로컬에 저장했습니다. " + Math.Ceiling(remaining.TotalSeconds) + "초 뒤부터 다시 전송할 수 있습니다.";
                if (capture.HasFailures) message += " 일부 자료 수집에 실패했습니다.";
                ReportDiagnostic(message);
                Logger.LogInfo(message);
                try { File.WriteAllText(Path.Combine(capture.DirectoryPath, "upload-result.txt"), message); }
                catch (Exception ex) { Logger.LogWarning("진단 전송 제한 기록을 저장하지 못했습니다: " + ex); }
                return;
            }
            _diagnosticCancellation = new CancellationTokenSource();
            // 초기 운영 제한값이다. 느린 연결이 게임 종료나 다음 조작을 붙잡지 않도록 한다.
            _diagnosticCancellation.CancelAfter(TimeSpan.FromSeconds(60));
            var cancellation = _diagnosticCancellation.Token;
            var consent = _settings.DiagnosticConsent.Value;
            _diagnosticTask = Task.Run(async () =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var archive = DiagnosticArchive.Create(capture.Files);
                    File.WriteAllBytes(Path.Combine(capture.DirectoryPath, "report.zip"), archive);
                    var receipt = await _diagnosticClient.SendAsync(new Uri(DiagnosticUploadClient.DefaultEndpoint), consent,
                        capture.Id, archive, cancellation).ConfigureAwait(false);
                    var message = "진단 접수 완료 · 제보 번호 " + receipt;
                    if (capture.HasFailures) message += " · 일부 자료 수집 실패 포함";
                    try { File.WriteAllText(Path.Combine(capture.DirectoryPath, "upload-result.txt"), message); }
                    catch (Exception saveError)
                    {
                        Logger.LogWarning("진단은 접수됐지만 로컬 접수 기록 저장에 실패했습니다: " + saveError);
                        message += " · 로컬 접수 기록 저장 실패";
                    }
                    return message;
                }
                catch (Exception ex)
                {
                    var message = "진단 전송 실패: " + _diagnosticLog.Redact(ex.Message) + " · 로컬 보관: " + capture.DirectoryPath;
                    Logger.LogWarning(message);
                    try { File.WriteAllText(Path.Combine(capture.DirectoryPath, "upload-result.txt"), message); }
                    catch (Exception saveError) { Logger.LogWarning("진단 전송 실패 기록도 저장하지 못했습니다: " + saveError); }
                    return message;
                }
            });
            ReportDiagnostic("진단을 비공개 서버로 전송 중입니다. 게임을 계속할 수 있습니다.");
        }

        private void CheckDiagnosticUpload()
        {
            if (!_settings.DiagnosticUploadAllowed) _diagnosticCancellation?.Cancel();
            if (_diagnosticTask == null || !_diagnosticTask.IsCompleted) return;
            var completed = _diagnosticTask;
            _diagnosticTask = null;
            _diagnosticCancellation.Dispose();
            _diagnosticCancellation = null;
            var message = completed.GetAwaiter().GetResult();
            Logger.LogInfo(message);
            ReportDiagnostic(message);
        }

        private void ReportDiagnostic(string message)
        {
            _diagnosticNotice = message;
            _diagnosticNoticeUntil = Time.unscaledTime + AutoPlaceNoticeSeconds;
            Report(message, AutoPlaceNoticeSeconds);
        }

        private void PollGameState()
        {
            var started = FrameCost.Now;
            try
            {
                var step = FrameCost.Now;
                var snapshot = GameReader.Read(_settings.OfferRadius.Value, _settings.Recommendations.Value);
                FrameCost.Read.Add(step);

                step = FrameCost.Now;
                var simulation = GameReader.CheckSimulation();
                FrameCost.Simulation.Add(step);
                VerifySimulation(simulation);

                ReportSephirites(snapshot);
                ArmCatalogRetry(snapshot);

                step = FrameCost.Now;
                FeedNativePanel(snapshot);
                FrameCost.Feed.Add(step);
            }
            catch (Exception ex)
            {
                // 게임 업데이트로 내부 구조가 바뀌면 여기서 터진다. 게임을 죽이지 않고 물러선다.
                Logger.LogError("스냅샷 생성 실패: " + ex);
                _nextPoll = Time.unscaledTime + 5f;
            }
            FrameCost.Poll.Add(started);
        }

        /// <summary>
        /// 근처 세피라이트의 상태를 남긴다. 무엇이 왜 후보에 못 들어갔는지 나중에 읽을 수 있어야 한다.
        /// </summary>
        private void ReportSephirites(GameSnapshot snapshot)
        {
            var report = OfferReader.LastSephiriteReport;
            if (report == _lastSephiriteReport) return;

            _lastSephiriteReport = report;
            Logger.LogInfo($"세피라이트 {(report.Length == 0 ? "없음" : report)} -> 후보 {snapshot.Offers.Count}개");
        }

        private void VerifySimulation(RuntimeSimulationCheck verification)
        {
            var changed = verification.Status != _simulationVerification ||
                          verification.Reason != _simulationReason;
            _simulationVerification = verification.Status;
            _simulationReason = verification.Reason;
            if (changed && verification.Status == PlanVerificationStatus.Failed)
            {
                Logger.LogWarning(verification.Reason);
            }

            // 일치할 때 아무것도 남기지 않으면 검증이 돌았는지조차 알 수 없다.
            var checkedTablets = SimulationVerifier.LastCheckedTablets;
            if (verification.Status == PlanVerificationStatus.Passed && checkedTablets > _verifiedTablets)
            {
                _verifiedTablets = checkedTablets;
                Logger.LogInfo($"시뮬레이터 검증 통과 - 석판 {checkedTablets}개와 칸별 레벨까지 일치");
            }
        }

        /// <summary>
        /// 인게임 화면이 쓸 배치를 푼다. 스냅샷이 그대로면
        /// 다시 풀지 않는다 - 같은 답을 얻자고 매번 빔 서치를 돌릴 이유가 없다.
        /// </summary>
        private void FeedNativePanel(GameSnapshot snapshot)
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

            var preferences = Preferences();
            _currentCatalogGeneration = CatalogDump.ActiveGeneration;
            _currentPlacementFingerprint = PlanFingerprint.Placement(
                snapshot, preferences, _currentCatalogGeneration);
            _runner.Submit(snapshot, preferences, _currentCatalogGeneration);
            _submittedPreferencesRevision = _prefs.Revision;
            _panelAwaitingRefresh = false;
        }

        private BuildContext CurrentBuild()
        {
            var state = _runner?.State;
            return new BuildContext
            {
                Catalog = _runner != null ? CatalogSource.Get() : null,
                Snapshot = _lastSnapshot,
                // 재계산 중에도 아이템 설정 목록은 유지하고 DPS 결과의 최신 여부를 따로 표시한다.
                Plan = state?.Latest,
                PlanCurrent = state?.IsCurrent == true && _submittedPreferencesRevision == _prefs.Revision && _settings.Panel.Value && !_panelAwaitingRefresh,
                PlanError = state?.Error ?? "",
                Recommendations = _settings.Recommendations.Value,
            };
        }

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
            if (_hud.IsAlive && _builtLayout != _settings.Layout) _hud.Destroy();

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

            if (_builtLayout != _settings.Layout)
            {
                _builtLayout = _settings.Layout;
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

            if (Time.unscaledTime < _diagnosticNoticeUntil)
            {
                _hud.RenderNotice(_diagnosticNotice);
                return;
            }

            // 런 밖이나 죽은 뒤에는 보여 줄 배치가 없다. 직전 런의 점수를 남겨 두면 거짓말이 된다.
            // "탐험"은 게임 자체가 쓰는 말이다.
            if (_lastSnapshot?.Inventory == null)
            {
                _hud.RenderNotice("탐험 중이 아닙니다.");
                return;
            }

            if (_panelAwaitingRefresh)
            {
                _hud.RenderNotice("최신 게임 상태를 읽는 중입니다.");
                return;
            }

            var state = _runner?.State;
            var error = state?.Error;
            if (error != null)
            {
                _hud.RenderNotice("계산 실패 - " + error);
                return;
            }

            if (state == null || !state.IsCurrent)
            {
                _hud.RenderNotice(Waiting(state));
                return;
            }

            var plan = state.Latest;
            if (plan == null)
            {
                _hud.RenderNotice(Waiting(state));
                return;
            }

            var mixerOpen = _settings.Recommendations.Value && GameReader.IsMixerOpen();
            AutoExpand(plan, mixerOpen);

            var preview = PreviewName(plan);
            try
            {
                Render(plan, preview, state, mixerOpen);
            }
            catch (Exception ex)
            {
                // 유니티 쪽 예외는 Player.log 에만 쌓여 우리 로그가 조용하다. 매 프레임 도는
                // 자리라 같은 예외는 한 번만 남긴다.
                // 반쯤 그려진 화면이 굳지 않게 다음 프레임에 처음부터 다시 그리게 한다.
                _hud.Invalidate();

                if (!NewError("화면 그리기", ex.GetType().Name + ": " + ex.Message)) return;

                Logger.LogError("화면 그리기 실패 - " + ex);
            }
        }

        private void Render(Plan plan, string preview, PlanRunState state, bool mixerOpen)
        {
            _hud.Render(new HudFrame
            {
                Snapshot = _lastSnapshot,
                Plan = plan,
                Catalog = CatalogSource.Get(),
                Prefs = _prefs,
                Values = CharmValueSource.Book,
                Expanded = _expanded,
                MixerOpen = mixerOpen,
                Recommendations = _settings.Recommendations.Value,
                MultiplayerAutoPlace = _settings.MultiplayerAutoPlace.Value,
                QueryVerified = CatalogDump.QueryVerificationPassed(),
                RuntimeVerification = _simulationVerification,
                RuntimeVerificationReason = _simulationReason,
                Hint = Hint(plan, preview, state),
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
        private string Waiting(PlanRunState state)
        {
            if (state == null)
            {
                // 실패는 로그에만 남고 화면은 영영 "준비 중"이었다. 되살릴 키를 여기서 말한다.
                return CatalogDump.RefreshStatus == CatalogRefreshStatus.Failed
                    ? "데이터 생성 실패 - " + Describe(_settings.DumpKey) +
                      " 로 다시 시도하세요(BepInEx 로그에 이유가 있습니다)."
                    : "데이터 준비 중";
            }

            switch (state.Blocker)
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
            var plan = CurrentPlan();
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
        /// 무엇을 집거나 합칠지 고르는 순간에는 격자와 후보를 다 봐야 한다. 후보나 합성 창이 나타나면
        /// 펼치고 닫히면 되돌린다. 그 사이에 직접 접거나 편 것은 상황이 바뀔 때까지 그대로 둔다.
        /// </summary>
        private void AutoExpand(Plan plan, bool mixerOpen)
        {
            var expanded = _adviceExpansion.Update(plan.Offers.Count > 0, mixerOpen);
            if (expanded.HasValue) _expanded = expanded.Value;
        }

        /// <summary>
        /// 아래 한 줄. 누를 것이 없는 화면이라 무엇을 눌러야 하는지는 여기서만 알 수 있다.
        /// 자동 배치 결과도 잠깐 이 자리에 띄운다 - 로그에만 남기면 무음 실패가 된다.
        /// </summary>
        private string Hint(Plan plan, string preview, PlanRunState state)
        {
            // 참가자 세션의 적용은 걸음마다 서버 왕복을 기다려 수 초가 걸린다. 그동안 아무 말이
            // 없으면 인벤토리가 저 혼자 움직이는 것으로만 보인다.
            if (PlanApplier.InProgress) return PlanApplier.Progress;
            if (PlanApplier.RecoveryRequired) return PlanApplier.RecoveryMessage;

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
            return _expanded ? Guide(state) : "";
        }

        private string _guide = "";
        private bool _guideExpanded;
        private bool _guideAutoPlace;
        private bool _guideOffers;
        private int _guideShortcuts = -1;

        private string Guide() => Guide(_runner?.State);

        /// <summary>
        /// 무엇을 누르면 되는지. 지금 할 수 있는 것만 적는다 - 멀티에서 자동 배치를, 후보가
        /// 없을 때 미리보기를 적어 두면 눌러도 아무 일이 없는 키를 알려주는 셈이 된다.
        ///
        /// <b>지은 것을 들고 있는다.</b> 이 줄은 그리는 쪽이 프레임마다 부르지만 내용이 바뀌는
        /// 것은 접거나 펴고, 자동 배치가 열리고 닫히고, 후보가 생기고 사라지고, 단축키를 바꿀
        /// 때뿐이다. 짓는 값에 <c>KeyCode.ToString()</c>이 여덟 번 들어 있어 프레임마다 치를
        /// 값이 아니다 - 이 게임은 증분 GC 가 프레임당 3ms 를 가져간다.
        /// </summary>
        private string Guide(PlanRunState state)
        {
            var plan = state != null && state.IsCurrent ? state.Latest : null;
            var offers = plan != null && plan.Offers.Count > 0;
            var autoPlace = AutoPlaceAvailability(
                _lastSnapshot, state, _currentPlacementFingerprint, _currentCatalogGeneration).Allowed;
            var shortcuts = _settings.ShortcutRevision;

            if (_guideExpanded == _expanded && _guideAutoPlace == autoPlace &&
                _guideOffers == offers && _guideShortcuts == shortcuts)
                return _guide;

            _guideExpanded = _expanded;
            _guideAutoPlace = autoPlace;
            _guideOffers = offers;
            _guideShortcuts = shortcuts;

            var text = Describe(_settings.ExpandKey) + (_expanded ? " 접기" : " 펼치기");
            if (autoPlace) text += "   " + Describe(_settings.AutoPlaceKey) + " 자동 배치";
            if (offers) text += "   " + Describe(_settings.PreviewKey) + " 후보 미리보기";

            _guide = text +
                     "   " + Describe(_settings.BuildKey) + " 빌드" +
                     "   " + Describe(_settings.OpacityKey) + " 불투명도" +
                     "   " + Describe(_settings.MoveKey) + " 이동" +
                     "   " + Describe(_settings.HideKey) + " 숨기기" +
                     "   " + Describe(_settings.SettingsKey) + " 설정";
            return _guide;
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
        ///
        /// 참가자로 접속한 세션에서는 걸음마다 서버 왕복을 기다리므로 여러 프레임에 걸친다.
        /// 그래서 적용기는 코루틴이고 결과는 끝난 뒤에 돌아온다.
        /// </summary>
        private void AutoPlace()
        {
            try
            {
                if (PlanApplier.InProgress)
                {
                    Report("자동 배치가 아직 진행 중입니다.");
                    _nextPoll = 0;
                    return;
                }

                if (_previewKey.Length > 0)
                {
                    _previewKey = "";
                    Report("현재 가방의 배치로 돌아왔습니다. 배치를 확인한 뒤 " +
                           Describe(_settings.AutoPlaceKey) + " 를 다시 누르세요.");
                    return;
                }

                var snapshot = GameReader.Read(_settings.OfferRadius.Value, _settings.Recommendations.Value);
                VerifySimulation(GameReader.CheckSimulation());
                FeedNativePanel(snapshot);

                var state = _runner?.State;
                var generation = CatalogDump.ActiveGeneration;
                var fingerprint = PlanFingerprint.Placement(snapshot, Preferences(), generation);
                var decision = AutoPlaceAvailability(snapshot, state, fingerprint, generation);
                if (!decision.Allowed)
                {
                    Report(decision.Reason);
                    _nextPoll = 0;
                    return;
                }

                var plan = state.Latest;
                StartCoroutine(PlanApplier.Apply(
                    plan.CreateApplyCommand(), _settings.MultiplayerAutoPlace.Value, AutoPlaceFinished));
            }
            catch (Exception ex)
            {
                Logger.LogError("자동 배치 실패: " + ex);
                Report("자동 배치 중 오류가 났습니다. BepInEx 로그를 확인하세요.");
                _nextPoll = 0;
            }
        }

        private void AutoPlaceFinished(string result)
        {
            Logger.LogInfo(result);

            // 자동 배치 결과는 다른 안내보다 오래 띄운다. 실패하면 "손으로 정리한 뒤 다시
            // 시도하세요" 같은 긴 문장이 오는데, 전투 중이면 여섯 초로는 읽다 만다.
            // 이 길이는 재어 본 값이 아니라 읽는 데 걸릴 시간을 어림한 것이다.
            Report(result, AutoPlaceNoticeSeconds);

            // 적용 결과가 화면에 바로 보이도록 다음 폴링을 기다리지 않는다.
            _nextPoll = 0;
        }

        private Plan CurrentPlan()
        {
            var state = _runner?.State;
            return state != null && state.IsCurrent ? state.Latest : null;
        }

        private AutoPlaceDecision AutoPlaceAvailability(
            GameSnapshot snapshot, PlanRunState state,
            string placementFingerprint, string catalogGeneration) =>
            AutoPlacePolicy.Evaluate(new AutoPlaceContext
            {
                Runner = state,
                CatalogVerified = CatalogDump.QueryVerificationPassed(),
                CatalogGeneration = catalogGeneration,
                RuntimeVerification = _simulationVerification,
                RuntimeVerificationReason = _simulationReason,
                CurrentPlacementFingerprint = placementFingerprint,
                IsMultiplayer = snapshot != null && snapshot.IsMultiplayer,
                AllowMultiplayer = _settings.MultiplayerAutoPlace.Value,
                SessionActive = NetworkServer.active || NetworkClient.active,
                Applying = PlanApplier.InProgress,
                RecoveryRequired = PlanApplier.RecoveryRequired,
                Previewing = _previewKey.Length > 0,
            });

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

        /// <summary>오른쪽 단추가 이 프레임에 눌렸는가. 커서와 같은 길로 읽는다.</summary>
        private static bool RightClicked()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            return mouse != null ? mouse.rightButton.wasPressedThisFrame : Input.GetMouseButtonDown(1);
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
            _settings.SavePanelMargin(margin.x, margin.y);

            // 화면은 이미 그 자리에 가 있다. 여백이 바뀌었다고 다시 짓게 두면 같은 자리에
            // 같은 것을 짓느라 한 프레임 깜빡일 뿐이다.
            _builtLayout = _settings.Layout;
            Report("자리를 기억했습니다.");

            // 커서가 움직이지 않으면 화면도 제자리에 선다. 그때 무엇이 잘못인지는 좌표를 봐야 안다.
            Logger.LogInfo($"이동 모드 끝 - 커서 {Cursor()} 여백 {margin}");
        }

        private const float NoticeSeconds = 6f;
        private const float AutoPlaceNoticeSeconds = 12f;

        private void Report(string message, float seconds = NoticeSeconds)
        {
            _autoPlaceResult = message;
            _autoPlaceShownUntil = Time.unscaledTime + seconds;
        }

        private void OnDestroy()
        {
            _runner?.Dispose();
            _diagnosticCancellation?.Cancel();
            _diagnosticCancellation?.Dispose();
            _diagnosticClient?.Dispose();
            Logger.LogEvent -= CaptureOwnLog;
            if (_moving && _settings != null && _hud.IsAlive)
            {
                var margin = _hud.Margin;
                _settings.SavePanelMargin(margin.x, margin.y);
            }
            _hud.Destroy();
            _window.Destroy();
            _build.Destroy();
            _diagnosticWindow.Destroy();
        }
    }
}

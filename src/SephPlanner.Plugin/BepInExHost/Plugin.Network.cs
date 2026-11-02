using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Plugin.Host;
using SephPlanner.Plugin.Net;
using SephPlanner.Plugin.Ui;
using UnityEngine;
using static SephPlanner.Core.Text.Localization;
using Phrase = SephPlanner.Core.Text.Phrase;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 진단 전송과 게임 안 업데이트. 네트워크를 쓰는 것은 여기뿐이고, BepInEx 판에만 들어간다 - 창작마당 모드는
    /// 정보 수집·외부 전송이 금지되고 실행 중 DLL 을 바꾸면 로드되지 않는다(docs/LEGAL.md 의 "창작마당 모드 규칙").
    /// </summary>
    internal sealed partial class SephPlannerPlugin
    {
        private DiagnosticConsentWindow _diagnosticWindow;
        private DiagnosticNoteWindow _noteWindow;
        private DiagnosticConsentWindow _automaticDiagnosticWindow;
        private bool _automaticPromptShown;
        private bool _automaticUpload;
        private readonly AutomaticDiagnosticPolicy _automaticPolicy = new AutomaticDiagnosticPolicy();
        private object _automaticIncident;
        private string _automaticIncidentLog;
        private DiagnosticUploadClient _diagnosticClient;
        private CancellationTokenSource _diagnosticCancellation;
        // 화면 문장과 로그 문장. 문장 형식(Phrase)으로 들지 않는 것은 필드 형식이 클래스를 올릴 때 풀려서다(_guideLanguage).
        private Task<(string Shown, string Logged)> _diagnosticTask;
        private readonly DiagnosticUploadThrottle _diagnosticThrottle = new DiagnosticUploadThrottle(DiagnosticUploadThrottle.DefaultInterval);
        private UpdateClient _updateClient;
        private UpdateWindow _updateWindow;
        private CancellationTokenSource _updateCancellation;
        private Task<Version> _updateCheck;
        private Task<string> _updateInstall;
        private Version _updateAvailable;

        /// <summary>화면에 띄워야 하는데 아직 못 띄운 단계. 창을 만들 수 있을 때까지 폴링마다 다시 시도한다.</summary>
        private UpdateWindow.Stage? _updatePrompt;
        private float _updatePromptRetryAt;
        private Version _updatedTo;
        private bool _filesSplit;

        partial void StartNetwork()
        {
            _diagnosticWindow = new DiagnosticConsentWindow(ChooseDiagnosticConsent, CancelDiagnostic);
            _automaticDiagnosticWindow = new DiagnosticConsentWindow(ChooseAutomaticDiagnosticConsent,
                () => _automaticPromptShown = true, automatic: true);
            _settings.AutomaticDiagnosticConsent.SettingChanged += (_, _) =>
            {
                if (_settings.AutomaticDiagnosticAllowed) return;
                _automaticIncident = null;
                _automaticIncidentLog = null;
                if (_automaticUpload) _diagnosticCancellation?.Cancel();
            };
            _noteWindow = new DiagnosticNoteWindow(FinishDiagnostic, CancelDiagnostic);
            _diagnosticClient = new DiagnosticUploadClient();
            _settings.DiagnosticConsent.SettingChanged += (_, _) =>
            {
                if (!_automaticUpload && !_settings.DiagnosticUploadAllowed) _diagnosticCancellation?.Cancel();
            };
            _settings.ReviewDiagnostics = () => OpenDiagnosticConsent(null);
            _settings.ReviewAutomaticDiagnostics = OpenAutomaticDiagnosticConsent;

            _updateWindow = new UpdateWindow(StartUpdateInstall, () => Log.Info("업데이트를 미뤘습니다. 다음 실행 때 다시 묻습니다."));
            _updateClient = new UpdateClient(Application.platform == RuntimePlatform.OSXPlayer);
            Guarded(FinishPreviousUpdate, "업데이트 정리");
            if (_settings.UpdateCheck.Value) StartUpdateCheck();
        }

        partial void PollNetwork()
        {
            Guarded(CheckDiagnosticUpload, "진단 전송 상태");
            Guarded(CheckAutomaticDiagnostic, "자동 진단 처리");
            Guarded(CheckUpdateProgress, "업데이트 진행");
        }

        partial void StopNetwork()
        {
            _diagnosticCancellation?.Cancel();
            _diagnosticCancellation?.Dispose();
            _diagnosticClient?.Dispose();
            _updateCancellation?.Cancel();
            _updateCancellation?.Dispose();
            _updateClient?.Dispose();
            _diagnosticWindow?.Destroy();
            _automaticDiagnosticWindow?.Destroy();
            _noteWindow?.Destroy();
            _updateWindow?.Destroy();
        }

        partial void AddNetworkWindows(List<PlannerWindow> windows)
        {
            windows.Add(_diagnosticWindow);
            windows.Add(_automaticDiagnosticWindow);
            windows.Add(_noteWindow);
            windows.Add(_updateWindow);
        }

        partial void CheckAnsweringWindow(ref bool open) =>
            open = _diagnosticWindow.IsOpen || _noteWindow.IsOpen || _automaticDiagnosticWindow.IsOpen || _updateWindow.IsOpen;

        partial void CheckDiagnosticBusy(ref bool busy) =>
            busy = _diagnosticTask != null || _diagnosticWindow.IsOpen || _noteWindow.IsOpen || _automaticDiagnosticWindow.IsOpen;

        partial void DetectFilesSplit(ref bool split) => split = _filesSplit;

        /// <summary>
        /// 동의를 먼저 묻고 그다음에 메모를 받는다. 보낼지도 정하지 않았는데 무슨 일이
        /// 있었는지부터 적게 하면, 안 보내기로 한 사람은 헛수고를 한 셈이 된다.
        /// 로컬에만 남길 자료에는 읽을 사람이 없으므로 아예 묻지 않는다.
        /// </summary>
        partial void AskToSend(DiagnosticCapture capture, ref bool asked)
        {
            if (_settings.DiagnosticUploadAllowed)
            {
                OpenDiagnosticNote();
                asked = true;
                return;
            }
            if (!ConsentPending()) return;
            OpenDiagnosticConsent(capture);
            asked = true;
        }

        private partial void Deliver(DiagnosticCapture capture)
        {
            if (_settings.DiagnosticUploadAllowed) StartDiagnosticUpload(capture);
            else if (ConsentPending()) OpenDiagnosticConsent(capture);
            else ReportDiagnostic(capture.HasFailures
                ? Tr("진단 일부 저장에 실패했습니다. 저장한 자료와 실패 기록은 로컬에 보관했습니다.")
                : Tr("진단을 로컬에 저장했습니다. F3에서 진단 전송을 켤 수 있습니다."));
        }

        partial void TakeUpdateNotice()
        {
            if (_updatedTo == null) return;
            _startupNotice = Tr("SephPlanner {0} 으로 업데이트됐습니다.", UpdateClient.Format(_updatedTo));
            _updatedTo = null;
        }

        private void OpenAutomaticDiagnosticConsent()
        {
            if (!_automaticDiagnosticWindow.IsOpen)
                _automaticDiagnosticWindow.Toggle(Tr("ESC: 선택 보류"));
            if (_automaticDiagnosticWindow.IsOpen) _automaticPromptShown = true;
        }

        private void ChooseAutomaticDiagnosticConsent(bool allowed)
        {
            try
            {
                _runner?.TakeFailure();
                _settings.SetAutomaticDiagnosticConsent(allowed);
                _window.Refresh();
                ReportDiagnostic(allowed
                    ? Tr("이후 SephPlanner 오류 진단을 자동 전송합니다. F3에서 철회할 수 있습니다.")
                    : Tr("오류 진단을 자동 전송하지 않습니다. F10 수동 진단은 계속 사용할 수 있습니다."));
            }
            catch (Exception ex)
            {
                Log.Error("자동 진단 동의 저장 실패: " + ex);
                ReportDiagnostic(Tr("자동 진단 설정을 저장하지 못했습니다. 설정에서 다시 확인해 주세요."));
            }
        }

        partial void QueueAutomaticDiagnostic(string stage, string detail, PlanFailure failure)
        {
            try { TryQueueAutomaticDiagnostic(stage, detail, failure); }
            catch (Exception ex) { Log.Error("자동 진단 예약 실패: " + ex); }
        }

        private void TryQueueAutomaticDiagnostic(string stage, string detail, PlanFailure failure)
        {
            if (!_settings.AutomaticDiagnosticAllowed || _automaticIncident != null ||
                _diagnosticTask != null || _pendingDiagnostic != null || AnsweringWindowOpen()) return;
            var redacted = _diagnosticLog.Redact(detail);
            if (!_automaticPolicy.TryAccept(stage + "|" + redacted, _diagnosticThrottle)) return;
            _automaticIncident = new
            {
                Trigger = "automatic",
                Stage = stage,
                OccurredUtc = failure?.OccurredUtc ?? DateTime.UtcNow.ToString("O"),
                Detail = redacted,
                FailedInput = failure == null ? null : new
                {
                    failure.Snapshot,
                    failure.Preferences,
                    failure.CatalogGeneration,
                    Catalog = (failure.Catalog as Catalog)?.Export(),
                    failure.PreviousTargets,
                },
            };
            _automaticIncidentLog = _diagnosticLog.Snapshot();
        }

        private void CheckAutomaticDiagnostic()
        {
            if (!_automaticPromptShown && _settings.AutomaticDiagnosticChoice.Value != PluginSettings.AutomaticDiagnosticKey &&
                !AnyWindowOpen() && Time.unscaledTime >= _nextPoll)
                OpenAutomaticDiagnosticConsent();

            if (_automaticIncident == null) return;
            var incident = _automaticIncident;
            var log = _automaticIncidentLog;
            _automaticIncident = null;
            _automaticIncidentLog = null;
            if (!_settings.AutomaticDiagnosticAllowed || _diagnosticTask != null || _pendingDiagnostic != null) return;
            try
            {
                var capture = CollectDiagnostic();
                capture.Finish(PluginIdentity.Describe(),
                    ReplayPreferences.From(Preferences()), incident: incident, incidentLog: log);
                StartDiagnosticUpload(capture, automatic: true, reserved: true);
            }
            catch (Exception ex)
            {
                Log.Error("오류 진단 자동 수집 실패: " + ex);
                ReportDiagnostic(Tr("오류 진단을 자동 수집하지 못했습니다. BepInEx 로그를 확인하세요."));
            }
        }

        /// <summary>아직 전송 동의를 묻거나 다시 물어야 하는 상태인가.</summary>
        private bool ConsentPending() =>
            !_settings.DiagnosticChoiceMade.Value || _settings.DiagnosticConsent.Value.Length > 0;

        private void OpenDiagnosticNote()
        {
            _noteWindow.Reset();
            if (!_noteWindow.IsOpen) _noteWindow.Toggle(Tr("Enter: 보내기 · ESC: 취소"));
            if (_noteWindow.Blocker.Length == 0) return;

            // 창을 못 열었다고 진단을 버리지 않는다. 메모 없이 하던 대로 보낸다.
            Log.Warning("진단 메모 창을 열지 못했습니다: " + _noteWindow.Blocker);
            FinishDiagnostic(DiagnosticNote.None);
        }

        /// <summary>
        /// 보내지 않기로 했다. 수집한 자료는 그대로 로컬에 남긴다 - 취소는 전송을 그만두는 것이지
        /// 진단을 버리는 것이 아니다. 전송을 시작하지 않으므로 전송 간격 제한도 쓰지 않는다.
        /// </summary>
        private void CancelDiagnostic()
        {
            var cancelled = _pendingDiagnostic;
            _pendingDiagnostic = null;
            if (cancelled == null || !WriteDiagnostic(cancelled, DiagnosticNote.None)) return;

            ReportDiagnostic(cancelled.HasFailures
                ? Tr("이번 진단은 보내지 않았습니다. 저장한 자료와 실패 기록은 이 PC에만 있습니다.")
                : Tr("이번 진단은 보내지 않았습니다. 자료는 이 PC에만 있습니다."));
        }

        private void OpenDiagnosticConsent(DiagnosticCapture capture)
        {
            _pendingDiagnostic = capture;
            if (!_diagnosticWindow.IsOpen) _diagnosticWindow.Toggle(Tr("ESC: 이번에는 전송하지 않기"));
            if (_diagnosticWindow.Blocker.Length > 0)
            {
                Log.Warning("진단 전송 안내를 열지 못했습니다: " + _diagnosticWindow.Blocker);
                ReportDiagnostic(Tr("전송 안내를 열지 못했습니다. 수집한 진단은 로컬에 보관합니다."));
            }
        }

        private void ChooseDiagnosticConsent(bool allowed)
        {
            try { _settings.SetDiagnosticConsent(allowed); }
            catch (Exception ex)
            {
                Log.Error("진단 전송 설정 저장 실패: " + ex);
                var stranded = _pendingDiagnostic;
                _pendingDiagnostic = null;
                if (stranded != null) WriteDiagnostic(stranded, DiagnosticNote.None);
                ReportDiagnostic(Tr("전송 설정을 저장하지 못했습니다. 이번 진단은 로컬에 보관합니다."));
                return;
            }
            _window.Refresh();

            // 이제 보내도 된다는 답을 받았으니 무슨 일이 있었는지 묻는다. 전송은 그 뒤다.
            if (allowed && _pendingDiagnostic != null)
            {
                OpenDiagnosticNote();
                return;
            }

            var capture = _pendingDiagnostic;
            _pendingDiagnostic = null;
            if (capture != null && !WriteDiagnostic(capture, DiagnosticNote.None)) return;
            ReportDiagnostic(allowed ? Tr("이후 F10 진단을 비공개 서버로 전송합니다.") :
                capture?.HasFailures == true ? Tr("진단 일부 저장에 실패했습니다. 실패 기록은 로컬에 보관합니다.") : Tr("진단은 로컬에만 저장합니다."));
        }

        private void StartDiagnosticUpload(DiagnosticCapture capture, bool automatic = false, bool reserved = false)
        {
            if (!(automatic ? _settings.AutomaticDiagnosticAllowed : _settings.DiagnosticUploadAllowed) || _diagnosticTask != null) return;
            if (!reserved && !_diagnosticThrottle.TryStart(out var remaining))
            {
                var seconds = Math.Ceiling(remaining.TotalSeconds);
                var message = capture.HasFailures
                    ? new Phrase("이번 진단은 로컬에 저장했습니다. {0}초 뒤부터 다시 전송할 수 있습니다. 일부 자료 수집에 실패했습니다.", seconds)
                    : new Phrase("이번 진단은 로컬에 저장했습니다. {0}초 뒤부터 다시 전송할 수 있습니다.", seconds);
                ReportDiagnostic(message.ToString());
                Log.Info(message.Korean);
                try { File.WriteAllText(Path.Combine(capture.DirectoryPath, "upload-result.txt"), message.Korean); }
                catch (Exception ex) { Log.Warning("진단 전송 제한 기록을 저장하지 못했습니다: " + ex); }
                return;
            }
            _automaticUpload = automatic;
            _diagnosticCancellation = new CancellationTokenSource();
            // 초기 운영 제한값이다. 느린 연결이 게임 종료나 다음 조작을 붙잡지 않도록 한다.
            _diagnosticCancellation.CancelAfter(TimeSpan.FromSeconds(60));
            var cancellation = _diagnosticCancellation.Token;
            var consent = automatic
                ? DiagnosticUploadClient.ConsentKey(new Uri(DiagnosticUploadClient.DefaultEndpoint))
                : _settings.DiagnosticConsent.Value;
            _diagnosticTask = Task.Run(async () =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var archive = DiagnosticArchive.Create(capture.Files);
                    File.WriteAllBytes(Path.Combine(capture.DirectoryPath, "report.zip"), archive);
                    var receipt = await _diagnosticClient.SendAsync(new Uri(DiagnosticUploadClient.DefaultEndpoint), consent,
                        capture.Id, archive, cancellation).ConfigureAwait(false);
                    var message = Phrase.Join(" · ", new Phrase("진단 접수 완료 · 제보 번호 {0}", receipt),
                        capture.HasFailures ? new Phrase("일부 자료 수집 실패 포함") : null);
                    try { File.WriteAllText(Path.Combine(capture.DirectoryPath, "upload-result.txt"), message.Korean); }
                    catch (Exception saveError)
                    {
                        Log.Warning("진단은 접수됐지만 로컬 접수 기록 저장에 실패했습니다: " + saveError);
                        message = Phrase.Join(" · ", message, new Phrase("로컬 접수 기록 저장 실패"));
                    }
                    return (message.ToString(), message.Korean);
                }
                catch (Exception ex)
                {
                    var message = new Phrase("진단 전송 실패: {0} · 로컬 보관: {1}",
                        Phrase.Of(ex, _diagnosticLog.Redact), capture.DirectoryPath);
                    Log.Warning(message.Korean);
                    try { File.WriteAllText(Path.Combine(capture.DirectoryPath, "upload-result.txt"), message.Korean); }
                    catch (Exception saveError) { Log.Warning("진단 전송 실패 기록도 저장하지 못했습니다: " + saveError); }
                    return (message.ToString(), message.Korean);
                }
            });
            ReportDiagnostic(Tr("진단을 비공개 서버로 전송 중입니다. 게임을 계속할 수 있습니다."));
        }

        private void CheckDiagnosticUpload()
        {
            if (!(_automaticUpload ? _settings.AutomaticDiagnosticAllowed : _settings.DiagnosticUploadAllowed))
                _diagnosticCancellation?.Cancel();
            if (_diagnosticTask == null || !_diagnosticTask.IsCompleted) return;
            var completed = _diagnosticTask;
            _diagnosticTask = null;
            _automaticUpload = false;
            _diagnosticCancellation.Dispose();
            _diagnosticCancellation = null;
            var (shown, logged) = completed.GetAwaiter().GetResult();
            Log.Info(logged);
            ReportDiagnostic(shown);
        }

        private static Version CurrentVersion() => typeof(SephPlannerPlugin).Assembly.GetName().Version;

        /// <summary>
        /// 지금 로드된 DLL 둘의 자리. 업데이트는 이 파일들을 바꾸는 것이지 <c>plugins</c> 폴더에
        /// 새로 놓는 것이 아니다 - 하위 폴더에 설치한 사람도 같은 자리를 받는다.
        /// </summary>
        private static Dictionary<string, string> UpdateTargets() => new Dictionary<string, string>
        {
            [UpdatePackage.PluginFile] = typeof(SephPlannerPlugin).Assembly.Location,
            [UpdatePackage.CoreFile] = typeof(PlanRunner).Assembly.Location,
        };

        /// <summary>
        /// 지난 실행이 밀어 둔 옛 DLL 을 지운다. 있었다면 이번이 새 버전의 첫 실행이다. 두 DLL 의 판이
        /// 다르면 업데이트가 반쯤 걸린 것이라 알리고 다시 설치하라고 한다.
        ///
        /// <b>여기서는 옛 Core 에도 있던 것만 부른다.</b> 판이 섞였다면 Core 가 옛것일 수 있고, 그때 새로 생긴
        /// 멤버를 부르면 이 메서드가 통째로 못 돌아 알릴 기회를 잃는다. Core 의 판은 오래전부터 있던
        /// <see cref="PlanRunner"/> 로 읽는다. 업데이트 코드 자체는 플러그인 DLL 에 있다. 문자열 상수는 컴파일할 때 박히므로 괜찮다.
        /// 이 메서드를 부르는 <c>Awake</c> 본문도 같다 - 거기서 새 Core 멤버를 직접 부르면 여기까지 오지 못한다.
        /// </summary>
        private void FinishPreviousUpdate()
        {
            // 판 대조를 지우기보다 먼저 한다. 옛 DLL 이 잠겨 지우다 실패해도 알림은 남아야 한다.
            var core = typeof(PlanRunner).Assembly.GetName().Version;
            var split = UpdateClient.Normalize(CurrentVersion()) != UpdateClient.Normalize(core);
            _filesSplit = split;
            if (split)
            {
                _startupNotice = "SephPlanner 파일의 판이 서로 다릅니다(플러그인 " + UpdateClient.Format(CurrentVersion()) +
                                 ", Core " + UpdateClient.Format(core) + "). " + UpdateRollbackException.ReinstallAdvice;
                Log.Error(_startupNotice);
            }
            var updated = UpdateInstaller.CleanRetired(UpdateTargets().Values);
            if (split || !updated) return;
            // 번역은 새 Core 멤버라 여기서 부를 수 없다. 문장은 화면이 설 때 짓는다.
            _updatedTo = CurrentVersion();
            Log.Info("업데이트가 적용됐습니다. 이전 버전의 DLL 을 지웠습니다.");
        }

        private void StartUpdateCheck()
        {
            _updateCancellation = new CancellationTokenSource();
            _updateCancellation.CancelAfter(TimeSpan.FromSeconds(30));
            var cancellation = _updateCancellation.Token;
            var current = CurrentVersion();
            _updateCheck = Task.Run(() => _updateClient.CheckAsync(current, cancellation));
        }

        private void StartUpdateInstall(Version version)
        {
            if (_updateInstall != null) return;
            _updateWindow.Show(UpdateWindow.Stage.Working, version, CurrentVersion());
            _updateCancellation = new CancellationTokenSource();
            _updateCancellation.CancelAfter(TimeSpan.FromMinutes(5));
            var cancellation = _updateCancellation.Token;
            var targets = UpdateTargets();
            Log.Info("업데이트 " + UpdateClient.Format(version) + " 을 받습니다.");
            _updateInstall = Task.Run(async () =>
            {
                try
                {
                    var zip = await _updateClient.DownloadAsync(version, cancellation).ConfigureAwait(false);
                    cancellation.ThrowIfCancellationRequested();
                    UpdateInstaller.Install(UpdatePackage.Extract(zip, version), targets);
                    return null;
                }
                catch (Exception ex)
                {
                    Log.Warning("업데이트 실패: " + ex);
                    return ex is OperationCanceledException ? Tr("시간이 너무 오래 걸려 그만두었습니다.") : Phrase.Of(ex).ToString();
                }
            });
        }

        /// <summary>
        /// 확인과 설치의 결과를 메인 스레드에서 걷는다. 창은 게임의 HUD 캔버스가 서야 만들 수
        /// 있고 다른 창 위에 겹쳐 띄우지 않으므로, 띄울 것이 있으면 될 때까지 폴링마다 되짚는다.
        /// </summary>
        private void CheckUpdateProgress()
        {
            if (_updateCheck != null && _updateCheck.IsCompleted)
            {
                var completed = _updateCheck;
                _updateCheck = null;
                DisposeUpdateCancellation();
                try
                {
                    _updateAvailable = completed.GetAwaiter().GetResult();
                    if (_updateAvailable != null)
                    {
                        Log.Info("새 버전 " + UpdateClient.Format(_updateAvailable) + " 이 있습니다.");
                        _updatePrompt = UpdateWindow.Stage.Offer;
                    }
                    else Log.Info("최신 안정판입니다.");
                }
                catch (Exception ex)
                {
                    // 확인은 조용히 실패한다. 오프라인이거나 GitHub 가 잠깐 안 되는 것을 게임 화면에서 알릴 이유가 없다.
                    Log.Warning("업데이트 확인 실패: " + ex.Message);
                }
            }

            if (_updateInstall != null && _updateInstall.IsCompleted)
            {
                var completed = _updateInstall;
                _updateInstall = null;
                DisposeUpdateCancellation();
                var error = completed.GetAwaiter().GetResult();
                if (error == null) Log.Info("업데이트 " + UpdateClient.Format(_updateAvailable) + " 을 설치했습니다. 다음 실행부터 적용됩니다.");
                _updateWindow.Show(error == null ? UpdateWindow.Stage.Done : UpdateWindow.Stage.Failed, _updateAvailable, CurrentVersion(), error ?? "");
                _updatePrompt = _updateWindow.IsOpen ? null : _updateWindow.Current;
            }

            if (!_updatePrompt.HasValue || AnyWindowOpen() || Time.unscaledTime < _updatePromptRetryAt) return;
            _updatePromptRetryAt = Time.unscaledTime + 1f;
            if (_updatePrompt == UpdateWindow.Stage.Offer)
                _updateWindow.Show(UpdateWindow.Stage.Offer, _updateAvailable, CurrentVersion());
            _updateWindow.Toggle("");
            if (_updateWindow.Blocker.Length == 0) _updatePrompt = null;
        }

        private void DisposeUpdateCancellation()
        {
            _updateCancellation?.Dispose();
            _updateCancellation = null;
        }
    }
}

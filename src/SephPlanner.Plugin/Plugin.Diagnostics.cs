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
    /// 진단 전송. 두 판 모두 들어간다 - 창작마당 규칙 5 는 게임 관련 정보를 무엇을 보내는지 알리고 보내는 것을 허용한다
    /// (docs/LEGAL.md 의 "진단 전송 문의"). 동의 창이 보낼 항목을 알리므로 항목을 늘리면 그 안내도 고친다.
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
        private void StartDiagnostics()
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
        }

        private Action _checkDiagnosticUpload, _checkAutomaticDiagnostic;

        private void PollDiagnostics()
        {
            Guarded(_checkDiagnosticUpload ??= CheckDiagnosticUpload, "진단 전송 상태");
            Guarded(_checkAutomaticDiagnostic ??= CheckAutomaticDiagnostic, "자동 진단 처리");
        }

        private void StopDiagnostics()
        {
            _diagnosticCancellation?.Cancel();
            _diagnosticCancellation?.Dispose();
            _diagnosticClient?.Dispose();
            _diagnosticWindow?.Destroy();
            _automaticDiagnosticWindow?.Destroy();
            _noteWindow?.Destroy();
        }

        private void AddDiagnosticWindows(List<PlannerWindow> windows)
        {
            windows.Add(_diagnosticWindow);
            windows.Add(_automaticDiagnosticWindow);
            windows.Add(_noteWindow);
        }

        private bool DiagnosticWindowOpen() =>
            _diagnosticWindow.IsOpen || _noteWindow.IsOpen || _automaticDiagnosticWindow.IsOpen;

        /// <summary>앞선 진단을 아직 처리하는 중인가(전송 중이거나 동의·메모 창이 떠 있음).</summary>
        private bool DiagnosticBusy() => _diagnosticTask != null || DiagnosticWindowOpen();

        /// <summary>
        /// 동의를 먼저 묻고 그다음에 메모를 받는다. 보낼지도 정하지 않았는데 무슨 일이
        /// 있었는지부터 적게 하면, 안 보내기로 한 사람은 헛수고를 한 셈이 된다.
        /// 로컬에만 남길 자료에는 읽을 사람이 없으므로 아예 묻지 않는다.
        /// </summary>
        /// <returns>보낼지 물으러 갔으면 참. 거짓이면 곧바로 로컬에 적는다.</returns>
        private bool AskToSend(DiagnosticCapture capture)
        {
            if (_settings.DiagnosticUploadAllowed)
            {
                OpenDiagnosticNote();
                return true;
            }
            if (!ConsentPending()) return false;
            OpenDiagnosticConsent(capture);
            return true;
        }

        /// <summary>설명 파일까지 쓴 진단을 보내거나 로컬에 둔 것을 알린다.</summary>
        private void Deliver(DiagnosticCapture capture)
        {
            if (_settings.DiagnosticUploadAllowed) StartDiagnosticUpload(capture);
            else if (ConsentPending()) OpenDiagnosticConsent(capture);
            else ReportDiagnostic(capture.HasFailures
                ? Tr("진단 일부 저장에 실패했습니다. 저장한 자료와 실패 기록은 로컬에 보관했습니다.")
                : Tr("진단을 로컬에 저장했습니다. F3에서 진단 전송을 켤 수 있습니다."));
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

        /// <param name="sameCause">같은 원인인지 가를 문장. 없으면 <paramref name="detail"/> 로 가른다.</param>
        /// <returns>
        /// 큐가 잠시 막혀(동의 전, 다른 진단 처리 중, 답할 창이 떠 있음, 전송 간격) 받지도 거절하지도 않았다. 같은 오류를
        /// 나중에 다시 내밀어야 한다.
        /// </returns>
        private bool QueueAutomaticDiagnostic(string stage, string detail, PlanFailure failure, string sameCause = null)
        {
            try { return TryQueueAutomaticDiagnostic(stage, detail, failure, sameCause); }
            catch (Exception ex)
            {
                Log.Error("자동 진단 예약 실패: " + ex);
                return false;
            }
        }

        private bool TryQueueAutomaticDiagnostic(string stage, string detail, PlanFailure failure, string sameCause)
        {
            if (!_settings.AutomaticDiagnosticAllowed || _automaticIncident != null ||
                _diagnosticTask != null || _pendingDiagnostic != null || AnsweringWindowOpen() ||
                !_diagnosticThrottle.Ready) return true;
            var redacted = _diagnosticLog.Redact(detail);
            var key = sameCause == null ? redacted : _diagnosticLog.Redact(sameCause);
            if (!_automaticPolicy.TryAccept(stage + "|" + key, _diagnosticThrottle, out var later)) return later;
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
            return false;
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
                ReportDiagnostic(Tr("오류 진단을 자동 수집하지 못했습니다. 로그 파일({0})을 확인하세요.", PluginHost.LogFile));
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
                ? DiagnosticUploadClient.DefaultConsentKey
                : _settings.DiagnosticConsent.Value;
            _diagnosticTask = Task.Run(async () =>
            {
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var archive = DiagnosticArchive.Create(capture.Files);
                    File.WriteAllBytes(Path.Combine(capture.DirectoryPath, "report.zip"), archive);
                    var receipt = await _diagnosticClient.SendAsync(DiagnosticUploadClient.DefaultEndpointUri, consent,
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
    }
}

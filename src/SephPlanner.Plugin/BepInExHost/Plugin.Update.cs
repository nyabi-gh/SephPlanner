using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    /// 게임 안 업데이트. BepInEx 판에만 들어간다 - 창작마당 모드는 실행 중 DLL 을 바꾸면 로드되지 않고, 업데이트는
    /// Steam 이 한다(docs/LEGAL.md 의 "창작마당 모드 규칙").
    /// </summary>
    internal sealed partial class SephPlannerPlugin
    {
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

        partial void StartUpdates()
        {
            _updateWindow = new UpdateWindow(StartUpdateInstall, () => Log.Info("업데이트를 미뤘습니다. 다음 실행 때 다시 묻습니다."));
            _updateClient = new UpdateClient(Application.platform == RuntimePlatform.OSXPlayer);
            Guarded(FinishPreviousUpdate, "업데이트 정리");
            if (_settings.UpdateCheck.Value) StartUpdateCheck();
        }

        private Action _checkUpdateProgress;

        partial void PollUpdates() => Guarded(_checkUpdateProgress ??= CheckUpdateProgress, "업데이트 진행");

        partial void StopUpdates()
        {
            _updateCancellation?.Cancel();
            _updateCancellation?.Dispose();
            _updateClient?.Dispose();
            _updateWindow?.Destroy();
        }

        partial void AddUpdateWindows(List<PlannerWindow> windows) => windows.Add(_updateWindow);

        partial void CheckUpdateWindow(ref bool open) => open |= _updateWindow.IsOpen;

        partial void DetectFilesSplit(ref bool split) => split = _filesSplit;

        partial void TakeUpdateNotice()
        {
            if (_updatedTo == null) return;
            _startupNotice = Tr("SephPlanner {0} 으로 업데이트됐습니다.", UpdateClient.Format(_updatedTo));
            _updatedTo = null;
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

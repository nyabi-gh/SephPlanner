using System;
using System.Threading;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Runtime
{
    /// <param name="settled">
    /// 지금 놓여 있는 것이 이미 최선이라고 실행기가 아는 경우(자동 배치 직후). 탐색을 건너뛴다.
    /// </param>
    public delegate Plan? PlanBuildOperation(
        GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
        out PlanBlocker blocker, Plan? previous, LayoutCache layouts, bool settled,
        CancellationToken cancellation);

    /// <summary>
    /// 2단계. 게시된 배치에 조언을 붙인 <b>새 계획</b>을 낸다. 취소되면 <c>null</c> 이고 그때
    /// 배치는 그대로 살아 있다.
    /// </summary>
    public delegate Plan? PlanAdviceOperation(
        Plan placement, GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
        LayoutCache layouts, CancellationToken cancellation);

    public sealed class PlanRunState
    {
        public Plan? Latest { get; set; }
        public string? Error { get; set; }

        /// <summary>
        /// 조언 단계에서 난 오류. 배치는 멀쩡하므로 <see cref="Error"/> 와 나눈다 - 조언 하나가
        /// 실패했다고 자동 배치까지 막으면 고칠 수 있는 것을 못 고치게 된다.
        /// </summary>
        public string? AdviceError { get; set; }
        public PlanBlocker Blocker { get; set; }
        public long RequestedGeneration { get; set; }
        public long PublishedGeneration { get; set; }
        public bool IsBusy { get; set; }
        public bool HasPending { get; set; }

        /// <summary>조언이 아직 도는 중이거나 다시 풀 차례를 기다리고 있다.</summary>
        public bool AdviceBusy { get; set; }

        /// <summary>게시된 계획의 조언이 지금 판의 것이다.</summary>
        public bool AdviceIsCurrent { get; set; }

        /// <summary>
        /// <b>배치 기준이다.</b> 후보가 바뀌어 조언만 다시 푸는 동안에도 배치는 최신이다 -
        /// 그 둘을 한 깃발로 묶어 두었더니 세피라이트 창을 여닫는 것만으로 화면이 내내
        /// "갱신 중" 이었다.
        /// </summary>
        public bool IsCurrent => Error is null && RequestedGeneration > 0 &&
                                 PublishedGeneration == RequestedGeneration;
    }

    /// <summary>
    /// 배치와 조언을 따로 풀어 따로 게시한다.
    ///
    /// <b>왜 둘인가.</b> 계획 지문에는 후보·합성기·소지금이 들어 있어서 세피라이트 보상 창을
    /// 여닫기만 해도 지문이 바뀐다. 한 덩어리로 풀던 때는 그때마다 <b>배치까지</b> 취소하고 처음부터
    /// 다시 풀었고, 조언이 배치보다 몇 배 비싸므로 창을 5초마다 여닫는 층에서는 어느 요청도 끝을
    /// 보지 못했다(제보 <c>4c1efa35</c>: 게시 199 / 요청 204). 배치 지문이 그대로면 배치는 이미
    /// 답을 알고 있으므로, 조언만 다시 푼다.
    ///
    /// <b>한 번에 한 작업만 돈다.</b> <see cref="LayoutCache"/> 는 스레드 안전하지 않다. 다음에
    /// 무엇을 할지는 대기열이 아니라 세대 번호가 정하고(<see cref="StartNextLocked"/>), 배치가
    /// 조언보다 언제나 먼저다.
    /// </summary>
    public sealed class PlanRunner : IDisposable
    {
        private sealed class Request : IDisposable
        {
            /// <summary>조언 단계인가. 아니면 배치 단계다.</summary>
            public bool Advice;

            /// <summary>이 단계의 요청 세대.</summary>
            public long Generation;

            /// <summary>조언이 붙을 계획. 배치 단계에서는 <c>null</c> 이다.</summary>
            public Plan? Placement;

            /// <summary>지금 배치가 이미 최선이다. 자동 배치가 방금 끝나 그대로 들어온 경우다.</summary>
            public bool Settled;

            public GameSnapshot Snapshot = new GameSnapshot();
            public PlanPreferences Preferences = PlanPreferences.None;
            public string Fingerprint = "";
            public string PlacementFingerprint = "";
            public string PlanningContextFingerprint = "";
            public string CatalogGeneration = "";
            public Plan? Previous;

            /// <summary>요청이 들어온 때. 여기서 게시까지가 사용자가 기다리는 시간이다.</summary>
            public long RequestedAt;

            /// <summary>
            /// 이 요청이 낡았다고 알리는 자리. 뒤에 요청이 오면 지금 도는 풀이의 답은 어차피
            /// 버려지므로, 끝까지 돌게 두면 CPU 와 할당을 그대로 버린다.
            /// </summary>
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();

            /// <summary>
            /// <b>자물쇠 안에서만 부른다.</b> 취소를 거는 쪽(<see cref="Submit"/>)도 같은 자물쇠를
            /// 쥐고 있어야, 버리는 순간과 취소하는 순간이 겹쳐 터지는 일이 없다.
            /// </summary>
            public void Dispose() => Cancellation.Dispose();
        }

        private readonly object _gate = new object();
        private readonly ICatalog _catalog;
        private readonly PlanBuildOperation _build;
        private readonly PlanAdviceOperation _advise;
        private readonly TimeSpan _retryDelay;

        /// <summary>
        /// 계획 사이에 돌려 쓰는 빔 탐색. 아티팩트를 하나 옮기기만 해도 판이 다시 풀리는데 석판이
        /// 그대로면 빔은 같은 것을 다시 찾을 뿐이고, 평소 재계산의 여덟 할이 그 한 번이다.
        /// 계획 맥락(카탈로그 세대와 설정)이 바뀌면 값어치 표와 배치 조건이 함께 달라지므로,
        /// 열쇠에 세는 대신 캐시를 통째로 새로 짓는다.
        /// </summary>
        private LayoutCache _layouts = new LayoutCache();
        private string _layoutsContext = "";

        private Request? _running;
        private Plan? _latest;
        private GameSnapshot? _replaySnapshot;
        private PlanPreferences? _replayPreferences;
        private System.Collections.Generic.List<PlanTarget>? _replayPreviousTargets;
        private string? _error;
        private string? _adviceError;
        private PlanBlocker _blocker;

        /// <summary>마지막으로 제출된 판. 조언 단계는 배치 때의 것이 아니라 이것으로 푼다.</summary>
        private GameSnapshot? _snapshot;
        private PlanPreferences _preferences = PlanPreferences.None;
        private string _catalogGeneration = "";
        private string _contextFingerprint = "";
        private string _placementFingerprint = "";
        private string _fullFingerprint = "";

        /// <summary>
        /// 자동 배치가 검증까지 통과하고 끝났다. 그 결과가 그대로 들어오면 다시 풀지 않는다.
        /// 한 번 확인하면 버린다 - 그 뒤로는 무엇이든 달라질 수 있다.
        /// </summary>
        private Plan? _applied;
        private GameSnapshot? _appliedBefore;

        /// <summary>이번 배치 요청이 그 적용 결과인가.</summary>
        private bool _settled;

        private long _placementGeneration;
        private long _placementStarted;
        private long _placementPublished;
        private long _placementRequestedAt;
        private long _adviceGeneration;
        private long _adviceStarted;
        private long _advicePublished;

        private DateTime _retryAfterUtc;
        private bool _disposed;

        private readonly PlanSolveStat _placementStat = new PlanSolveStat();
        private readonly PlanSolveStat _adviceStat = new PlanSolveStat();
        private double _publishDelayMs;
        private double _worstPublishDelayMs;
        private int _worstPublishRun;
        private int _published;
        private long _worstBacklog;

        public PlanRunner(ICatalog catalog)
            : this(catalog, Build, TimeSpan.FromSeconds(1))
        {
        }

        public PlanRunner(
            ICatalog catalog, PlanBuildOperation build, TimeSpan? retryDelay = null,
            PlanAdviceOperation? advise = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _build = build ?? throw new ArgumentNullException(nameof(build));
            _advise = advise ?? Advise;
            _retryDelay = retryDelay ?? TimeSpan.FromSeconds(1);
        }

        public PlanRunState State
        {
            get
            {
                lock (_gate)
                {
                    return new PlanRunState
                    {
                        Latest = _latest,
                        Error = _error,
                        AdviceError = _adviceError,
                        Blocker = _blocker,
                        RequestedGeneration = _placementGeneration,
                        PublishedGeneration = _placementPublished,
                        IsBusy = _running is not null,
                        HasPending = PlacementNeededLocked() || AdviceNeededLocked(),
                        AdviceBusy = _running is { Advice: true } || AdviceNeededLocked(),
                        AdviceIsCurrent = _latest is not null && _adviceError is null &&
                                          _advicePublished == _adviceGeneration,
                    };
                }
            }
        }

        /// <summary>
        /// 지금까지의 풀이 계측. 잠금 안에서 복사해 주므로 읽는 쪽이 더 조심할 것은 없다.
        /// </summary>
        public PlanRunnerStats Stats
        {
            get
            {
                lock (_gate)
                {
                    return new PlanRunnerStats
                    {
                        Placement = _placementStat.Copy(),
                        Advice = _adviceStat.Copy(),
                        PublishDelayMs = _publishDelayMs,
                        WorstPublishDelayMs = _worstPublishDelayMs,
                        WorstPublishRun = _worstPublishRun,
                        Published = _published,
                        Backlog = _placementGeneration - _placementPublished,
                        WorstBacklog = _worstBacklog,
                    };
                }
            }
        }

        /// <summary>
        /// 지금 판을 제출한다. 돌려주는 것은 <b>배치</b> 요청 세대다.
        ///
        /// 배치 지문이 바뀌었으면 둘 다 새로 풀고, 전체 지문만 바뀌었으면(후보·합성기·소지금)
        /// 조언만 다시 푼다.
        /// </summary>
        public long Submit(
            GameSnapshot snapshot, PlanPreferences preferences, string catalogGeneration)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            if (preferences is null) throw new ArgumentNullException(nameof(preferences));

            var contextFingerprint = PlanFingerprint.PlanningContext(preferences, catalogGeneration);
            var placementFingerprint = PlanFingerprint.Placement(snapshot, contextFingerprint);
            var fingerprint = PlanFingerprint.Full(snapshot, preferences, catalogGeneration, placementFingerprint);

            lock (_gate)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(PlanRunner));

                // 조언 단계가 쓸 판이다. 배치는 그대로인데 후보만 바뀐 경우, 조언은 배치 때의
                // 낡은 스냅샷이 아니라 방금 들어온 것으로 풀어야 한다.
                _snapshot = snapshot;
                _preferences = preferences;
                _catalogGeneration = catalogGeneration;
                _contextFingerprint = contextFingerprint;

                if (placementFingerprint != _placementFingerprint)
                {
                    // 자동 배치가 끝나고 처음 들어온 판이다. 그 계획 그대로면 답을 이미 안다.
                    _settled = _applied is not null && AppliedPlacement.Settled(
                        _applied, _appliedBefore!, snapshot, placementFingerprint, contextFingerprint);
                    _applied = null;
                    _appliedBefore = null;

                    _placementFingerprint = placementFingerprint;
                    _fullFingerprint = fingerprint;
                    NewPlacementLocked();
                }
                else if (fingerprint != _fullFingerprint)
                {
                    _fullFingerprint = fingerprint;
                    NewAdviceLocked();

                    // 배치는 그대로다. 도는 것이 조언일 때만 멈춘다.
                    if (_running is { Advice: true }) _running.Cancellation.Cancel();
                }
                else if (_error is not null && _running is null &&
                         !PlacementNeededLocked() && DateTime.UtcNow >= _retryAfterUtc)
                {
                    // 같은 판인데 앞선 계산이 실패했다. 물러섰다가 다시 해 본다.
                    NewPlacementLocked();
                }

                StartNextLocked();
                return _placementGeneration;
            }
        }

        private void NewPlacementLocked()
        {
            _placementGeneration++;
            _placementRequestedAt = SolveClock.Now;
            _error = null;
            NewAdviceLocked();

            // 돌고 있는 풀이의 답은 이제 쓰이지 않는다. 실측에서 한 번이 1초, 할당 2GB 까지
            // 갔으므로 그냥 끝까지 두면 그만큼을 버리는 셈이다.
            _running?.Cancellation.Cancel();

            var backlog = _placementGeneration - _placementPublished;
            if (backlog > _worstBacklog) _worstBacklog = backlog;
        }

        private void NewAdviceLocked()
        {
            _adviceGeneration++;
            _adviceError = null;
        }

        /// <summary>배치를 다시 풀어야 하는데 아직 시작하지 않았다.</summary>
        private bool PlacementNeededLocked() =>
            !_disposed && _snapshot is not null && _placementGeneration != _placementStarted;

        /// <summary>
        /// 지금 판의 배치가 게시돼 있는데 거기 붙은 조언이 낡았다. 낡은 배치(다시 푸는 중이거나
        /// 앞선 계산이 실패한 경우)에는 조언을 붙이지 않는다 - 새 판의 조언을 옛 배치에 얹으면
        /// 화면이 섞인 답을 보여 준다.
        /// </summary>
        private bool AdviceNeededLocked() =>
            !_disposed && _error is null && _latest is not null &&
            _latest.RequestGeneration == _placementGeneration &&
            _adviceGeneration != _adviceStarted;

        private static Plan? Build(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
            out PlanBlocker blocker, Plan? previous, LayoutCache layouts, bool settled,
            CancellationToken cancellation) =>
            PlanBuilder.BuildPlacement(
                snapshot, catalog, preferences, out blocker, previous, layouts, settled, cancellation);

        private static Plan? Advise(
            Plan placement, GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
            LayoutCache layouts, CancellationToken cancellation) =>
            PlanBuilder.BuildAdvice(placement, snapshot, catalog, preferences, layouts, cancellation);

        /// <summary>
        /// 자동 배치가 검증까지 통과했다고 알린다. 다음 판이 그 계획 그대로면 탐색을 건너뛴다.
        /// 실패했거나 서버 반영이 불확실한 적용에는 부르지 않는다 - 그때는 무엇이 놓였는지 모른다.
        /// </summary>
        public void MarkApplied(Plan plan)
        {
            lock (_gate)
            {
                if (_disposed || plan is null || _replaySnapshot is null) return;

                _applied = plan;
                _appliedBefore = _replaySnapshot;
            }
        }

        public PlanReplay? CaptureReplay()
        {
            lock (_gate)
            {
                if (_latest is null || _replaySnapshot is null || _replayPreferences is null) return null;
                if (!(_catalog is Catalog catalog))
                    throw new InvalidOperationException("재현 자료로 내보낼 수 없는 카탈로그입니다.");
                return new PlanReplay
                {
                    Version = PlanReplay.CurrentVersion,
                    CatalogVersion = PlannerData.CatalogVersion,
                    CoreBuild = PlanReplay.CurrentCoreBuild,
                    CapturedUtc = DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
                    RequestedGeneration = _placementGeneration,
                    PublishedGeneration = _latest.RequestGeneration,
                    LatestError = _error ?? "",
                    CatalogGeneration = _latest.CatalogGeneration,
                    RequestFingerprint = _latest.RequestFingerprint,

                    // 조언이 아직 안 붙은 계획을 잡았으면 기준 결과에서 조언을 견주지 않는다.
                    // 재현은 언제나 조언까지 풀므로, 그대로 견주면 없는 것과 있는 것이 붙는다.
                    AdviceComplete = _latest.AdviceStatus != AdviceStatus.Pending,
                    Snapshot = _replaySnapshot,
                    Preferences = ReplayPreferences.From(_replayPreferences),
                    Catalog = catalog.Export(),
                    PreviousTargets = new System.Collections.Generic.List<PlanTarget>(
                        _replayPreviousTargets ?? new System.Collections.Generic.List<PlanTarget>()),
                    Expected = ReplayResult.From(_latest),
                };
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _running?.Cancellation.Cancel();
                _latest = null;
                _snapshot = null;
                _applied = null;
                _appliedBefore = null;
                _replaySnapshot = null;
                _replayPreferences = null;
                _replayPreviousTargets = null;
                _layouts = new LayoutCache();
                _layoutsContext = "";
                _placementPublished = 0;
                _advicePublished = 0;
            }
        }

        /// <summary>
        /// 다음에 할 일 하나를 시작한다. <b>배치가 조언보다 먼저다</b> - 배치가 낡았으면 그
        /// 배치에 붙일 조언도 낡았다.
        ///
        /// 취소는 협조적이라 취소를 걸어도 그 스레드가 아직 캐시를 만지는 중일 수 있다. 그래서
        /// 다음 작업은 여기서만 시작한다 - 부를 때 <c>_running</c> 이 비어 있다는 것이 곧 앞
        /// 작업이 실제로 빠져나왔다는 뜻이다.
        /// </summary>
        private void StartNextLocked()
        {
            if (_running is not null) return;

            if (PlacementNeededLocked())
            {
                _placementStarted = _placementGeneration;
                StartLocked(new Request
                {
                    Generation = _placementGeneration,
                    Snapshot = _snapshot!,
                    Preferences = _preferences,
                    Fingerprint = _fullFingerprint,
                    PlacementFingerprint = _placementFingerprint,
                    PlanningContextFingerprint = _contextFingerprint,
                    CatalogGeneration = _catalogGeneration,
                    Previous = _latest,
                    RequestedAt = _placementRequestedAt,
                    Settled = _settled,
                });
                return;
            }

            if (!AdviceNeededLocked()) return;

            _adviceStarted = _adviceGeneration;
            StartLocked(new Request
            {
                Advice = true,
                Generation = _adviceGeneration,
                Placement = _latest,
                Snapshot = _snapshot!,
                Preferences = _preferences,
                Fingerprint = _fullFingerprint,
                PlacementFingerprint = _placementFingerprint,
                PlanningContextFingerprint = _contextFingerprint,
                CatalogGeneration = _catalogGeneration,
            });
        }

        private void StartLocked(Request request)
        {
            _running = request;
            ThreadPool.QueueUserWorkItem(_ => Execute(request));
        }

        private void Execute(Request request)
        {
            LayoutCache layouts;
            lock (_gate)
            {
                if (_layoutsContext != request.PlanningContextFingerprint)
                {
                    _layouts = new LayoutCache();
                    _layoutsContext = request.PlanningContextFingerprint;
                }
                layouts = _layouts;
            }

            Plan? plan = null;
            var blocker = PlanBlocker.None;
            Exception? failure = null;
            var startedAt = SolveClock.Now;
            try
            {
                if (request.Advice)
                {
                    plan = _advise(
                        request.Placement!, request.Snapshot, _catalog, request.Preferences, layouts,
                        request.Cancellation.Token);
                }
                else
                {
                    plan = _build(
                        request.Snapshot, _catalog, request.Preferences, out blocker, request.Previous,
                        layouts, request.Settled, request.Cancellation.Token);
                    if (plan is not null)
                    {
                        plan.RequestGeneration = request.Generation;
                        plan.RequestFingerprint = request.Fingerprint;
                        plan.PlacementFingerprint = request.PlacementFingerprint;
                        plan.PlanningContextFingerprint = request.PlanningContextFingerprint;
                        plan.CatalogGeneration = request.CatalogGeneration;
                    }
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            // 잠금을 기다린 시간은 풀이에 든 시간이 아니다. 자물쇠 밖에서 끊는다.
            var elapsedMs = SolveClock.MsSince(startedAt);

            lock (_gate)
            {
                if (request.Advice) FinishAdviceLocked(request, plan, failure, elapsedMs);
                else FinishPlacementLocked(request, plan, blocker, failure, elapsedMs);

                _running = null;
                StartNextLocked();

                // 다 쓴 요청이다. 자물쇠 안이라 취소를 거는 쪽과 겹치지 않고, _running 에서
                // 이미 떼어 냈으므로 이제 아무도 닿지 못한다.
                request.Dispose();
            }
        }

        private void FinishPlacementLocked(
            Request request, Plan? plan, PlanBlocker blocker, Exception? failure, double elapsedMs)
        {
            // 취소된 요청의 결과는 도중에 그만둔 것이라 쓸 수 없다. 세대 검사만으로도 걸리지만,
            // 반쪽짜리 계획을 최신이라고 게시하는 일만은 확실히 막아 둔다.
            var usable = !request.Cancellation.IsCancellationRequested &&
                         request.Generation == _placementGeneration;
            _placementStat.Add(elapsedMs, !usable);
            if (!usable) return;

            _placementPublished = request.Generation;
            if (failure is not null)
            {
                _error = failure.Message;
                _blocker = PlanBlocker.None;
                _retryAfterUtc = DateTime.UtcNow + _retryDelay;
                return;
            }

            _latest = plan;
            _replaySnapshot = request.Snapshot;
            _replayPreferences = request.Preferences;
            _replayPreviousTargets = new System.Collections.Generic.List<PlanTarget>(
                request.Previous?.Targets ?? new System.Collections.Generic.List<PlanTarget>());
            _blocker = blocker;
            _error = null;

            // 새 배치의 조언은 아직 아무것도 아니다. 0 은 어느 요청 세대와도 같지 않다.
            _advicePublished = 0;

            _published++;
            _publishDelayMs = SolveClock.MsSince(request.RequestedAt);
            if (_publishDelayMs > _worstPublishDelayMs)
            {
                _worstPublishDelayMs = _publishDelayMs;
                _worstPublishRun = _published;
            }
        }

        private void FinishAdviceLocked(Request request, Plan? plan, Exception? failure, double elapsedMs)
        {
            // 붙일 배치가 그 사이에 바뀌었으면 이 조언은 남의 판에 대한 답이다.
            var usable = !request.Cancellation.IsCancellationRequested &&
                         request.Generation == _adviceGeneration &&
                         ReferenceEquals(_latest, request.Placement);
            _adviceStat.Add(elapsedMs, !usable);
            if (!usable) return;

            if (failure is not null)
            {
                // 배치는 멀쩡하다. 조언 칸만 낡은 채로 두고 그 사실을 적는다.
                _adviceError = failure.Message;
                return;
            }

            // 취소로 돌아온 null 은 여기까지 오지 않는다(위의 usable 검사). 그래도 조립기가
            // 아무것도 내놓지 않으면 배치를 그대로 둔다.
            if (plan is null) return;

            // 재현 자료는 조언까지 포함해 다시 풀리므로, 조언이 쓴 판이 곧 그 자료의 판이다.
            // 후보만 바뀐 경우 그 판은 배치 때의 것이 아니라 더 뒤의 것이고, 전체 지문도 그쪽이다.
            plan.RequestFingerprint = request.Fingerprint;
            _replaySnapshot = request.Snapshot;
            _replayPreferences = request.Preferences;
            _latest = plan;
            _advicePublished = request.Generation;
        }
    }
}

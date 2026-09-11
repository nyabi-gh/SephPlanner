using System;
using System.Threading;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Solver;

namespace SephPlanner.Core.Runtime
{
    public delegate Plan? PlanBuildOperation(
        GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
        out PlanBlocker blocker, Plan? previous, LayoutCache layouts, CancellationToken cancellation);

    public sealed class PlanRunState
    {
        public Plan? Latest { get; set; }
        public string? Error { get; set; }
        public PlanBlocker Blocker { get; set; }
        public long RequestedGeneration { get; set; }
        public long PublishedGeneration { get; set; }
        public bool IsBusy { get; set; }
        public bool HasPending { get; set; }

        public bool IsCurrent => Error is null && RequestedGeneration > 0 &&
                                 PublishedGeneration == RequestedGeneration;
    }

    public sealed class PlanRunner : IDisposable
    {
        private sealed class Request : IDisposable
        {
            public long Generation;
            public GameSnapshot Snapshot = new GameSnapshot();
            public PlanPreferences Preferences = PlanPreferences.None;
            public string Fingerprint = "";
            public string PlacementFingerprint = "";
            public string PlanningContextFingerprint = "";
            public string CatalogGeneration = "";
            public Plan? Previous;

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
        private Request? _pending;
        private Plan? _latest;
        private GameSnapshot? _replaySnapshot;
        private PlanPreferences? _replayPreferences;
        private System.Collections.Generic.List<PlanTarget>? _replayPreviousTargets;
        private string? _error;
        private PlanBlocker _blocker;
        private long _requestedGeneration;
        private long _publishedGeneration;
        private string _requestedFingerprint = "";
        private DateTime _retryAfterUtc;
        private bool _disposed;

        public PlanRunner(ICatalog catalog)
            : this(catalog, Build, TimeSpan.FromSeconds(1))
        {
        }

        public PlanRunner(ICatalog catalog, PlanBuildOperation build, TimeSpan? retryDelay = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _build = build ?? throw new ArgumentNullException(nameof(build));
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
                        Blocker = _blocker,
                        RequestedGeneration = _requestedGeneration,
                        PublishedGeneration = _publishedGeneration,
                        IsBusy = _running is not null,
                        HasPending = _pending is not null,
                    };
                }
            }
        }

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
                var sameRequest = fingerprint == _requestedFingerprint;
                if (sameRequest && (_running is not null || _pending is not null ||
                                    _error is null && _publishedGeneration == _requestedGeneration))
                    return _requestedGeneration;
                if (sameRequest && DateTime.UtcNow < _retryAfterUtc) return _requestedGeneration;

                var request = new Request
                {
                    Generation = ++_requestedGeneration,
                    Snapshot = snapshot,
                    Preferences = preferences,
                    Fingerprint = fingerprint,
                    PlacementFingerprint = placementFingerprint,
                    PlanningContextFingerprint = contextFingerprint,
                    CatalogGeneration = catalogGeneration,
                    Previous = _latest,
                };
                _requestedFingerprint = fingerprint;
                _error = null;

                if (_running is not null)
                {
                    // 돌고 있는 풀이의 답은 이제 쓰이지 않는다. 실측에서 한 번이 1초, 할당
                    // 2GB 까지 갔으므로 그냥 끝까지 두면 그만큼을 버리는 셈이다.
                    _running.Cancellation.Cancel();

                    // 시작도 못 한 채 밀려난 요청. 여기서 정리하지 않으면 그대로 새어 나간다.
                    _pending?.Dispose();
                    _pending = request;
                }
                else
                {
                    StartLocked(request);
                }
                return request.Generation;
            }
        }

        private static Plan? Build(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
            out PlanBlocker blocker, Plan? previous, LayoutCache layouts, CancellationToken cancellation) =>
            PlanBuilder.Build(snapshot, catalog, preferences, out blocker, previous, layouts, cancellation);

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
                    RequestedGeneration = _requestedGeneration,
                    PublishedGeneration = _latest.RequestGeneration,
                    LatestError = _error ?? "",
                    CatalogGeneration = _latest.CatalogGeneration,
                    RequestFingerprint = _latest.RequestFingerprint,
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
                _pending?.Dispose();
                _pending = null;
                _latest = null;
                _replaySnapshot = null;
                _replayPreferences = null;
                _replayPreviousTargets = null;
                _layouts = new LayoutCache();
                _layoutsContext = "";
                _publishedGeneration = 0;
            }
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
            try
            {
                plan = _build(
                    request.Snapshot, _catalog, request.Preferences, out blocker, request.Previous,
                    layouts, request.Cancellation.Token);
                if (plan is not null)
                {
                    plan.RequestGeneration = request.Generation;
                    plan.RequestFingerprint = request.Fingerprint;
                    plan.PlacementFingerprint = request.PlacementFingerprint;
                    plan.PlanningContextFingerprint = request.PlanningContextFingerprint;
                    plan.CatalogGeneration = request.CatalogGeneration;
                }
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            lock (_gate)
            {
                // 취소된 요청의 결과는 도중에 그만둔 것이라 쓸 수 없다. 세대 검사만으로도 걸리지만,
                // 반쪽짜리 계획을 최신이라고 게시하는 일만은 확실히 막아 둔다.
                if (!request.Cancellation.IsCancellationRequested &&
                    request.Generation == _requestedGeneration)
                {
                    _publishedGeneration = request.Generation;
                    if (failure is null)
                    {
                        _latest = plan;
                        _replaySnapshot = request.Snapshot;
                        _replayPreferences = request.Preferences;
                        _replayPreviousTargets = new System.Collections.Generic.List<PlanTarget>(
                            request.Previous?.Targets ?? new System.Collections.Generic.List<PlanTarget>());
                        _blocker = blocker;
                        _error = null;
                    }
                    else
                    {
                        _error = failure.Message;
                        _blocker = PlanBlocker.None;
                        _retryAfterUtc = DateTime.UtcNow + _retryDelay;
                    }
                }

                _running = null;
                if (_pending is not null)
                {
                    var next = _pending;
                    _pending = null;
                    next.Previous = _latest;
                    StartLocked(next);
                }

                // 다 쓴 요청이다. 자물쇠 안이라 취소를 거는 쪽과 겹치지 않고, _running 에서
                // 이미 떼어 냈으므로 이제 아무도 닿지 못한다.
                request.Dispose();
            }
        }
    }
}

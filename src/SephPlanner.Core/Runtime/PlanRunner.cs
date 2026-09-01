using System;
using System.Threading;
using SephPlanner.Core.Planning;

namespace SephPlanner.Core.Runtime
{
    public delegate Plan? PlanBuildOperation(
        GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
        out PlanBlocker blocker, Plan? previous);

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

    public sealed class PlanRunner
    {
        private sealed class Request
        {
            public long Generation;
            public GameSnapshot Snapshot = new GameSnapshot();
            public PlanPreferences Preferences = PlanPreferences.None;
            public string Fingerprint = "";
            public string PlacementFingerprint = "";
            public string PlanningContextFingerprint = "";
            public string CatalogGeneration = "";
            public Plan? Previous;
        }

        private readonly object _gate = new object();
        private readonly ICatalog _catalog;
        private readonly PlanBuildOperation _build;
        private readonly TimeSpan _retryDelay;

        private Request? _running;
        private Request? _pending;
        private Plan? _latest;
        private string? _error;
        private PlanBlocker _blocker;
        private long _requestedGeneration;
        private long _publishedGeneration;
        private string _requestedFingerprint = "";
        private DateTime _retryAfterUtc;

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

            var fingerprint = PlanFingerprint.Full(snapshot, preferences, catalogGeneration);
            var placementFingerprint = PlanFingerprint.Placement(snapshot, preferences, catalogGeneration);
            var contextFingerprint = PlanFingerprint.PlanningContext(preferences, catalogGeneration);

            lock (_gate)
            {
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
                    _pending = request;
                else
                    StartLocked(request);
                return request.Generation;
            }
        }

        private static Plan? Build(
            GameSnapshot snapshot, ICatalog catalog, PlanPreferences preferences,
            out PlanBlocker blocker, Plan? previous) =>
            PlanBuilder.Build(snapshot, catalog, preferences, out blocker, previous);

        private void StartLocked(Request request)
        {
            _running = request;
            ThreadPool.QueueUserWorkItem(_ => Execute(request));
        }

        private void Execute(Request request)
        {
            Plan? plan = null;
            var blocker = PlanBlocker.None;
            Exception? failure = null;
            try
            {
                plan = _build(request.Snapshot, _catalog, request.Preferences, out blocker, request.Previous);
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
                if (request.Generation == _requestedGeneration)
                {
                    _publishedGeneration = request.Generation;
                    if (failure is null)
                    {
                        _latest = plan;
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
            }
        }
    }
}

#nullable disable
using System;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;
using SephPlanner.Core.Solver;

namespace SephPlanner.Plugin.Ui
{
    /// <summary>
    /// 한 번 그리는 데 필요한 것 전부. 인자가 늘어날 때마다 서명을 고치는 대신 여기에 담는다.
    /// </summary>
    internal sealed class HudFrame
    {
        public GameSnapshot Snapshot;
        public Plan Plan;
        public ICatalog Catalog;
        public PluginPreferences Prefs;
        public CharmValueBook Values = CharmValueBook.Empty;
        public bool Expanded;
        public bool MixerOpen;
        public bool Recommendations = true;
        public bool MultiplayerAutoPlace;
        public bool QueryVerified;
        public PlanVerificationStatus RuntimeVerification;
        public string RuntimeVerificationReason = "";
        public string Hint = "";

        /// <summary>안내 줄이 지금 미리보기를 설명하고 있는가. 그때는 색이 달라야 눈에 든다.</summary>
        public bool HintIsPreview;

        /// <summary>보여주는 계획이 지금 가방보다 낡았는가. 다시 푸는 동안 직전 계획을 그대로 둔다.</summary>
        public bool Stale;

        /// <summary>보고 있는 후보의 <see cref="OfferAdvice.Key"/>. 빈 문자열이면 미리보기가 없다.</summary>
        public string PreviewKey = "";

        public int Gold => Snapshot?.Run?.Gold ?? 0;
    }

    /// <summary>
    /// 한 번 그린 근거. 이것이 그대로면 그린 결과도 그대로다.
    ///
    /// <c>NativeHud.Render</c>는 매 프레임 불리지만 내용이 바뀌는 것은 폴링마다 한 번이다.
    /// 프레임마다 격자와 목록을 다시 지으면 아무것도 달라지지 않은 프레임에서도 쓰레기가
    /// 쌓이고, 그것이 결국 게임을 세운다 - 이 게임은 증분 GC 가 프레임당 3ms 를 가져간다
    /// (<c>Sephiria_Data/boot.config</c>의 <c>gc-max-time-slice</c>).
    /// </summary>
    internal readonly struct HudFrameKey
    {
        private readonly object _snapshot;
        private readonly object _plan;
        private readonly object _catalog;
        private readonly object _values;
        private readonly string _hint;
        private readonly string _reason;
        private readonly string _previewKey;
        private readonly PlanVerificationStatus _verification;
        private readonly int _prefs;
        private readonly bool _expanded;
        private readonly bool _mixerOpen;
        private readonly bool _recommendations;
        private readonly bool _queryVerified;
        private readonly bool _hintIsPreview;
        private readonly bool _multiplayerAutoPlace;
        private readonly bool _stale;

        public HudFrameKey(HudFrame frame)
        {
            _snapshot = frame.Snapshot;
            _plan = frame.Plan;
            _catalog = frame.Catalog;
            _values = frame.Values;
            _hint = frame.Hint;
            _reason = frame.RuntimeVerificationReason;
            _previewKey = frame.PreviewKey;
            _verification = frame.RuntimeVerification;

            // 빌드 창에서 강화 우선이나 밀고 있는 콤보를 바꾸면, 계획이 다시 풀리기 전에도
            // 격자와 칩의 표시가 달라진다. 개정 번호가 그 순간을 잡는다.
            _prefs = frame.Prefs != null ? frame.Prefs.Revision : -1;
            _expanded = frame.Expanded;
            _mixerOpen = frame.MixerOpen;
            _recommendations = frame.Recommendations;
            _queryVerified = frame.QueryVerified;
            _hintIsPreview = frame.HintIsPreview;

            // 멀티 동의 안내가 이 값으로 갈린다. 빠뜨리면 설정을 바꾼 직후 옛 문구가 남는다.
            _multiplayerAutoPlace = frame.MultiplayerAutoPlace;

            // 계획 객체는 그대로인 채 이 표시만 켜지고 꺼진다.
            _stale = frame.Stale;
        }

        public bool Matches(in HudFrameKey other) =>
            ReferenceEquals(_snapshot, other._snapshot) &&
            ReferenceEquals(_plan, other._plan) &&
            ReferenceEquals(_catalog, other._catalog) &&
            ReferenceEquals(_values, other._values) &&
            _verification == other._verification &&
            _prefs == other._prefs &&
            _expanded == other._expanded &&
            _mixerOpen == other._mixerOpen &&
            _recommendations == other._recommendations &&
            _queryVerified == other._queryVerified &&
            _hintIsPreview == other._hintIsPreview &&
            _multiplayerAutoPlace == other._multiplayerAutoPlace &&
            _stale == other._stale &&
            string.Equals(_hint, other._hint, StringComparison.Ordinal) &&
            string.Equals(_reason, other._reason, StringComparison.Ordinal) &&
            string.Equals(_previewKey, other._previewKey, StringComparison.Ordinal);
    }

}

using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 되뺀 고정 효과 층을 채택할지 판단한다.
    ///
    /// <para>
    /// 되뺀 값은 정의상 게임 행렬과 맞으므로 성질로 가른다 - 고정 효과는 <c>FixedEngraving</c>이
    /// 만들 때 구워 두는 칸별 표라 아이템을 옮겨도 변하지 않고, 시뮬레이터의 오차는 석판 조건이
    /// 점유를 보기 때문에 배치를 따라 변한다.
    /// </para>
    ///
    /// 한 번은 우연일 수 있어 - 그 사이 보상 석판을 바로 각인하면 아무 흔적도 남지 않는다 -
    /// 연속 두 번부터 어긋남으로 알린다.
    /// </summary>
    public sealed class FixedEffectTracker
    {
        private const int StrikesBeforeContradiction = 2;

        private List<FixedEffectCell>? _layer;
        private string _sources = "";
        private string _arrangement = "";
        private int _strikes;

        public IReadOnlyList<FixedEffectCell> Layer =>
            _layer ?? (IReadOnlyList<FixedEffectCell>)Array.Empty<FixedEffectCell>();

        /// <summary>층을 계산에 써도 되는가.</summary>
        public bool Trustworthy { get; private set; }

        /// <summary>배치를 따라 변해서 고정 효과로 볼 수 없는 상태인가.</summary>
        public bool Contradicted { get; private set; }

        /// <summary>층을 쓸 수 없을 때의 이유. 정상이면 빈 문자열이다.</summary>
        public string Reason { get; private set; } = "";

        public bool ConfirmedAcrossArrangements { get; private set; }

        public void Reset()
        {
            _layer = null;
            _sources = "";
            _arrangement = "";
            _strikes = 0;
            Trustworthy = false;
            Contradicted = false;
            ConfirmedAcrossArrangements = false;
            Reason = "";
        }

        /// <param name="sources">보이는 석판·각인의 자리와 회전까지 담은 지문.</param>
        /// <param name="arrangement">아이템 점유 지문. 석판이 그대로인데 이것만 달라지면 검사 기회다.</param>
        public void Observe(FixedEffectResidualResult residual, string sources, string arrangement)
        {
            if (residual.Status == FixedEffectResidualStatus.Unsettled)
            {
                Reason = residual.Reason;
                Trustworthy = _layer != null && !Contradicted;
                return;
            }

            if (residual.Status == FixedEffectResidualStatus.Overproduced)
            {
                Contradicted = true;
                Trustworthy = false;
                Reason = residual.Reason;
                return;
            }

            if (_layer == null)
            {
                Adopt(residual.Cells, sources, arrangement);
                return;
            }

            if (FixedEffectResidual.Same(_layer, residual.Cells))
            {
                if (arrangement != _arrangement)
                {
                    ConfirmedAcrossArrangements = true;
                    _strikes = 0;
                    Contradicted = false;
                }
                _sources = sources;
                _arrangement = arrangement;
                Trustworthy = true;
                Reason = "";
                return;
            }

            // 각인하면 석판이 가방에서 사라지므로 층이 바뀌는 것이 당연하다.
            if (sources != _sources || arrangement == _arrangement)
            {
                Adopt(residual.Cells, sources, arrangement);
                return;
            }

            _strikes++;
            Adopt(residual.Cells, sources, arrangement, keepStrikes: true);
            if (_strikes < StrikesBeforeContradiction) return;

            Contradicted = true;
            Trustworthy = false;
            ConfirmedAcrossArrangements = false;
            Reason = "석판이 그대로인데 아이템 배치를 따라 칸 효과가 바뀝니다. " +
                     "고정 효과로 설명할 수 없어 시뮬레이터가 실제와 다릅니다.";
        }

        private void Adopt(List<FixedEffectCell> cells, string sources, string arrangement, bool keepStrikes = false)
        {
            _layer = cells;
            _sources = sources;
            _arrangement = arrangement;
            if (!keepStrikes) _strikes = 0;
            ConfirmedAcrossArrangements = false;
            Contradicted = false;
            Trustworthy = true;
            Reason = "";
        }
    }
}

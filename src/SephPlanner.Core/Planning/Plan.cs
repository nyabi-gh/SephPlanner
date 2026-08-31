using System.Collections.Generic;
using SephPlanner.Core.Ipc;
using SephPlanner.Core.Model;
using SephPlanner.Core.Solver;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Planning
{
    /// <summary>무엇을 어디로 옮기라는 한 줄.</summary>
    public sealed class Move
    {
        public Move(string label, GridPos from, GridPos to, string detail)
        {
            Label = label;
            From = from;
            To = to;
            Detail = detail;
        }

        public string Label { get; }
        public GridPos From { get; }
        public GridPos To { get; }
        public string Detail { get; }
    }

    /// <summary>카탈로그의 표시 이름을 꺼내는 규칙. 이름이 없으면 내부 식별자로 물러선다.</summary>
    public static class Naming
    {
        public const string CurrentLanguage = "current";

        public static string Of(Dictionary<string, string> names, string id, string fallback)
        {
            if (names.TryGetValue(CurrentLanguage, out var text) && text.Length > 0) return text;
            return id.Length > 0 ? id : fallback;
        }

        /// <summary>석판은 플레이어가 붙인 이름이 있으면 그쪽이 먼저다. 합성 석판이 여기 해당한다.</summary>
        public static string OfTablet(TabletPlacement placement)
        {
            if (!string.IsNullOrEmpty(placement.InstanceName)) return placement.InstanceName!;
            return Of(placement.Definition.Names, placement.Definition.Id, "석판");
        }
    }

    /// <summary>지금 배치와 제안, 그리고 그 차이를 설명하는 데 필요한 것들.</summary>
    public sealed class Plan
    {
        public Arrangement Current { get; set; } = new Arrangement();
        public Arrangement Best { get; set; } = new Arrangement();
        public List<Move> Moves { get; set; } = new List<Move>();
        public List<OfferAdvice> Offers { get; set; } = new List<OfferAdvice>();

        /// <summary>
        /// 후보가 너무 많아 평가하지 못하고 넘어간 수. 조용히 빠뜨리면 화면에 없는 선택지를
        /// 없는 셈 치게 된다.
        /// </summary>
        public int SkippedOffers { get; set; }

        /// <summary>
        /// 게임이 계산해 둔 레벨과 우리 계산이 어긋난 칸 수. 0이 아니면 우리가 읽지 않는 효과가
        /// 걸려 있다는 뜻이라, 점수를 그대로 믿으면 안 된다.
        /// </summary>
        public int LevelMismatches { get; set; }

        /// <summary>제안된 배치에서 각 칸에 놓이는 아이템의 이름. 격자에 그대로 보여준다.</summary>
        public Dictionary<GridPos, string> Names { get; set; } = new Dictionary<GridPos, string>();

        /// <summary>제안된 배치에서 각 칸에 놓이는 아티팩트의 엔티티 번호. 강화 우선 지정에 쓴다.</summary>
        public Dictionary<GridPos, int> Charms { get; set; } = new Dictionary<GridPos, int>();

        /// <summary>자동 배치 명령에 실어 보낼 최종 배치. 인스턴스마다 있어야 할 자리다.</summary>
        public List<PlanTarget> Targets { get; set; } = new List<PlanTarget>();

        public double Gain => Best.Score - Current.Score;
    }
}

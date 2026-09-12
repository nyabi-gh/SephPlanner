using System.Collections.Generic;
using SephPlanner.Core.Model;
using SephPlanner.Core.Runtime;
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

        /// <summary>
        /// 레어도를 게임이 부르는 대로. 게임 로컬라이제이션의 <c>ItemRarity_*</c> 값 그대로이며,
        /// "커먼/레어" 같은 커뮤니티 은어를 화면이나 도구 출력에 쓰지 않기 위한 것이다.
        /// 다국어를 하게 되면 이 표는 카탈로그 덤프로 옮긴다.
        /// </summary>
        public static string OfRarity(Rarity rarity) => rarity switch
        {
            Rarity.Uncommon => "고급",
            Rarity.Rare => "희귀",
            Rarity.Legend => "전설",
            Rarity.Eternal => "영원",
            _ => "일반",
        };

        /// <summary>석판은 플레이어가 붙인 이름이 있으면 그쪽이 먼저다. 합성 석판이 여기 해당한다.</summary>
        public static string OfTablet(TabletPlacement placement)
        {
            if (!string.IsNullOrEmpty(placement.InstanceName)) return placement.InstanceName!;
            return Of(placement.Definition.Names, placement.Definition.Id, "석판");
        }
    }

    /// <summary>조언(합성·후보·제거)이 이 계획에 붙어 있는가.</summary>
    public enum AdviceStatus
    {
        /// <summary>추천을 끈 세션이다. 계산하지 않는다.</summary>
        NotRequested,

        /// <summary>배치는 나왔고 조언은 뒤따라 푸는 중이다. 화면은 "계산 중" 으로 적는다.</summary>
        Pending,

        /// <summary>조언까지 이 계획의 것이다.</summary>
        Ready,
    }

    /// <summary>지금 배치와 제안, 그리고 그 차이를 설명하는 데 필요한 것들.</summary>
    public sealed class Plan
    {
        public Arrangement Current { get; set; } = new Arrangement();
        public Arrangement Best { get; set; } = new Arrangement();
        public List<Move> Moves { get; set; } = new List<Move>();
        public List<string> ComboPlacementWarnings { get; set; } = new List<string>();
        public List<string> RetentionWarnings { get; set; } = new List<string>();
        public List<string> SupportWarnings { get; set; } = new List<string>();
        public List<string> ActivationWarnings { get; set; } = new List<string>();
        public bool HasUnapprovedDeactivation { get; set; }
        public bool ManualMoveInstructionsAvailable { get; set; } = true;
        public bool HasPlacementChanges { get; set; }
        public int InventoryWidth { get; set; }
        public int InventoryHeight { get; set; }
        public int InventoryStorage { get; set; }
        public List<OfferAdvice> Offers { get; set; } = new List<OfferAdvice>();
        public List<DiscardAdvice> Discards { get; set; } = new List<DiscardAdvice>();

        /// <summary>
        /// 가진 석판 중 합치면 좋은 쌍. 합성기가 이 층에 있고 아직 쓰지 않았을 때만 채워진다.
        /// </summary>
        public List<MixAdvice> Mixes { get; set; } = new List<MixAdvice>();

        /// <summary>
        /// 후보가 너무 많아 평가하지 못하고 넘어간 수. 조용히 빠뜨리면 화면에 없는 선택지를
        /// 없는 셈 치게 된다.
        /// </summary>
        public int SkippedOffers { get; set; }

        public PlanVerification Verification { get; set; } = new PlanVerification();

        /// <summary>기존 진단 도구와 표시 코드가 쓰는 레벨 차이 수.</summary>
        public int LevelMismatches => Verification.LevelMismatches;

        /// <summary>제안된 배치에서 각 칸에 놓이는 아이템의 이름. 격자에 그대로 보여준다.</summary>
        public Dictionary<GridPos, string> Names { get; set; } = new Dictionary<GridPos, string>();

        /// <summary>제안된 배치에서 각 칸에 놓이는 아티팩트의 엔티티 번호. 강화 우선 지정에 쓴다.</summary>
        public Dictionary<GridPos, int> Charms { get; set; } = new Dictionary<GridPos, int>();

        /// <summary>자동 배치 명령에 실어 보낼 최종 배치. 인스턴스마다 있어야 할 자리다.</summary>
        public List<PlanTarget> Targets { get; set; } = new List<PlanTarget>();

        /// <summary>
        /// 조언 칸의 상태. 배치만 게시된 계획은 <see cref="AdviceStatus.Pending"/> 이고, 그때
        /// <see cref="Offers"/>·<see cref="Mixes"/>·<see cref="Discards"/> 가 빈 것은 "없다" 가
        /// 아니라 "아직" 이다. 그 둘을 가르지 않으면 화면이 거짓말을 한다.
        /// </summary>
        public AdviceStatus AdviceStatus { get; set; } = AdviceStatus.NotRequested;

        /// <summary>
        /// 조언 단계가 쓸 1단계의 중간물. 계획 밖으로 나가지 않고 직렬화하지도 않는다 - 재현
        /// 자료에는 스냅샷과 설정이 있으므로 이것을 저장할 이유가 없다.
        /// </summary>
        internal PlacementWork? Work { get; set; }

        /// <summary>
        /// 조언만 갈아 끼운 새 계획. <b>게시된 계획을 제자리에서 고치지 않는다</b> - 화면이 매
        /// 프레임 읽으므로 반쯤 채워진 목록을 보게 된다. 배치 쪽 값은 만들어진 뒤로 바뀌지
        /// 않으므로 그대로 나눠 쓴다.
        /// </summary>
        internal Plan WithAdvice(
            List<OfferAdvice> offers, List<MixAdvice> mixes, List<DiscardAdvice> discards,
            int skippedOffers, AdviceStatus status)
        {
            var copy = (Plan)MemberwiseClone();
            copy.Offers = offers;
            copy.Mixes = mixes;
            copy.Discards = discards;
            copy.SkippedOffers = skippedOffers;
            copy.AdviceStatus = status;
            return copy;
        }

        public long RequestGeneration { get; set; }
        public string RequestFingerprint { get; set; } = "";
        public string PlacementFingerprint { get; set; } = "";
        public string PlanningContextFingerprint { get; set; } = "";
        public string CatalogGeneration { get; set; } = "";
        public string ExpectedWeaponId { get; set; } = "";

        public double Gain => Best.Score - Current.Score;

        public ApplyPlanCommand CreateApplyCommand() => new ApplyPlanCommand
        {
            ExpectedWidth = InventoryWidth,
            ExpectedHeight = InventoryHeight,
            ExpectedStorage = InventoryStorage,
            ExpectedPlacementFingerprint = PlacementFingerprint,
            ExpectedPlanningContextFingerprint = PlanningContextFingerprint,
            ExpectedWeaponId = ExpectedWeaponId,
            ExpectedCatalogGeneration = CatalogGeneration,
            Targets = new List<PlanTarget>(Targets),
            ExpectedCellLevels = new Dictionary<GridPos, int>(Best.CellLevels),
        };
    }
}

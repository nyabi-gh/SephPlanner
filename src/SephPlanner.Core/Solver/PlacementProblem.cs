using System;
using System.Collections.Generic;
using System.Threading;
using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;
using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Solver
{
    public sealed class CharmSlot
    {
        public CharmDefinition Definition { get; set; } = new CharmDefinition();
        public int InstanceId { get; set; }

        /// <summary>인챈트 등으로 붙은 고정 레벨. 석판과 무관하게 더해진다.</summary>
        public int Enchant { get; set; }

        /// <summary>
        /// 사용자가 얹은 가중치. 1이 기준이고, 강화 우선으로 찍으면 커진다. 아티팩트 사이의
        /// 본래 우열은 여기가 아니라 <see cref="Worth"/>가 답한다.
        /// </summary>
        public double Weight { get; set; } = 1;

        private CharmWorth? _worth;

        /// <summary>
        /// 이 아티팩트가 레벨마다 갖는 값어치. 채워 넣지 않으면 정의만 보고 정한다(손으로 채운
        /// 가치 없이 측정 표나 레어도로). 배정 비용 행렬의 최내곽에서 쓰이므로 한 번만 정한다.
        /// </summary>
        public CharmWorth Worth
        {
            get => _worth ??= CharmWorth.Resolve(Definition);
            set => _worth = value;
        }

        /// <summary>
        /// 소비 아이템처럼 점수에 기여하지 않지만 칸은 차지하는 것. 무시하면 그 칸이 빈 칸으로
        /// 취급되어 솔버가 이미 찬 자리에 아티팩트를 놓으라고 한다.
        /// </summary>
        public bool IsFiller { get; set; }

        /// <summary>
        /// 연동된 무기를 들고 있지 않아 효과가 꺼진 아티팩트. 점수에는 기여하지 않지만 이웃의
        /// 조건 판정에서는 여전히 아티팩트로 세므로 <see cref="IsFiller"/>와 다르다.
        /// </summary>
        public bool IsDormant { get; set; }

        /// <summary>
        /// 배치 조건을 무시하는 칸에만 앉힌다(<see cref="Planning.PlanPreferences.HeldCharms"/>). 그런 칸이
        /// 없으면 무시된다.
        /// </summary>
        public bool Held { get; set; }

        private CharmCriteriaKind? _criteria;

        /// <summary>
        /// 배치 조건의 종류. 배정 비용 행렬을 채우는 최내곽 루프에서 쓰이므로, 매번 타입 이름을
        /// 문자열로 풀지 않고 한 번만 해석해 둔다. Definition 은 생성 직후 바뀌지 않는다.
        /// </summary>
        public CharmCriteriaKind Criteria => _criteria ??= CharmCriteria.FromTypeName(Definition.CriteriaType);
    }

    public sealed class TabletSlot
    {
        public TabletDefinition Definition { get; set; } = new TabletDefinition();
        public int InstanceId { get; set; }
        public string? InstanceQuery { get; set; }
        public string? InstanceConditionQuery { get; set; }
        public string? InstanceName { get; set; }

        /// <summary>
        /// 이 슬롯을 돌려도 되는지에 대한 최종 답. 정의의 기본값을 여기에 다시 AND 하면 안 된다.
        /// 게임의 <c>DungeonManager.IsTabletRotatable</c>이 기본값을 이미 흡수한 뒤 인스턴스
        /// 오버라이드를 돌려주기 때문이다. 저주는 돌릴 수 있던 석판을 잠그고, 석판 합성은
        /// 반대로 정의상 돌릴 수 없는 합성 석판을 돌릴 수 있게 푼다. 채우는 쪽이 책임진다 —
        /// 가진 석판은 스냅샷 값, 아직 집지 않은 후보는 정의 값.
        /// </summary>
        public bool Rotatable { get; set; } = true;

        public TabletPlacement At(GridPos position, int rotation) => new TabletPlacement
        {
            Definition = Definition,
            Position = position,
            Rotation = rotation,
            InstanceQuery = InstanceQuery,
            InstanceConditionQuery = InstanceConditionQuery,
            InstanceName = InstanceName,
        };
    }

    public sealed class PlacementProblem
    {
        public GridSpec Grid { get; set; } = GridSpec.WithStorage(GridSpec.DefaultWidth * GridSpec.DefaultHeight);
        public List<CharmSlot> Charms { get; set; } = new List<CharmSlot>();
        public List<TabletSlot> Tablets { get; set; } = new List<TabletSlot>();

        /// <summary>
        /// 각인처럼 자리가 정해져 있어 옮길 수 없는 석판. 효과는 똑같이 내지만 칸을 차지하지 않아
        /// 그 자리에 아티팩트를 놓을 수 있다. 배치 탐색의 대상이 아니라 주어진 조건이다.
        /// </summary>
        public List<TabletPlacement> FixedTablets { get; set; } = new List<TabletPlacement>();

        /// <summary>고정 각인이 절대 좌표에 박아 둔 칸 효과. 역시 주어진 조건이다.</summary>
        public List<FixedEffectCell> FixedEffects { get; set; } = new List<FixedEffectCell>();

        /// <summary>
        /// 하얀 종이(양옆이 공유하는 카테고리를 물려받아 콤보에 +1)의 자리 가치를 매기는 데 쓴다.
        /// 없으면 하얀 종이는 평범한 아티팩트로만 평가된다.
        /// </summary>
        public IReadOnlyDictionary<string, int>? ComboCounts { get; set; }
        public Func<string, ComboDefinition?>? Combos { get; set; }
        public HashSet<string> PriorityCategories { get; set; } = new HashSet<string>();

        /// <summary>
        /// <see cref="ComboCounts"/>에서 자리 의존 아티팩트의 지금 자리 몫을 뺀 것. <see cref="ComboCounting"/>이
        /// 처음 읽을 때 짓는다. 개수나 지금 자리를 바꾼 뒤에는 null 로 되돌려야 다시 짓는다.
        /// </summary>
        public Dictionary<string, int>? BaseComboCounts { get; set; }

        /// <summary>
        /// 지금 놓여 있는 자리. 여기서 벗어나는 자리마다 솔버가 이사 비용을 문다 - 점수는 끝 상태만
        /// 세므로, 이것이 없으면 아무것도 달라지지 않았는데도 제안이 이리저리 바뀌고 티끌만 한
        /// 이득에 판 전체를 뒤집으라고 한다.
        /// </summary>
        public Dictionary<int, TabletSpot> CurrentTablets { get; } = new Dictionary<int, TabletSpot>();
        public Dictionary<int, GridPos> CurrentCharms { get; } = new Dictionary<int, GridPos>();

        /// <summary>
        /// 직전 제안이 앉힌 자리. 동점 배치가 여럿일 때 저번에 제안한 쪽을 고르는 데 쓴다.
        /// 이것이 없으면 제안을 한 수씩 따라가는 동안 남은 목표들이 저희끼리 자리를 맞바꾼다 -
        /// 실제 멀티 세션 기록에서 걸음마다 두 목표가 서로 뒤집히는 것이 관측됐다.
        /// </summary>
        public Dictionary<int, TabletSpot> PlannedTablets { get; } = new Dictionary<int, TabletSpot>();
        public Dictionary<int, GridPos> PlannedCharms { get; } = new Dictionary<int, GridPos>();
    }

    public readonly struct TabletSpot
    {
        public readonly GridPos Position;
        public readonly int Rotation;

        public TabletSpot(GridPos position, int rotation)
        {
            Position = position;
            Rotation = rotation;
        }
    }

    public sealed class SolverOptions
    {
        /// <summary>석판 배치 탐색에서 단계마다 남길 후보 수.</summary>
        public int BeamWidth { get; set; } = 400;

        /// <summary>정확한 배정까지 돌려볼 최종 후보 수.</summary>
        public int ExactCandidates { get; set; } = 100;

        /// <summary>
        /// 한 부모가 다음 빔에서 가져갈 수 있는 최대 자리 수. 0이면 제한하지 않는다(점수 순 상위 N).
        ///
        /// 실효 폭을 지키는 값이다 - 자세한 것은 <see cref="PlacementSolver"/>의 Select 주석.
        /// 실제로 쓰이는 몫은 이 값과 <c>BeamWidth / 부모 수</c> 중 큰 쪽이라, 부모가 적을 때
        /// 빔이 비지 않는다.
        /// </summary>
        public int ParentQuota { get; set; } = 8;

        /// <summary>조건 판정과 배정이 서로를 참조하므로 몇 번 되풀이해 수렴시킬지.</summary>
        public int FixpointIterations { get; set; } = 3;

        /// <summary>
        /// 이긴 배치를 자리 맞바꾸기로 다듬을 횟수. 0이면 다듬지 않는다.
        ///
        /// 패스마다 (아티팩트 x 칸) 번을 채점하고, 좋아지는 것이 없으면 그 자리에서 멈춘다.
        /// 대개 한두 패스에서 멈추므로 셋이면 넉넉하다.
        /// </summary>
        public int PolishPasses { get; set; } = 3;

        /// <summary>열쇠·종이 지정 배치의 추가 채점 상한. 실측 최적값이 아닌 탐색 예산이다.</summary>
        public int PriorityComboTrials { get; set; } = 2048;

        /// <summary>양옆 빈칸을 함께 확보하는 배정의 상한. 실측 최적값이 아닌 탐색 예산이다.</summary>
        public int EmptySideTrials { get; set; } = 192;

        /// <summary>
        /// 이 풀이가 이미 쓸모없어졌는지. 폴링이 풀이보다 빠르면 답이 나오기도 전에 그 답을 버릴
        /// 것이 정해지는데, 그런 계산을 끝까지 돌리면 CPU 와 할당을 고스란히 버린다.
        ///
        /// 중간에 멈춘 결과는 <b>쓰지 않는다</b>. 부르는 쪽이 통째로 버릴 때만 켜는 신호다.
        /// </summary>
        public CancellationToken Cancellation { get; set; }

        /// <summary>
        /// 조언(후보 추천·석판 합성)이 쓰는 탐색 강도. 후보마다 한 번씩 푸는 만큼 기본보다 가볍다.
        ///
        /// <b>두 조언이 반드시 같은 값을 써야 한다.</b> 둘은 <see cref="LayoutCache"/> 하나를
        /// 나눠 쓰는데 캐시 열쇠에 이 값들이 들어가므로, 한쪽만 달라지면 캐시가 조용히 갈라져
        /// 후보와 합성이 서로 다른 배치 위에서 겨루게 된다. 그래서 값을 여기 한 곳에만 둔다.
        /// </summary>
        public static SolverOptions ForAdvice(CancellationToken cancellation) =>
            new SolverOptions { BeamWidth = 150, ExactCandidates = 40, PriorityComboTrials = 192, EmptySideTrials = 48, Cancellation = cancellation };
    }

    public sealed class Arrangement
    {
        public int PriorityComboMatches { get; set; }
        public double PriorityComboProgress { get; set; }
        public List<int> UnmatchedComboCharms { get; } = new List<int>();

        public List<TabletPlacement> Tablets { get; } = new List<TabletPlacement>();

        /// <summary>석판 인스턴스 번호별 시뮬레이션 적용 여부.</summary>
        public Dictionary<int, bool> AppliedTablets { get; } = new Dictionary<int, bool>();

        /// <summary>석판 인스턴스 번호별 최종 자리와 회전.</summary>
        public Dictionary<int, TabletSpot> TabletPositions { get; } = new Dictionary<int, TabletSpot>();

        /// <summary>아티팩트 인스턴스 번호 → 배치된 칸.</summary>
        public Dictionary<int, GridPos> CharmPositions { get; } = new Dictionary<int, GridPos>();

        public double Score { get; set; }

        /// <summary>
        /// 배치들 사이에서 고를 때 쓰는 값. <see cref="Score"/>에 지금 자리를 지키는 몫(이사 비용의
        /// 반대 부호)과 직전 제안을 지키는 몫을 더한 것이다. 화면에 보이는 점수가 아니다 - 같은
        /// 판의 배치들끼리만 견줄 수 있고 절대값에는 뜻이 없다.
        /// </summary>
        public double Preference { get; set; }

        /// <summary>
        /// 놓을 자리가 모자라 배치에서 빠진 석판 수. 0이 아니면 점수가 실제 인벤토리를 다
        /// 반영하지 못한 것이므로, 이 배치를 자동 배치로 적용하면 안 된다.
        /// </summary>
        public int UnplacedTablets { get; set; }

        /// <summary>아티팩트가 놓인 칸의 최종 레벨. 화면 표시에 쓴다.</summary>
        public Dictionary<GridPos, int> Levels { get; } = new Dictionary<GridPos, int>();

        /// <summary>
        /// 열린 칸 전체의 레벨. 게임 levelMatrix 와의 대조용이다. 아티팩트가 놓인 칸만 대조하면
        /// 빈 칸에 걸린 효과(각인 등)의 어긋남을 놓쳐 "점수를 믿어도 되는가"의 신호가 절반이 된다.
        /// </summary>
        public Dictionary<GridPos, int> CellLevels { get; } = new Dictionary<GridPos, int>();

        /// <summary>시뮬레이션 결과 disable 행렬이 양수인 열린 칸.</summary>
        public HashSet<GridPos> DisabledCells { get; } = new HashSet<GridPos>();

        /// <summary>
        /// 그 칸의 아티팩트가 실제로 받는 레벨. 아티팩트마다 상한이 달라 칸의 레벨보다 낮을 수 있다.
        /// </summary>
        public Dictionary<GridPos, int> EffectiveLevels { get; } = new Dictionary<GridPos, int>();

        /// <summary>효과가 꺼진 아티팩트.</summary>
        public List<int> InactiveCharms { get; } = new List<int>();

        /// <summary>
        /// 제한 해제 칸에 고정했는데 그런 칸이 모자라 보통 칸에 앉은 아티팩트. 계획은 그대로 쓰고
        /// 화면이 알린다.
        /// </summary>
        public List<int> UnheldCharms { get; } = new List<int>();

        /// <summary>효과가 꺼진 아티팩트가 놓인 칸과 그 이유.</summary>
        public Dictionary<GridPos, CharmInactiveReason> InactiveCells { get; } =
            new Dictionary<GridPos, CharmInactiveReason>();
    }
}

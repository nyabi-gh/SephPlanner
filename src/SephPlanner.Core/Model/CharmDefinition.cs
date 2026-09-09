using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>아티팩트 정의. 게임 코드에서는 <c>Charm</c>이다.</summary>
    public sealed class CharmDefinition
    {
        public string Id { get; set; } = "";
        public int EntityId { get; set; }
        public Rarity Rarity { get; set; }

        /// <summary>효과에 반영되는 레벨 상한. 그 위로는 올려도 의미가 없다.</summary>
        public int MaxLevel { get; set; } = 5;

        /// <summary>게임의 CharmActivateCriteria 파생 타입 이름. 없으면 빈 문자열.</summary>
        public string CriteriaType { get; set; } = "";

        /// <summary>Charm_Magic 계열이면 참. 다른 아티팩트의 조건 판정에 쓰인다.</summary>
        public bool IsMagic { get; set; }

        public DirectedMagicSupport? MagicSupport { get; set; }
        public List<double> MagicCostByLevel { get; set; } = new List<double>();
        public List<ContextStatBonus> ContextStats { get; set; } = new List<ContextStatBonus>();
        public int PaperMatch { get; set; } = 2;

        /// <summary>참이면 특정 무기를 들어야 발동한다.</summary>
        public bool IsWeaponRelated { get; set; }

        /// <summary>연동된 무기 종류(<c>EWeaponType</c> 이름). 무기 연동이 아니면 빈 문자열.</summary>
        public string RelatedWeapon { get; set; } = "";

        /// <summary>
        /// 게임의 컴포넌트 클래스 이름. 하얀 종이처럼 배치에 따라 동작이 달라지는 특수 아티팩트를
        /// 솔버가 알아보는 데 쓴다.
        /// </summary>
        public string Behavior { get; set; } = "";

        /// <summary>
        /// 게임이 만들어 준 효과 설명. 게임 툴팁과 같은 문장이며 레벨별 값은 범위로 적혀 있다.
        /// 서식 태그는 덤프할 때 이미 걷어냈다. 만들지 못한 아티팩트는 비어 있다.
        /// </summary>
        public List<string> EffectLines { get; set; } = new List<string>();

        /// <summary>
        /// 조화의 수정 계열(<c>Charm_NearLevelDamage</c>)의 레벨별 배수. 이웃 8칸 아티팩트의
        /// 유효 레벨 합에 이 값을 곱한 만큼 전체 피해가 오른다. 자기 레벨로 색인한다.
        /// 다른 아티팩트에서는 비어 있다.
        /// </summary>
        public List<double> NeighborLevelBonus { get; set; } = new List<double>();

        /// <summary>
        /// 이 아티팩트가 강화할 대상을 찾는 칸의 오프셋. 게임 <c>Charm_UpCharmDamage</c>(북향의
        /// 침)의 <c>xOffset</c>/<c>yOffset</c>이며 기본은 바로 위 칸이다. 사람이 대상을 고르는
        /// 것이 아니라 <b>자리가 대상을 정한다</b> - 그 칸에 강화할 수 있는 아티팩트가 없으면
        /// 침은 아무 일도 하지 않는다. 해당 없는 아티팩트에서는 둘 다 0이다.
        /// </summary>
        public int DependencyOffsetX { get; set; }
        public int DependencyOffsetY { get; set; }

        /// <summary>침이 대상에게 주는 레벨별 피해 증가(<c>damageBonusByLevel</c>). 비어 있으면 침이 아니다.</summary>
        public List<double> DependencyBonusByLevel { get; set; } = new List<double>();

        /// <summary>
        /// 대상이 <see cref="DependencyMaxRarity"/> 이하일 때 얹히는 몫
        /// (<c>dependencyDamageBonusByLevel</c>). <see cref="HasDependencyCondition"/>이 참일 때만 쓴다.
        /// </summary>
        public List<double> DependencyExtraByLevel { get; set; } = new List<double>();

        public bool HasDependencyCondition { get; set; }
        public Rarity DependencyMaxRarity { get; set; }

        /// <summary>
        /// 공격하는 아티팩트(게임 <c>IAttackableCharm</c>). 북향의 침은 이런 아티팩트나 다른 침만
        /// 대상으로 삼는다.
        ///
        /// 마법은 담긴 기술의 공격 가능 여부까지 확인한다. 인스턴스에서 읽은 판정이 있으면
        /// 솔버는 그 값을 우선한다.
        /// </summary>
        public bool IsAttackable { get; set; }

        /// <summary>동료 아티팩트(게임 <c>ICompanionCharm</c>). 헌신의 휘장이 같은 행에서 찾는다.</summary>
        public bool IsCompanion { get; set; }

        /// <summary>
        /// 이웃 여덟 칸에서 강화할 아티팩트의 카테고리. 거대한 망원경
        /// (<c>Charm_PlanetModule</c>)이 이웃의 <c>PLANET</c> 행성을 강화하는 것이 유일한 예다.
        /// 카테고리 이름이 게임 코드에 글자로 박혀 있어 필드로 읽어 올 수 없고, 덤프가 동작
        /// 클래스를 보고 채운다. 해당 없으면 빈 문자열.
        /// </summary>
        public string NeighborEnhanceCategory { get; set; } = "";

        /// <summary>
        /// 놓인 행에 따라 갈아입는 카테고리. 게임 <c>Charm_3Elemental_ByRow.lineCategory</c>
        /// (캘세더니 열쇠)이고 <c>행 % 개수</c>로 고른다. 그래서 이 아티팩트는 어느 줄에 서느냐로
        /// 어떤 콤보를 미느냐가 갈린다. 다른 아티팩트에서는 비어 있다.
        /// </summary>
        public List<string> LineCategories { get; set; } = new List<string>();

        /// <summary>
        /// 게임이 매겨 둔 원가(<c>ItemEntity.cost</c>). 상점 표시가는 흥정 능력치로 조정되므로
        /// 이것과 다르다. 개발사가 아이템마다 직접 넣은 값이라, 능력치로 잴 수 없는 아티팩트의
        /// 값어치를 가늠할 후보다.
        /// </summary>
        public int Cost { get; set; }

        /// <summary>주머니 차원의 사파이어 해금가(<c>ItemEntity.sapphirePrice</c>). 역시 손으로 매긴 값이다.</summary>
        public int SapphirePrice { get; set; }

        /// <summary>
        /// 레벨별 값어치. 게임의 레벨별 능력치 표를 레벨 단위로 옮긴 것이며 색인이 곧 레벨이다.
        /// 능력치를 주지 않는 아티팩트에서는 비어 있고, 그때 점수는 레어도 어림값으로 물러선다.
        /// 계산은 <c>CharmStatWorth</c>가 하고 덤프가 결과만 실어 온다.
        /// </summary>
        public List<double> StatWorthByLevel { get; set; } = new List<double>();
        public List<double> StatBenefitByLevel { get; set; } = new List<double>();
        public List<double> StatPenaltyByLevel { get; set; } = new List<double>();

        /// <summary>
        /// 위 표를 얼마나 믿을 만한지(0~1). 그 아티팩트만 주는 능력치는 환산율이 자기 자신에서
        /// 나와 동어반복이 되므로, 그런 몫이 크면 값이 낮아진다.
        /// </summary>
        public double StatWorthConfidence { get; set; }

        /// <summary>환산 누락 여부까지 검사한 표인지. 구버전 카탈로그는 확인되지 않은 상태로 읽는다.</summary>
        public bool StatWorthCoverageKnown { get; set; }

        /// <summary>값어치로 환산하지 못한 능력치. 표본 신뢰도와 별개로 측정의 누락을 나타낸다.</summary>
        public List<string> StatWorthUnconverted { get; set; } = new List<string>();

        public List<string> Categories { get; set; } = new List<string>();
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }
}

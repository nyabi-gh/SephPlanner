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

        /// <summary>
        /// 위 표를 얼마나 믿을 만한지(0~1). 그 아티팩트만 주는 능력치는 환산율이 자기 자신에서
        /// 나와 동어반복이 되므로, 그런 몫이 크면 값이 낮아진다.
        /// </summary>
        public double StatWorthConfidence { get; set; }

        public List<string> Categories { get; set; } = new List<string>();
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }
}

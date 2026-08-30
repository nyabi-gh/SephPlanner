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

        public List<string> Categories { get; set; } = new List<string>();
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }
}

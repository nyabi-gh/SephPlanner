using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>아티팩트 정의. 게임 코드에서는 <c>Charm</c>이다.</summary>
    public sealed class CharmDefinition
    {
        public string Id { get; set; } = "";
        public int EntityId { get; set; }
        public Rarity Rarity { get; set; }

        /// <summary>아티팩트 자신이 요구하는 배치 조건 (예: 인벤토리 최상단/최하단).</summary>
        public List<string> ActivateCriteria { get; set; } = new List<string>();

        public List<string> Categories { get; set; } = new List<string>();
        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }
}

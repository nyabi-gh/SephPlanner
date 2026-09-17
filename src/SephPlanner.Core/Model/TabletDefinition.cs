using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    /// <summary>게임 <c>StoneTablet.EffectType</c>과 1:1 대응.</summary>
    public enum TabletEffectKind
    {
        None,
        IncreaseConstLevel,
        Disable,
        IgnoreCriteria,
        MultiplyConstLevel,
    }

    /// <summary>게임 <c>StoneTablet.CriteriaType</c>과 1:1 대응.</summary>
    public enum TabletCriteriaKind
    {
        None,
        AnyItem,
        OnlyCharm,
        Placed,
    }

    public enum Rarity
    {
        Common,
        Uncommon,
        Rare,
        Legend,
        Eternal,
    }

    /// <summary>
    /// 석판 한 종류의 정적 정의. 아이템은 한 칸만 차지하므로 모양 정보는 없고,
    /// 효과가 미치는 범위는 질의 문자열이 결정한다.
    /// </summary>
    public sealed class TabletDefinition
    {
        /// <summary>로컬라이제이션 키 <c>Item_StoneTablet_{Id}_Name</c>의 Id 부분.</summary>
        public string Id { get; set; } = "";
        public int EntityId { get; set; }
        public Rarity Rarity { get; set; }
        public bool IsRotatable { get; set; }

        /// <summary>게임의 버리기 금지 속성. 합성 창도 이 속성이 있는 재료를 받지 않는다.</summary>
        public bool CannotDiscard { get; set; }

        /// <summary>참이면 질의가 프리팹이 아니라 런 도중 인스턴스별로 정해진다.</summary>
        public bool IsCustom { get; set; }

        /// <summary>레벨 증감과 칸 비활성화를 지정하는 질의.</summary>
        public string Query { get; set; } = "";

        /// <summary>효과가 발동하기 위한 조건을 지정하는 질의.</summary>
        public string ConditionQuery { get; set; } = "";

        public Dictionary<string, string> Names { get; set; } = new Dictionary<string, string>();
    }
}

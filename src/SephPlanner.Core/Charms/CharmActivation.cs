using System;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Charms
{
    /// <summary>
    /// 아티팩트의 효과가 꺼진 이유. 게임의 <c>Charm_Basic.RefreshCharm</c>이 보는 조건들에 대응한다.
    /// </summary>
    public enum CharmInactiveReason
    {
        None,

        /// <summary>연동된 무기를 들고 있지 않다. 어디로 옮겨도 켜지지 않는다.</summary>
        Weapon,

        /// <summary>석판이 그 칸을 사용 불가로 만들었다.</summary>
        Disabled,

        /// <summary>레벨이 0 미만이다. 0은 켜져 있는 것으로 본다.</summary>
        NegativeLevel,

        /// <summary>아티팩트 자신의 배치 조건을 만족하지 못했다.</summary>
        Criteria,
    }

    public static class WeaponMatch
    {
        /// <summary>
        /// 무기 연동 아티팩트는 <c>relatedWeapon</c>과 들고 있는 무기가 같아야 효과가 켜진다.
        /// </summary>
        public static bool IsDormant(CharmDefinition definition, string equippedWeapon)
        {
            if (!definition.IsWeaponRelated) return false;

            // 무기를 모르는 채로 꺼 버리면 실제로는 켜져 있는 아티팩트를 전부 0점으로 보게 된다.
            // 런 밖이거나 플러그인이 무기를 읽지 못한 상황이므로 판정하지 않고 넘어간다.
            if (string.IsNullOrEmpty(equippedWeapon)) return false;

            // 연동 무기를 기록하기 전에 뜬 카탈로그면 이 값이 비어 있다. 그대로 비교하면 무기 연동
            // 아티팩트를 전부 꺼진 것으로 보게 되므로, 판정할 근거가 없을 때는 넘어간다.
            if (string.IsNullOrEmpty(definition.RelatedWeapon)) return false;

            return !string.Equals(definition.RelatedWeapon, equippedWeapon, StringComparison.Ordinal);
        }
    }
}

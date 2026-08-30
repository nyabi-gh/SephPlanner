using SephPlanner.Core.Charms;
using SephPlanner.Core.Model;

namespace SephPlanner.Tests;

/// <summary>
/// 무기 연동 아티팩트는 해당 무기를 들고 있어야 효과가 켜진다. 판정할 근거가 없을 때 꺼 버리면
/// 실제로는 켜져 있는 아티팩트를 전부 0점으로 보게 되므로, 그 경우들을 특히 못박아 둔다.
/// </summary>
public class WeaponMatchTests
{
    private static CharmDefinition WeaponCharm(string relatedWeapon) => new()
    {
        IsWeaponRelated = true,
        RelatedWeapon = relatedWeapon,
    };

    [Fact]
    public void CharmWithNoWeaponTieIsNeverDormant()
    {
        var plain = new CharmDefinition();

        Assert.False(WeaponMatch.IsDormant(plain, "GreatSword"));
    }

    [Fact]
    public void MatchingWeaponKeepsTheCharmOn()
    {
        Assert.False(WeaponMatch.IsDormant(WeaponCharm("GreatSword"), "GreatSword"));
    }

    [Fact]
    public void DifferentWeaponTurnsTheCharmOff()
    {
        Assert.True(WeaponMatch.IsDormant(WeaponCharm("GreatSword"), "Dagger"));
    }

    [Fact]
    public void UnknownEquippedWeaponIsNotJudged()
    {
        // 런 밖이거나 플러그인이 무기를 읽지 못한 상황이다.
        Assert.False(WeaponMatch.IsDormant(WeaponCharm("GreatSword"), ""));
    }

    [Fact]
    public void CatalogWithoutTheRelatedWeaponIsNotJudged()
    {
        // 연동 무기를 기록하기 전에 뜬 카탈로그다. 비교하면 전부 꺼진 것으로 보게 된다.
        Assert.False(WeaponMatch.IsDormant(WeaponCharm(""), "GreatSword"));
    }
}

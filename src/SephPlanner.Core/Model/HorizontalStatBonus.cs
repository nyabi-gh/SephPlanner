using System.Collections.Generic;

namespace SephPlanner.Core.Model
{
    public enum HorizontalSide { Automatic, Left, Right }

    public sealed class HorizontalStatBonus
    {
        // 게임 Charm_FireIce.AddStat과 Charm_FireIceWeapon.CheckPosition의 고정 경계다.
        public static bool IsLeft(GridPos position) => position.X <= 2;

        public string LeftStat { get; set; } = "";
        public string RightStat { get; set; } = "";
        public List<int> MainByLevel { get; set; } = new List<int>();
        public List<int> OppositeByLevel { get; set; } = new List<int>();
    }
}

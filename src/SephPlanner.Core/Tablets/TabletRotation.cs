namespace SephPlanner.Core.Tablets
{
    /// <summary>
    /// 게임의 회전 한 걸음은 <c>StoneTablet.rotation</c> 을 1 늘리고 4 에서 0 으로 돈다.
    ///
    /// 호스트는 각도를 그대로 쓰면 되지만 클라이언트에는 그 길이 없다 - 우클릭이 타는
    /// <c>DoClickAction</c> 으로 한 걸음씩 눌러야 하므로 몇 번인지를 세야 한다. 방향을 뒤집어
    /// 세면 누를 때마다 조금씩 어긋난 채로 굳으므로(같은 일을 하는 커뮤니티 도구가 겪은 드리프트),
    /// 게임과 같은 방향으로 센다는 것을 여기 한 곳에 두고 시험한다.
    /// </summary>
    public static class TabletRotation
    {
        /// <summary>한 바퀴가 몇 걸음인지.</summary>
        public const int Steps = 4;

        /// <summary>지금 각도에서 목표 각도까지 눌러야 하는 횟수(0~3).</summary>
        public static int PressesFrom(int current, int target) =>
            ((target - current) % Steps + Steps) % Steps;

        /// <summary>그만큼 눌렀다가 되돌리려면 몇 번 더 눌러야 하는지(0~3).</summary>
        public static int PressesBack(int pressed) =>
            (Steps - pressed % Steps) % Steps;
    }
}

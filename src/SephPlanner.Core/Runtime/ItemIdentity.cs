using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 아이템을 계획 안에서 가리키는 번호.
    ///
    /// 게임이 번호를 주지 않는 아이템이 있다 - 시나리오 동행 증표(<c>Item_ScenarioCompanion_*</c>)
    /// 가 그렇고 <c>instanceID</c> 가 0 이다. 그대로 두면 둘 이상일 때 서로를 덮어쓰고(자리·지문·
    /// 검증이 한 몸으로 본다) 빈 칸과도 구분되지 않는다.
    ///
    /// 그런 아이템은 <b>옮길 수 없으므로 칸이 곧 정체성</b>이다. 그래서 칸에서 만든 음수 번호를
    /// 준다 - 게임의 번호는 음수가 되지 않으므로 부딪히지 않고, 못 옮기니 이 번호도 흔들리지 않는다.
    ///
    /// <b>읽는 쪽 전부가 같은 규칙을 써야 한다.</b> 계획은 이 번호로 목표를 적고 적용기의 사전
    /// 검증은 게임에서 다시 읽으므로, 한쪽만 다르면 "계산 이후 인벤토리가 바뀌었다" 로 거절된다.
    /// </summary>
    public static class ItemIdentity
    {
        public static int Of(int instanceId, GridSpec grid, int x, int y) =>
            instanceId != 0 ? instanceId : -(grid.ToIndex(x, y) + 1);
    }
}

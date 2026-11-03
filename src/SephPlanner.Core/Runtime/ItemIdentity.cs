using SephPlanner.Core.Tablets;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 아이템을 계획 안에서 가리키는 번호.
    ///
    /// 게임의 <c>instanceID</c> 로 하나를 가리킬 수 없는 아이템이 있다.
    /// - 번호가 0 이다. 시나리오 동행 증표(<c>Item_ScenarioCompanion_*</c>)가 그렇다.
    /// - 다른 아이템과 번호가 겹친다. 해골로 새 판을 시작하면 시작 석판 저주가 같은 번호로 두 개
    ///   들어온다(제보 4dd465fb·5be39302). 게임의 <c>RestockStartingItem</c> 이 시작 아이템 목록을
    ///   원래 번호 그대로 다시 넣는다.
    ///
    /// 그대로 두면 둘 이상일 때 서로를 덮어쓰고(자리·지문·검증이 한 몸으로 본다), 번호 0 은 빈
    /// 칸과도 구분되지 않는다. 게임에 "이것을 저 칸으로" 라고 말할 길도 없다.
    ///
    /// 그런 아이템은 <b>옮길 수 없으므로 칸이 곧 정체성</b>이다. 그래서 칸에서 만든 음수 번호를
    /// 준다 - 게임의 번호는 음수가 되지 않으므로 부딪히지 않고, 못 옮기니 이 번호도 흔들리지 않는다.
    ///
    /// <b>읽는 쪽 전부가 같은 규칙을 써야 한다.</b> 계획은 이 번호로 목표를 적고 적용기의 사전
    /// 검증은 게임에서 다시 읽으므로, 한쪽만 다르면 "계산 이후 인벤토리가 바뀌었다" 로 거절된다.
    /// </summary>
    public static class ItemIdentity
    {
        /// <param name="shared">격자 안의 다른 아이템이 같은 번호를 쓴다.</param>
        public static int Of(int instanceId, GridSpec grid, int x, int y, bool shared) =>
            Addressable(instanceId, shared) ? instanceId : -(grid.ToIndex(x, y) + 1);

        /// <summary>게임의 번호로 이 아이템 하나를 가리킬 수 있다. 아니면 옮길 수 없다.</summary>
        public static bool Addressable(int instanceId, bool shared) => instanceId != 0 && !shared;
    }
}

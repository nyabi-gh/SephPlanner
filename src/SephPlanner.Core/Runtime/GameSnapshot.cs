using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    /// <summary>한 번의 폴링에서 읽은 게임 상태.</summary>
    public sealed class GameSnapshot
    {
        public string GameVersion { get; set; } = "";

        /// <summary>멀티 세션에서는 자동 배치를 잠근다.</summary>
        public bool IsMultiplayer { get; set; }

        public InventoryState? Inventory { get; set; }

        /// <summary>상점이나 보상 화면에 제시된 후보들. 추천 대상이다.</summary>
        public List<OfferedItem> Offers { get; set; } = new List<OfferedItem>();

        public RunState? Run { get; set; }

        /// <summary>
        /// 이 층의 석판 합성기. 없으면 null 이다.
        ///
        /// 거리를 보지 않는다. 합성기는 미니맵에 표시되는 고정물이라 이 층에 있다는 사실 자체가
        /// 이미 화면에 보이는 정보이고, 무엇을 합칠지는 합성기 앞에 서기 전에 정해 두는 편이
        /// 쓸모 있기 때문이다. 상자 속 내용물과 달리 숨은 정보가 아니다.
        /// </summary>
        public MixerState? Mixer { get; set; }

        /// <summary>
        /// 지금 인챈트를 걸 수 있는 기회. 없으면 null 이다.
        ///
        /// 제단과 인챈트 물약이 <b>같은 창</b>을 연다(<c>EInventoryMode.Enchant</c>). 제단은 층에
        /// 놓인 고정물이라 걸어가는 동안 미리 재어 둘 수 있고, 물약은 언제든 마실 수 있어 창이
        /// 열린 것 자체가 신호다. 그래서 둘을 한 자리에 담는다.
        /// </summary>
        public EnchantChanceState? EnchantChance { get; set; }
    }

    /// <summary>인챈트를 걸 기회. 게임이 사람마다 따로 세므로 남은 횟수도 내 몫이다.</summary>
    public sealed class EnchantChanceState
    {
        /// <summary>
        /// 이 층의 제단에서 내가 더 쓸 수 있는 횟수(<c>AltarOfEnchant.localRemaining</c>의 합).
        ///
        /// 게임은 접속할 때 서버에 한 번 묻고 답이 올 때까지 -1 을 들고 있다. 그 -1 을 0 으로
        /// 적으면 아직 못 쓴 제단이 다 쓴 것으로 보이므로, 읽는 쪽이 프리팹의 <c>localUseCount</c>
        /// 로 메운다.
        /// </summary>
        public int AltarUses { get; set; }

        /// <summary>
        /// 아티팩트 선택 창이 지금 열려 있는지. <b>인챈트 물약은 제단 없이도 이 창을 연다</b> -
        /// 이것이 없으면 물약을 마신 사람은 빈 화면을 본다.
        /// </summary>
        public bool Open { get; set; }

        /// <summary>
        /// 플레이어가 제단 가까이에 있는지. 합성기의 같은 이름과 같은 자리이고 이유도 같다 -
        /// <b>인챈트 조언은 공짜가 아니다.</b> 아티팩트 35개짜리 가방에서 재어 보니 377ms 로,
        /// 하나 빼기(272ms)보다 비싸고 합성(838ms)보다 싸다. 층에 제단이 있기만 하면 계속 돌면
        /// 합성기가 <c>Near</c> 를 갖기 전에 겪은 일을 그대로 되풀이한다.
        ///
        /// <c>null</c> 은 재지 않았다는 뜻이며 그때는 거리를 따지지 않는다 - 이 값이 없던 시절의
        /// 재현 자료가 그렇다.
        /// </summary>
        public bool? Near { get; set; }

        /// <summary>
        /// 지금 조언을 만들 이유가 있는가. 제단 몫이 남았고 걸어갈 만큼 가까우면 미리 재어 두고,
        /// 제단이 없거나 멀어도 창이 열렸으면 - 물약이 그렇다 - 그때 잰다.
        /// </summary>
        public bool Available => (AltarUses > 0 && Near != false) || Open;
    }

    /// <summary>석판 합성기의 상태. 한 사람이 층마다 한 번만 쓸 수 있다.</summary>
    public sealed class MixerState
    {
        /// <summary>합성 비용(골드).</summary>
        public int Cost { get; set; }

        /// <summary>내가 이미 이 층에서 썼는지. 썼으면 더 권할 것이 없다.</summary>
        public bool Used { get; set; }

        /// <summary>
        /// 플레이어가 이 합성기 가까이에 있는지. 합성 추천은 조언 한 번의 값을 세 배로 만드는
        /// 가장 비싼 계산인데, 층에 합성기가 있기만 하면 계속 돌고 있었다. 걸어가는 동안 준비될
        /// 만큼 넉넉한 거리에서만 돌린다.
        ///
        /// <c>null</c> 은 재지 않았다는 뜻이며 그때는 전처럼 돈다 - 이 값이 없던 시절의 F10
        /// 재현 자료가 그렇다. 거짓으로 두면 그 자료들이 합성 추천을 잃는다.
        /// </summary>
        public bool? Near { get; set; }
    }

    public sealed class RunState
    {
        /// <summary>장착한 무기 종류(<c>EWeaponType</c> 이름). 무기 연동 아티팩트의 발동 여부를 가른다.</summary>
        public string WeaponId { get; set; } = "";

        /// <summary>소지금. 살 수 없는 후보를 가려내는 데 쓴다.</summary>
        public int Gold { get; set; }
    }

    public sealed class InventoryState
    {
        public int Width { get; set; }
        public int Height { get; set; }

        /// <summary>실제로 열려 있는 칸 수. 질의 해석이 이 값에 의존한다.</summary>
        public int Storage { get; set; }

        public List<PlacedItem> Items { get; set; } = new List<PlacedItem>();
        public List<PlacedTablet> Tablets { get; set; } = new List<PlacedTablet>();

        /// <summary>
        /// 각인. 격자 위 고정된 자리에서 석판과 똑같이 효과를 내지만 칸을 차지하지 않고
        /// 플레이어가 옮길 수도 없다. 그래서 배치 대상이 아니라 주어진 조건이다.
        /// </summary>
        public List<PlacedTablet> Engravings { get; set; } = new List<PlacedTablet>();

        /// <summary>게임이 계산해 둔 셀별 최종 레벨. 키는 "x,y".</summary>
        public Dictionary<string, int> LevelMatrix { get; set; } = new Dictionary<string, int>();
        public List<string> DisabledCells { get; set; } = new List<string>();

        /// <summary>
        /// 게임이 세어 둔 카테고리별 콤보 개수(<c>currentSetEffectCount</c>). 유니크 페어 보정처럼
        /// 우리가 재현하지 않는 규칙이 있어, 세는 대신 게임 값을 그대로 쓴다.
        /// </summary>
        public Dictionary<string, int> ComboCounts { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// 고정 각인과 친타마니가 칸에 남긴 효과. <see cref="ComboEngraving"/> 이 있으면 콤보 각인
        /// 몫은 여기 없다 - 배치를 따라 생기고 사라지기 때문이다.
        /// </summary>
        public List<FixedEffectCell> FixedEffects { get; set; } = new List<FixedEffectCell>();

        /// <summary>
        /// 신비 콤보 각인의 규칙과 좌표. 0.4.18 전 자료에는 없고, 그때는 그 각인이 <see cref="FixedEffects"/>
        /// 에 섞여 있다.
        /// </summary>
        public ComboEngravingRule? ComboEngraving { get; set; }
    }

    public sealed class PlacedItem
    {
        public List<string>? ObservedCategories { get; set; }
        public bool? IsAttackable { get; set; }
        public int DefinitionId { get; set; }
        public int InstanceId { get; set; }
        public GridPos Position { get; set; }
        public int EffectiveLevel { get; set; }
        public bool IsActive { get; set; }

        /// <summary>인챈트로 붙은 고정 레벨. 자리를 옮겨도 따라다닌다.</summary>
        public int Enchant { get; set; }

        /// <summary>
        /// 옮길 수 없는 아이템. 게임이 인스턴스 번호를 주지 않은 것이 여기 해당한다 - 시나리오
        /// 동행 증표(<c>Item_ScenarioCompanion_*</c>)가 그렇고, 번호가 없으니 게임에 "이것을
        /// 저 칸으로" 라고 말할 길이 없다.
        ///
        /// 칸은 차지하고 석판 조건에도 아티팩트로 세므로 없는 셈 칠 수는 없다. 그래서 지금 칸에
        /// 못 박고 나머지를 그 주위로 푼다.
        /// </summary>
        public bool Immovable { get; set; }

        /// <summary>
        /// 성장 아티팩트가 목표까지 얼마나 왔는지. <c>null</c> 은 읽지 못했다는 뜻이며 0 과 다르다.
        ///
        /// 게임은 이 값을 서버에만 두고 소유자 화면에는 문자열로만 보낸다(<c>SetEffectHUDValue</c>).
        /// 그래서 참가자 세션에서는 읽을 길이 없어 <c>null</c> 로 남는다. 0 으로 적으면 아직
        /// 아무것도 못 채운 것과 구분되지 않으므로 그렇게 하지 않는다.
        ///
        /// <b>계획 지문에는 넣지 않는다</b>(<see cref="PlanFingerprint"/>). 이 값은 가드나 패링
        /// 한 번마다 올라가므로 지문에 넣으면 싸울 때마다 계획을 다시 풀게 된다. 아직 배치 계산이
        /// 쓰지 않는 값이라 넣을 이유도 없다. 쓰기 시작하면 그때 지문도 함께 본다.
        /// </summary>
        public int? GrowthProgress { get; set; }

        /// <summary>
        /// 성장이 끝나는 횟수. 0 이면 성장하지 않는 아티팩트다. 카탈로그에도 같은 값이 있지만,
        /// 계획 지문이 카탈로그를 보지 않고도 진행도를 칸으로 끊을 수 있어야 해서 여기에도 싣는다.
        /// </summary>
        public int GrowthGoal { get; set; }
    }

    public sealed class PlacedTablet
    {
        public int DefinitionId { get; set; }
        public int InstanceId { get; set; }
        public GridPos Position { get; set; }
        public int Rotation { get; set; }
        public bool IsApplied { get; set; }

        /// <summary>
        /// 게임이 적용해 둔 효과 범위(<c>EffectRange</c>)가 게임 자신의 질의 해석으로 지금 격자에서
        /// 나오는 범위와 다르다. 게임이 아직 다시 계산하지 않은 것이라 레벨 행렬도 낡았다 - 가방이
        /// 34칸에서 48칸으로 넓어진 뒤 새 칸에 석판 효과가 없던 제보 40f5874c 가 그랬다.
        /// </summary>
        public bool AppliedRangeStale { get; set; }

        /// <summary>
        /// 이 인스턴스를 지금 돌릴 수 있는지. 저주는 돌릴 수 있던 석판을 잠그고, 석판 합성은
        /// 반대로 정의상 돌릴 수 없는 합성 석판을 돌릴 수 있게 풀기 때문에 정의값과 별개다
        /// (<c>DungeonManager.IsTabletRotatable</c>). 비어 있으면 정의값을 쓴다 — 참을 기본값으로
        /// 두면 값을 싣지 않은 쪽이 돌릴 수 없는 석판을 돌릴 수 있다고 말하는 셈이 된다.
        /// </summary>
        public bool? IsRotatable { get; set; }

        /// <summary>커스텀 석판만 채워진다. 질의가 인스턴스마다 다르기 때문이다.</summary>
        public string? Query { get; set; }
        public string? ConditionQuery { get; set; }

        /// <summary>
        /// 플레이어가 석판 합성기에서 직접 붙인 이름. 합성 석판은 정의상 이름이 "..." 자리표시자라
        /// 이것이 없으면 화면에서 서로 구분되지 않는다. 붙인 이름이 없으면 비어 있다.
        /// </summary>
        public string? Name { get; set; }
    }

    public sealed class OfferedItem
    {
        public int DefinitionId { get; set; }
        public string Kind { get; set; } = "";

        /// <summary>실제 구매가. 상자나 바닥에 떨어진 것처럼 그냥 집으면 되는 것은 0이다.</summary>
        public int Price { get; set; }

        public int SlotIndex { get; set; }
    }
}

using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Ipc
{
    /// <summary>플러그인이 오버레이로 보내는 게임 상태 한 장면.</summary>
    public sealed class GameSnapshot
    {
        public int ProtocolVersion { get; set; } = IpcContract.ProtocolVersion;
        public string GameVersion { get; set; } = "";
        public long TimestampMs { get; set; }

        /// <summary>멀티 세션에서는 오버레이가 게임에 영향을 주는 기능을 전부 잠근다.</summary>
        public bool IsMultiplayer { get; set; }

        public InventoryState? Inventory { get; set; }

        /// <summary>상점이나 보상 화면에 제시된 후보들. 추천 대상이다.</summary>
        public List<OfferedItem> Offers { get; set; } = new List<OfferedItem>();

        public RunState? Run { get; set; }
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
        /// 고정 각인(신비 콤보 등)이 칸에 박아 둔 효과. 서버에만 있는 값이라 싱글(호스트)에서만
        /// 채워지고, 클라이언트로 접속한 세션에서는 비어 있다.
        /// </summary>
        public List<FixedEffectCell> FixedEffects { get; set; } = new List<FixedEffectCell>();
    }

    public sealed class PlacedItem
    {
        public int DefinitionId { get; set; }
        public int InstanceId { get; set; }
        public GridPos Position { get; set; }
        public int EffectiveLevel { get; set; }
        public bool IsActive { get; set; }

        /// <summary>인챈트로 붙은 고정 레벨. 자리를 옮겨도 따라다닌다.</summary>
        public int Enchant { get; set; }
    }

    public sealed class PlacedTablet
    {
        public int DefinitionId { get; set; }
        public int InstanceId { get; set; }
        public GridPos Position { get; set; }
        public int Rotation { get; set; }
        public bool IsApplied { get; set; }

        /// <summary>커스텀 석판만 채워진다. 질의가 인스턴스마다 다르기 때문이다.</summary>
        public string? Query { get; set; }
        public string? ConditionQuery { get; set; }
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

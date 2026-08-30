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
        public int Floor { get; set; }
        public string WeaponId { get; set; } = "";
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

        /// <summary>게임이 계산해 둔 셀별 최종 레벨. 키는 "x,y".</summary>
        public Dictionary<string, int> LevelMatrix { get; set; } = new Dictionary<string, int>();
        public List<string> DisabledCells { get; set; } = new List<string>();
    }

    public sealed class PlacedItem
    {
        public int DefinitionId { get; set; }
        public int InstanceId { get; set; }
        public GridPos Position { get; set; }
        public int EffectiveLevel { get; set; }
        public bool IsActive { get; set; }
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
        public int Price { get; set; }
        public int SlotIndex { get; set; }
    }
}

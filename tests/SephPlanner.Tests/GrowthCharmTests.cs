using System.Text.Json;
using SephPlanner.Core.Model;
using SephPlanner.Core.Planning;
using SephPlanner.Core.Runtime;

namespace SephPlanner.Tests;

public sealed class GrowthCharmTests
{
    [Fact]
    public void DefinitionCarriesTheGrowthGoalAndReward()
    {
        var definition = new CharmDefinition { EntityId = 1313, GrowthQuestGoal = 50, GrowthRewardEntityId = 1314 };
        Assert.Equal(50, definition.GrowthQuestGoal);
        Assert.Equal(1314, definition.GrowthRewardEntityId);

        // 성장하지 않는 아티팩트는 목표가 0 이다. 기본값이 그래야 카탈로그 전체가 조용히 성장형이 되지 않는다.
        var plain = new CharmDefinition { EntityId = 1000 };
        Assert.Equal(0, plain.GrowthQuestGoal);
        Assert.Equal(0, plain.GrowthRewardEntityId);
    }

    [Fact]
    public void UnknownProgressIsNotZero()
    {
        var unknown = new PlacedItem { DefinitionId = 1313, InstanceId = 7 };
        var fresh = new PlacedItem { DefinitionId = 1313, InstanceId = 8, GrowthProgress = 0 };
        Assert.Null(unknown.GrowthProgress);
        Assert.Equal(0, fresh.GrowthProgress);
        Assert.NotEqual(unknown.GrowthProgress, fresh.GrowthProgress);
    }

    [Fact]
    public void ProgressSurvivesTheSnapshotRoundTrip()
    {
        var item = new PlacedItem { DefinitionId = 1313, InstanceId = 7, GrowthProgress = 49 };
        var json = JsonSerializer.Serialize(item);
        var back = JsonSerializer.Deserialize<PlacedItem>(json)!;
        Assert.Equal(49, back.GrowthProgress);
    }

    [Fact]
    public void SnapshotsWrittenBeforeThisFieldExistedReadBackAsUnknown()
    {
        // 옛 F10 자료에는 이 항목이 없다. 없는 것을 0 으로 읽으면 "아직 아무것도 못 채웠다"가 되어
        // 실제로는 읽지 못한 것을 읽은 것처럼 만든다.
        var back = JsonSerializer.Deserialize<PlacedItem>(
            "{\"DefinitionId\":1313,\"InstanceId\":7,\"Enchant\":2}")!;
        Assert.Equal(1313, back.DefinitionId);
        Assert.Equal(2, back.Enchant);
        Assert.Null(back.GrowthProgress);
    }

    [Fact]
    public void ProgressStaysOutOfThePlanFingerprint()
    {
        // 가드 한 번마다 올라가는 값이라 지문에 들어가면 싸우는 내내 계획을 다시 푼다.
        static GameSnapshot Snapshot(int? progress) => new GameSnapshot
        {
            Inventory = new InventoryState
            {
                Width = 3,
                Height = 3,
                Storage = 9,
                Items = { new PlacedItem { DefinitionId = 1313, InstanceId = 7, Position = new GridPos(0, 0), GrowthProgress = progress } },
            },
        };

        var none = PlanFingerprint.Placement(Snapshot(null), PlanPreferences.None, "catalog");
        var started = PlanFingerprint.Placement(Snapshot(0), PlanPreferences.None, "catalog");
        var nearly = PlanFingerprint.Placement(Snapshot(49), PlanPreferences.None, "catalog");
        Assert.Equal(none, started);
        Assert.Equal(none, nearly);
    }
}

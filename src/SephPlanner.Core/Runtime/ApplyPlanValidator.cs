using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    public sealed class LivePlanItem
    {
        public int InstanceId { get; set; }
        public GridPos Position { get; set; }
        public bool IsTablet { get; set; }
        public int Rotation { get; set; }
        public bool CanRotate { get; set; }
    }

    public static class ApplyPlanValidator
    {
        public static string? Validate(
            ApplyPlanCommand command, IReadOnlyCollection<LivePlanItem> liveItems,
            int width, int height, int storage,
            string? livePlacementFingerprint = null, string? weaponId = null)
        {
            if (command.Targets is null || command.Targets.Count == 0)
                return "적용할 배치가 없습니다.";
            if (command.ExpectedWidth != width || command.ExpectedHeight != height ||
                command.ExpectedStorage != storage)
                return Changed();
            if (livePlacementFingerprint is not null &&
                (command.ExpectedPlacementFingerprint.Length == 0 ||
                 command.ExpectedPlacementFingerprint != livePlacementFingerprint))
                return Changed();
            if (weaponId is not null && command.ExpectedWeaponId != weaponId)
                return Changed();

            var liveById = new Dictionary<int, LivePlanItem>();
            foreach (var item in liveItems)
            {
                if (!liveById.TryAdd(item.InstanceId, item))
                    return "인벤토리 상태를 고유하게 식별할 수 없어 자동 배치를 중단합니다.";
            }

            if (liveById.Count != command.Targets.Count)
                return Changed();

            var targetIds = new HashSet<int>();
            var destinations = new HashSet<GridPos>();
            foreach (var target in command.Targets)
            {
                if (!targetIds.Add(target.InstanceId))
                    return "같은 아이템이 자동 배치 계획에 두 번 들어 있습니다.";
                if (!destinations.Add(target.To))
                    return $"목표 칸 {target.To}이 겹쳐 자동 배치를 중단합니다.";
                if (!IsOnGrid(target.To, width, height, storage))
                    return $"목표 칸 {target.To}이 격자 밖이라 자동 배치를 중단합니다.";
                if (!liveById.TryGetValue(target.InstanceId, out var live))
                    return Changed();
                if (live.Position != target.From || live.IsTablet != target.IsTablet)
                    return Changed();
                if (!target.IsTablet) continue;
                if (live.Rotation != target.FromRotation)
                    return Changed();
                if (target.Rotation < 0 || target.Rotation > 3)
                    return "석판 회전값이 올바르지 않아 자동 배치를 중단합니다.";
                if (live.Rotation != target.Rotation && !live.CanRotate)
                    return $"회전할 수 없는 석판(인스턴스 {target.InstanceId})이 있어 자동 배치를 중단합니다.";
            }

            return null;
        }

        private static string Changed() =>
            "배치 계산 이후 인벤토리가 바뀌어 자동 배치를 중단합니다. 잠시 뒤 다시 시도하세요.";

        private static bool IsOnGrid(GridPos position, int width, int height, int storage) =>
            position.X >= 0 && position.X < width &&
            position.Y >= 0 && position.Y < height &&
            position.Y * width + position.X < storage;
    }
}

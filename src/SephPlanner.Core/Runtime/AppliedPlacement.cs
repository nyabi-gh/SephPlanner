using System.Collections.Generic;
using SephPlanner.Core.Planning;

namespace SephPlanner.Core.Runtime
{
    /// <summary>
    /// 자동 배치가 끝난 뒤 들어온 판이 <b>그 계획 그대로인지</b> 가른다.
    ///
    /// 맞으면 다시 풀 것이 없다. 그 배치는 방금 솔버가 이 문제의 답으로 고른 것이고 문제가
    /// 그대로이므로 답도 그대로다. F8 직후는 석판 회전이 바뀌어 빔 캐시 열쇠까지 달라지는 탓에
    /// 세션에서 가장 비싼 재계산이 돌던 자리다 - 제보 <c>4c1efa35</c> 의 판에서 cold 1.07초(.NET),
    /// Mono 는 초 단위다. 게다가 그 답은 이미 알고 있는 것이라 순전히 버리는 시간이다.
    ///
    /// <b>가르는 법.</b> 적용 전 판을 계획대로 옮겨 놓은 판을 지어, 그 배치 지문을 실제로 들어온
    /// 판의 지문과 견준다. 지문은 배치에 영향을 주는 것을 모두 담으므로 하나라도 어긋나면 평소대로
    /// 푼다 - 이 길의 실패는 느려질 뿐 틀리지 않는다.
    ///
    /// <b>게임이 다시 계산하는 값은 견주지 않는다.</b> 칸 레벨·활성 여부·비활성 칸·석판 적용
    /// 여부는 배치 문제의 <i>입력</i>이 아니라 게임이 배치를 보고 내놓는 <i>결과</i>다. 그것까지
    /// 맞히려 들면 우리 예측이 한 칸만 달라도 이 길이 영영 안 서므로, 새 판의 것을 그대로 실어
    /// 견주기에서 뺀다. 그 값들이 우리 모델과 맞는지는 계획의 검증이 따로 보고, 어긋나면 자동
    /// 배치가 잠긴다.
    /// </summary>
    public static class AppliedPlacement
    {
        /// <summary>
        /// <paramref name="after"/> 가 <paramref name="applied"/> 를 <paramref name="before"/> 에
        /// 적용한 결과 그대로인가.
        /// </summary>
        public static bool Settled(
            Plan applied, GameSnapshot before, GameSnapshot after,
            string placementFingerprint, string contextFingerprint)
        {
            if (applied is null || before is null || after is null) return false;

            // 설정이나 카탈로그가 달라졌으면 값어치 표와 배치 조건이 함께 달라진다. 자리가 그대로여도
            // 같은 문제가 아니다. 이것은 지문 비교로는 걸리지 않는다 - 양쪽에 같은 맥락을 쓰기 때문이다.
            if (applied.PlanningContextFingerprint != contextFingerprint) return false;

            var projected = Project(applied, before, after);
            return projected is not null &&
                   PlanFingerprint.Placement(projected, contextFingerprint) == placementFingerprint;
        }

        /// <summary>
        /// 적용이 끝났으면 이렇게 되어 있어야 하는 판. <b>지문을 내는 데에만 쓴다</b> - 지문이
        /// 읽는 것만 채우므로 그 밖의 값은 비어 있다.
        /// </summary>
        private static GameSnapshot? Project(Plan applied, GameSnapshot before, GameSnapshot after)
        {
            var source = before.Inventory;
            var actual = after.Inventory;
            if (source is null || actual is null || applied.Targets.Count == 0) return null;

            var itemTargets = new Dictionary<int, PlanTarget>();
            var tabletTargets = new Dictionary<int, PlanTarget>();
            foreach (var target in applied.Targets)
            {
                if (target.IsTablet) tabletTargets[target.InstanceId] = target;
                else itemTargets[target.InstanceId] = target;
            }

            var reported = new Dictionary<int, PlacedItem>();
            foreach (var item in actual.Items) reported[item.InstanceId] = item;
            var reportedTablets = new Dictionary<int, PlacedTablet>();
            foreach (var tablet in actual.Tablets) reportedTablets[tablet.InstanceId] = tablet;

            var inventory = new InventoryState
            {
                Width = source.Width,
                Height = source.Height,
                Storage = source.Storage,

                // 배치 문제의 입력이다. 달라졌으면 지문이 달라져 평소대로 푼다.
                ComboCounts = source.ComboCounts,
                FixedEffects = source.FixedEffects,
                Engravings = source.Engravings,

                // 게임이 배치를 보고 다시 계산하는 값이다.
                LevelMatrix = actual.LevelMatrix,
                DisabledCells = actual.DisabledCells,
            };

            foreach (var item in source.Items)
            {
                if (!itemTargets.TryGetValue(item.InstanceId, out var target)) return null;
                if (!reported.TryGetValue(item.InstanceId, out var now)) return null;

                inventory.Items.Add(new PlacedItem
                {
                    DefinitionId = item.DefinitionId,
                    InstanceId = item.InstanceId,
                    Position = target.To,
                    Enchant = item.Enchant,
                    GrowthGoal = item.GrowthGoal,
                    GrowthProgress = item.GrowthProgress,
                    IsAttackable = item.IsAttackable,
                    ObservedCategories = item.ObservedCategories,
                    EffectiveLevel = now.EffectiveLevel,
                    IsActive = now.IsActive,
                });
            }

            foreach (var tablet in source.Tablets)
            {
                if (!tabletTargets.TryGetValue(tablet.InstanceId, out var target)) return null;
                if (!reportedTablets.TryGetValue(tablet.InstanceId, out var now)) return null;

                inventory.Tablets.Add(new PlacedTablet
                {
                    DefinitionId = tablet.DefinitionId,
                    InstanceId = tablet.InstanceId,
                    Position = target.To,
                    Rotation = target.Rotation,
                    IsRotatable = tablet.IsRotatable,
                    Query = tablet.Query,
                    ConditionQuery = tablet.ConditionQuery,
                    Name = tablet.Name,
                    IsApplied = now.IsApplied,
                });
            }

            return new GameSnapshot
            {
                Inventory = inventory,
                Run = before.Run,
                GameVersion = before.GameVersion,
            };
        }
    }
}

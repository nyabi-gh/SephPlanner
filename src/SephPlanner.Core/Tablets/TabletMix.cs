using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Tablets
{
    /// <summary>합성 재료 하나. 정의값이 아니라 인스턴스에 붙은 값이 답이다.</summary>
    public readonly struct MixMaterial
    {
        public readonly int InstanceId;
        public readonly int EntityId;
        public readonly string Query;
        public readonly string ConditionQuery;
        public readonly bool Rotatable;

        /// <summary>
        /// 가방에 놓인 회전. 합성 창은 돌릴 수 있는 석판을 이 각도로 받아오므로, 사람이 창에서
        /// 몇 번 돌려야 하는지의 출발점이다(<c>UI_CharacterStatusPanel</c> → <c>AddItemToMix</c>).
        /// </summary>
        public readonly int CurrentRotation;

        public MixMaterial(
            int instanceId, int entityId, string query, string conditionQuery, bool rotatable, int currentRotation = 0)
        {
            InstanceId = instanceId;
            EntityId = entityId;
            Query = query ?? "";
            ConditionQuery = conditionQuery ?? "";
            Rotatable = rotatable;
            CurrentRotation = ((currentRotation % 4) + 4) % 4;
        }
    }

    public sealed class MixResult
    {
        public string Query { get; set; } = "";
        public string ConditionQuery { get; set; } = "";
        public bool Rotatable { get; set; }

        /// <summary>합성할 때 재료가 놓여 있어야 하는 회전. 사람이 따라 해야 하므로 남긴다.</summary>
        public int RotationA { get; set; }
        public int RotationB { get; set; }
    }

    /// <summary>
    /// 석판 합성기(게임 안 이름 "석판 합성기")의 규칙. 게임의 <c>GridInventory.ServerMixTablet</c>
    /// 과 <c>CanMixTablet</c>을 그대로 옮긴 것이다.
    ///
    /// 결과는 엔티티 2101 짜리 새 석판 하나이고, 재료 둘은 사라진다. 질의는 재료의 질의를 합성
    /// 시점의 회전으로 구워 이어 붙인 것이라, "무엇과 무엇을" 만이 아니라 "어느 회전으로"까지가
    /// 선택이다.
    /// </summary>
    public static class TabletMix
    {
        /// <summary>합성 결과 석판의 엔티티 번호. 이 석판은 다시 합성할 수 없다.</summary>
        public const int ResultEntityId = 2101;

        /// <summary>
        /// 두 재료를 주어진 회전으로 합쳤을 때의 결과. 합칠 수 없으면 null.
        ///
        /// 조건 질의는 <b>양쪽이 다 있을 때만</b> 같아야 한다. 한쪽이 비어 있으면 있는 쪽이
        /// 그대로 결과의 조건이 된다(게임의 <c>DIFFERENT_CONDITION_QUERY</c> 판정과 같다).
        /// </summary>
        public static MixResult? Of(MixMaterial a, int rotationA, MixMaterial b, int rotationB)
        {
            if (a.InstanceId == b.InstanceId) return null;

            // 합성으로 만든 석판은 재료가 되지 못한다. 게임 CanMixTablet 이 2101 을 걸러낸다.
            if (a.EntityId == ResultEntityId || b.EntityId == ResultEntityId) return null;

            if (!a.Rotatable && rotationA != 0) return null;
            if (!b.Rotatable && rotationB != 0) return null;

            var conditionA = TabletQuery.Rotated(a.ConditionQuery, rotationA);
            var conditionB = TabletQuery.Rotated(b.ConditionQuery, rotationB);

            var bothHaveCondition = conditionA.Length > 0 && conditionB.Length > 0;
            if (bothHaveCondition && conditionA != conditionB) return null;

            return new MixResult
            {
                Query = Join(TabletQuery.Rotated(a.Query, rotationA), TabletQuery.Rotated(b.Query, rotationB)),
                ConditionQuery = conditionA.Length > 0 ? conditionA : conditionB,
                Rotatable = a.Rotatable && b.Rotatable,
                RotationA = rotationA,
                RotationB = rotationB,
            };
        }

        /// <summary>
        /// 합성 창에서 재료가 놓일 수 있는 회전. 돌릴 수 없는 석판은 가방에서 어떻게 놓였든 창의
        /// 아이콘이 0 으로 되돌린다(<c>UI_ItemIcon.UpdateIcon</c>).
        ///
        /// 둘 다 돌릴 수 있으면 결과도 돌릴 수 있으므로, 절대 회전이 아니라 <b>둘 사이의 각도</b>
        /// 만 결과를 가른다. 그래서 한쪽을 0 으로 고정해도 나올 수 있는 모양은 다 나온다.
        /// </summary>
        public static IEnumerable<int> Rotations(MixMaterial material)
        {
            if (!material.Rotatable)
            {
                yield return 0;
                yield break;
            }
            for (var rotation = 0; rotation < 4; rotation++) yield return rotation;
        }

        private static string Join(string left, string right)
        {
            if (left.Length == 0) return right;
            if (right.Length == 0) return left;
            return left + "\n" + right;
        }

        /// <summary>
        /// 합성 결과를 정의 없이 다룰 수 있게 껍데기 정의를 만든다. 게임에서도 엔티티 2101 의
        /// 정의는 껍데기이고 질의·회전·이름이 전부 인스턴스에 붙는다.
        /// </summary>
        public static TabletDefinition Definition(TabletDefinition? catalogued) =>
            catalogued ?? new TabletDefinition { EntityId = ResultEntityId, Id = "MixedTablet" };
    }
}

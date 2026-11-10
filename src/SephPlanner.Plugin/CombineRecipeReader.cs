using System.Collections.Generic;
using System.Reflection;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 붙여 두면 게임이 합쳐 버리는 배치 보너스 레시피(<c>ArrangementBonusEntity.IsCombine</c>)의 재료.
    /// 합치기는 1.1.2 에 생겼고 1.0.33 의 <c>ArrangementBonusEntity</c> 에는 <c>combine</c> 필드가 없어 이름으로 읽는다.
    /// </summary>
    internal static class CombineRecipeReader
    {
        private static readonly FieldInfo Combine =
            typeof(ArrangementBonusEntity).GetField("combine", BindingFlags.Instance | BindingFlags.Public);

        private static readonly FieldInfo Enabled =
            typeof(ArrangementBonusEntity).GetField("isEnabled", BindingFlags.Instance | BindingFlags.Public);

        private static List<List<int>> _recipes;

        public static List<List<int>> Read()
        {
            if (_recipes != null) return Copy(_recipes);
            if (Combine == null) return _recipes = new List<List<int>>();

            var recipes = new List<List<int>>();
            var seen = false;
            foreach (var bonus in ItemDatabase.GetAllArrangementBonuses())
            {
                seen = true;
                if (bonus == null || bonus.arrangements == null || bonus.arrangements.Length == 0) continue;
                if (Enabled != null && !(Enabled.GetValue(bonus) is bool enabled && enabled)) continue;
                var combine = Combine.GetValue(bonus);
                var result = combine?.GetType().GetField("resultItem", BindingFlags.Instance | BindingFlags.Public)?.GetValue(combine);
                if (!(result is UnityEngine.Object resultItem) || resultItem == null) continue;

                var materials = new List<int>();
                foreach (var arrangement in bonus.arrangements)
                {
                    if (arrangement == null || arrangement.item == null) { materials = null; break; }
                    materials.Add(arrangement.item.id);
                }
                // 게임 IsValid 처럼 재료 하나라도 비면 레시피 전체가 빠진다.
                if (materials != null) recipes.Add(materials);
            }
            // 데이터베이스가 서기 전이면 빈 목록이 오므로 그때는 기억하지 않는다.
            if (seen) _recipes = recipes;
            return Copy(recipes);
        }

        private static List<List<int>> Copy(List<List<int>> recipes)
        {
            var copy = new List<List<int>>(recipes.Count);
            foreach (var recipe in recipes) copy.Add(new List<int>(recipe));
            return copy;
        }
    }
}

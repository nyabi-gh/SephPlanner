using System.Collections.Generic;

namespace SephPlanner.Plugin
{
    /// <summary>
    /// 붙여 두면 게임이 합쳐 버리는 배치 보너스 레시피(<c>ArrangementBonusEntity.IsCombine</c>)의 재료.
    /// </summary>
    internal static class CombineRecipeReader
    {
        private static List<List<int>> _recipes;

        public static List<List<int>> Read()
        {
            if (_recipes != null) return Copy(_recipes);

            var recipes = new List<List<int>>();
            var seen = false;
            foreach (var bonus in ItemDatabase.GetAllArrangementBonuses())
            {
                seen = true;
                if (bonus == null || bonus.arrangements == null || bonus.arrangements.Length == 0) continue;
                if (!bonus.isEnabled || bonus.combine == null || bonus.combine.resultItem == null) continue;

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

        /// <summary>카탈로그와 함께 버린다. F9 처럼 정의가 바뀌었을 수 있을 때 카탈로그만 새로 지으면 레시피는 옛것이 남는다.</summary>
        public static void Invalidate() => _recipes = null;

        private static List<List<int>> Copy(List<List<int>> recipes)
        {
            var copy = new List<List<int>>(recipes.Count);
            foreach (var recipe in recipes) copy.Add(new List<int>(recipe));
            return copy;
        }
    }
}

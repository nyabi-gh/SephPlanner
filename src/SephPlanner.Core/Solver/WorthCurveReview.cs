using System;
using System.Collections.Generic;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 레벨이 올라가는데 값어치가 내려가는 아티팩트를 찾는다.
    ///
    /// <b>왜 찾는가.</b> 솔버는 값어치가 가장 큰 배치를 고르므로, 값어치가 레벨에 대해 내려가는
    /// 구간이 있으면 <b>낮은 레벨 칸이 정답이 된다.</b> 모델 안에서는 맞는 선택인데 사용자에게는
    /// "왜 이걸 낮은 자리에 박아 두느냐"로 보인다 - 토끼마을 경비병 투구가 그렇게 제보로 왔다
    /// (회피 -400 이 레벨 2에서 -800 이 되는데 피해는 선형으로 오른다). 비단조 자체를 금지하지는
    /// 않는다(docs/PLACEMENT-OBJECTIVE.md 의 "탐색과 최적성"). 대신 그런 아티팩트가 몇 종이고
    /// 무엇이 원인인지 미리 알고 있으려는 것이다 - 같은 제보가 올 자리의 목록이다.
    ///
    /// 값어치가 아니라 <b>게임이 준 능력치 표</b>가 원인이므로, 고칠 곳은 솔버가 아니라 환산율이나
    /// <c>data/values/charms.json</c> 이다.
    /// </summary>
    public static class WorthCurveReview
    {
        /// <summary>레벨 한 칸을 올렸을 때 값어치가 내려간 자리.</summary>
        public sealed class Drop
        {
            public int FromLevel { get; set; }
            public int ToLevel { get; set; }

            /// <summary>내려간 크기. 음수다.</summary>
            public double Delta { get; set; }

            /// <summary>가장 크게 깎은 능력치. 환산율이 없는 능력치뿐이면 빈 문자열이다.</summary>
            public string Cause { get; set; } = "";

            /// <summary>그 능력치의 원본 수치 변화. 무엇을 디컴파일로 확인할지 가리킨다.</summary>
            public int CauseFromAmount { get; set; }
            public int CauseToAmount { get; set; }
        }

        /// <summary>
        /// <paramref name="definition"/>의 값어치 곡선에서 내려가는 구간을 모은다. 상한까지만 본다 -
        /// 상한 위의 레벨은 효과에 반영되지 않으므로 배치 선택에 영향이 없다.
        /// </summary>
        public static List<Drop> Of(CharmDefinition definition, CharmValueEntry? curated = null)
        {
            var drops = new List<Drop>();
            var worth = CharmWorth.Resolve(definition, curated);
            var top = Math.Max(0, definition.MaxLevel);

            for (var level = 0; level < top; level++)
            {
                var delta = worth.At(level + 1) - worth.At(level);
                if (delta >= 0) continue;

                var drop = new Drop { FromLevel = level, ToLevel = level + 1, Delta = delta };
                Blame(definition, level, drop);
                drops.Add(drop);
            }
            return drops;
        }

        private static void Blame(CharmDefinition definition, int level, Drop drop)
        {
            var worst = 0.0;
            foreach (var effect in definition.StatEffects)
            {
                if (effect.WorthPerUnit is null) continue;

                var from = AmountAt(effect, level);
                var to = AmountAt(effect, level + 1);
                var change = (to - from) * effect.WorthPerUnit.Value;
                if (change >= worst) continue;

                worst = change;
                drop.Cause = effect.StatusId;
                drop.CauseFromAmount = from;
                drop.CauseToAmount = to;
            }
        }

        private static int AmountAt(CharmStatEffect effect, int level)
        {
            if (effect.AmountByLevel.Count == 0) return 0;
            return effect.AmountByLevel[Math.Min(Math.Max(0, level), effect.AmountByLevel.Count - 1)];
        }
    }
}

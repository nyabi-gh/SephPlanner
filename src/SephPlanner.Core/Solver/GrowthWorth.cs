using System;
using System.Collections.Generic;

namespace SephPlanner.Core.Solver
{
    /// <summary>
    /// 성장 아티팩트가 강화형에 가까워진 만큼 값어치를 끌어올린다.
    ///
    /// 조건을 다 채운 빛바랜 방패 문장은 사실상 철벽의 문장이다(레벨 0에서 1.54 대 4.62). 그런데
    /// 진행도를 보지 않으면 49/50과 0/50이 같은 값이 되어, 거의 다 키운 것을 빼라고 말하게 된다.
    ///
    /// <b>진행도를 그대로 쓰지 않고 <see cref="Steps"/>칸으로 끊는다.</b> 이 값은 가드나 패링 한
    /// 번마다 올라가므로, 그대로 점수에 넣으면 싸우는 내내 배치가 다시 풀린다. 우리 값어치의
    /// 정밀도가 1/50을 가릴 만큼도 아니다. 계획 지문도 같은 칸을 본다.
    /// </summary>
    public static class GrowthWorth
    {
        /// <summary>진행도를 몇 칸으로 끊을지. 목표가 50이면 열 번에 한 칸이다.</summary>
        public const int Steps = 5;

        /// <summary>
        /// 진행도를 칸으로 옮긴다. 모르면 0이다 - 참가자 자리에서 아직 표시값이 오지 않은 것을
        /// 성장한 것처럼 쳐 주지 않는다.
        /// </summary>
        public static int Bucket(int? progress, int goal)
        {
            if (progress is null || goal <= 0) return 0;

            var counted = progress.Value;
            if (counted <= 0) return 0;
            if (counted >= goal) return Steps;
            return Math.Min(Steps, counted * Steps / goal);
        }

        /// <summary>
        /// 지금 값어치와 바뀔 대상의 값어치를 진행도만큼 섞는다.
        ///
        /// 낮아지지는 않는다. 대상이 더 싸게 매겨진 경우에도 아직 이 아이템인 것은 사실이므로,
        /// 성장한다는 이유로 지금 값어치를 깎지 않는다.
        /// </summary>
        public static CharmWorth Project(CharmWorth current, CharmWorth reward, int bucket, int maxLevel)
        {
            if (bucket <= 0 || reward is null) return current;

            var share = Math.Min(bucket, Steps) / (double)Steps;
            var top = Math.Max(0, maxLevel);
            var projected = new CharmWorth
            {
                Base = current.Base,
                PerLevel = current.PerLevel,
                ByLevel = Mix(current, reward, share, top, (worth, level) => worth.At(level)),
                ByLevelIsFloor = current.ByLevelIsFloor,

                // 지어낸 몫이 섞였으므로 근거는 둘 중 약한 쪽을 따른다.
                Source = Weaker(current.Source, reward.Source),
                Confidence = Math.Min(current.Confidence, reward.Confidence),
            };

            if (current.BenefitByLevel != null && reward.BenefitByLevel != null)
                projected.BenefitByLevel = Mix(current, reward, share, top,
                    (worth, level) => Table(worth.BenefitByLevel, level));
            if (current.PenaltyByLevel != null && reward.PenaltyByLevel != null)
                projected.PenaltyByLevel = Mix(current, reward, share, top,
                    (worth, level) => Table(worth.PenaltyByLevel, level));
            return projected;
        }

        private static IReadOnlyList<double> Mix(
            CharmWorth current, CharmWorth reward, double share, int top, Func<CharmWorth, int, double> read)
        {
            var values = new double[top + 1];
            for (var level = 0; level <= top; level++)
            {
                var now = read(current, level);
                var gain = read(reward, level) - now;
                values[level] = gain > 0 ? now + gain * share : now;
            }
            return values;
        }

        private static double Table(IReadOnlyList<double>? table, int level)
        {
            if (table is null || table.Count == 0) return 0;
            return table[Math.Min(Math.Max(level, 0), table.Count - 1)];
        }

        private static CharmWorthSource Weaker(CharmWorthSource left, CharmWorthSource right) =>
            Rank(left) <= Rank(right) ? left : right;

        private static int Rank(CharmWorthSource source) => source switch
        {
            CharmWorthSource.Rarity => 0,
            CharmWorthSource.MeasuredFloor => 1,
            CharmWorthSource.Measured => 2,
            _ => 3,
        };
    }
}

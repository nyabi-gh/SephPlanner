using System;
using System.Collections.Generic;
using System.IO;
using SephPlanner.Core.Model;

namespace SephPlanner.Core.Runtime
{
    public static class ComboCatalogBuilder
    {
        public static ComboDefinition Build(
            string categoryId, IEnumerable<int> staticThresholds,
            Func<IEnumerable<ComboEffectLine>> readEffects)
        {
            var thresholds = new SortedSet<int>();
            foreach (var threshold in staticThresholds)
                if (threshold > 0) thresholds.Add(threshold);

            var effects = new List<ComboEffectLine>();
            try
            {
                foreach (var effect in readEffects())
                {
                    if (effect.Threshold <= 0) continue;
                    thresholds.Add(effect.Threshold);
                    if (!string.IsNullOrWhiteSpace(effect.Text))
                        effects.Add(new ComboEffectLine { Threshold = effect.Threshold, Text = effect.Text });
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException(
                    $"콤보 '{categoryId}'의 발동 단계를 수집하지 못했습니다. 불완전한 카탈로그는 저장하지 않습니다.", ex);
            }

            // 같은 단계의 설명 순서는 게임이 반환한 순서로 유지한다.
            var orderedEffects = new List<ComboEffectLine>();
            foreach (var threshold in thresholds)
                foreach (var effect in effects)
                    if (effect.Threshold == threshold) orderedEffects.Add(effect);

            return new ComboDefinition
            {
                Id = categoryId,
                Thresholds = new List<int>(thresholds),
                Effects = orderedEffects,
            };
        }
    }
}

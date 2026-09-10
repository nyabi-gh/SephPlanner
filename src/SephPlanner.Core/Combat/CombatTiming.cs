using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace SephPlanner.Core.Combat
{
    public static class CombatTiming
    {
        public static bool Matches(CombatSnapshot snapshot, CombatScenario scenario) =>
            scenario.MeasuredActionSeconds.Count > 0 && scenario.MeasuredWeaponKey == Key(snapshot);

        public static CombatScenario Calibrate(CombatSnapshot snapshot, CombatScenario scenario, string text)
        {
            var copy = scenario.Copy();
            var key = Key(snapshot);
            if (key.Length == 0) throw new ArgumentException("무기 공격 자료를 먼저 읽어야 실측 간격을 보정할 수 있습니다.");
            if (copy.MeasuredWeaponKey != key) copy.MeasuredActionSeconds.Clear();
            var stats = new CombatStatState();
            stats.SetSource(new() { Id = "측정 당시", Stats = snapshot.ObservedStats, Amplification = snapshot.ObservedAmplification });
            var words = text.Split(new[] { ',', ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) throw new ArgumentException("일반=0.42,대시=0.8처럼 측정한 평균 공격 간격(초)을 복사하세요.");
            var seen = new HashSet<CombatActionKind>();
            foreach (var word in words)
            {
                var pair = word.Split('=');
                if (pair.Length != 2 || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
                    !CombatDamage.Finite(seconds) || seconds < 0.01 || seconds > 60)
                    throw new ArgumentException("실측 간격은 일반=0.42,대시=0.8처럼 입력하며 0.01~60초를 지원합니다.");
                var action = pair[0] switch
                {
                    "일반" => CombatActionKind.Basic,
                    "대시" => CombatActionKind.Dash,
                    "특수" => CombatActionKind.Special,
                    "마법" => CombatActionKind.Magic,
                    _ => throw new ArgumentException("실측 간격의 동작은 일반·대시·특수·마법 중에서 지정하세요."),
                };
                if (!seen.Add(action)) throw new ArgumentException("같은 동작의 실측 간격을 두 번 지정했습니다.");
                var attacks = snapshot.WeaponAttacks.Where(attack => attack.Action == action).ToList();
                if (action == CombatActionKind.Magic) attacks = new() { new() { Action = action } };
                if (attacks.Count == 0 || attacks.Any(attack => attack.DurationSeconds > 0))
                    throw new ArgumentException(pair[0] + ": 평균 간격으로 보정할 수 있는 무기 동작 자료가 없습니다.");
                var unit = new CombatScenario();
                var scaledUnit = attacks.Average(attack => CombatSimulator.Duration(attack, stats, unit));
                copy.MeasuredActionSeconds[action.ToString()] = seconds / scaledUnit;
            }
            copy.MeasuredWeaponKey = key;
            CombatSimulator.Validate(copy);
            return copy;
        }

        internal static CombatScenario Apply(CombatSnapshot snapshot, CombatScenario scenario)
        {
            if (!Matches(snapshot, scenario)) return scenario;
            var copy = scenario.Copy();
            copy.BasicSeconds = scenario.MeasuredActionSeconds.GetValueOrDefault(nameof(CombatActionKind.Basic), scenario.BasicSeconds);
            copy.DashSeconds = scenario.MeasuredActionSeconds.GetValueOrDefault(nameof(CombatActionKind.Dash), scenario.DashSeconds);
            copy.SpecialSeconds = scenario.MeasuredActionSeconds.GetValueOrDefault(nameof(CombatActionKind.Special), scenario.SpecialSeconds);
            copy.CastingSeconds = scenario.MeasuredActionSeconds.GetValueOrDefault(nameof(CombatActionKind.Magic), scenario.CastingSeconds);
            return copy;
        }

        private static string Key(CombatSnapshot snapshot) => string.Join("|", snapshot.WeaponAttacks.Select(attack => attack.Id));
    }
}

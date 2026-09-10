using System;

namespace SephPlanner.Core.Combat
{
    public static class CombatStatMath
    {
        private static readonly string[] Elements = { "PHYSICAL", "FIRE", "ICE", "LIGHTNING" };

        public static int Read(string key, Func<string, int> amplified)
        {
            var element = Array.FindIndex(Elements, name => name + "DAMAGE" == key);
            var value = amplified(key);
            if (element < 0) return value;
            if (value > 20 && Destination(element, amplified).Index >= 0) value = 20;
            for (var from = 0; from < Elements.Length; from++)
            {
                if (from == element) continue;
                var destination = Destination(from, amplified);
                if (destination.Index != element) continue;
                var excess = amplified(Elements[from] + "DAMAGE") - 20;
                if (excess > 0)
                    value = unchecked(value + Truncate((float)unchecked(excess * destination.Percent) / 100f));
            }
            return value;
        }

        public static int Amplify(int value, int percent) =>
            value == 0 ? 0 : Truncate((float)unchecked(value * unchecked(100 + percent)) / 100f);

        public static int Truncate(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < int.MinValue || value > int.MaxValue)
                throw new OverflowException("정수 범위를 벗어나는 게임 런타임 변환은 아직 검증하지 않았습니다.");
            return (int)value;
        }

        private static (int Index, int Percent) Destination(int from, Func<string, int> amplified)
        {
            var best = (Index: -1, Percent: 0);
            for (var to = 0; to < Elements.Length; to++)
            {
                if (to == from) continue;
                var percent = amplified(Elements[from] + "TO" + Elements[to]);
                if (percent > best.Percent) best = (to, percent);
            }
            return best;
        }
    }
}

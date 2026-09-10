using System;
using System.Globalization;
using System.Numerics;

namespace SephPlanner.Core
{
    internal enum FingerprintFormat { Canonical, LegacyMono, LegacyDotNet }

    internal static class FingerprintNumber
    {
        internal static string Of(double value, FingerprintFormat format, bool roundTrip = false)
        {
            if (format == FingerprintFormat.Canonical)
                return BitConverter.DoubleToInt64Bits(value == 0 ? 0 : value).ToString("x16", CultureInfo.InvariantCulture);
            if (format == FingerprintFormat.LegacyDotNet)
                return value.ToString(roundTrip ? "R" : "G", CultureInfo.InvariantCulture);

            var shortValue = LegacyMono(value, 15);
            return roundTrip && double.Parse(shortValue, CultureInfo.InvariantCulture) != value
                ? LegacyMono(value, 17) : shortValue;
        }

        private static string LegacyMono(double value, int precision)
        {
            if (value == 0) return "0";
            if (double.IsNaN(value) || double.IsInfinity(value)) return value.ToString(CultureInfo.InvariantCulture);

            // 구형 Mono는 정확히 중간인 십진수를 0에서 멀어지게 반올림한다.
            // .NET의 G15/G17은 짝수 쪽을 택하므로 이진 값을 정수 비율로 풀어 계산한다.
            var bits = BitConverter.DoubleToInt64Bits(value);
            var exponent = (int)((bits >> 52) & 0x7ff);
            var numerator = new BigInteger(bits & 0xfffffffffffff);
            if (exponent != 0) numerator += BigInteger.One << 52;
            var shift = exponent == 0 ? -1074 : exponent - 1075;
            var denominator = BigInteger.One;
            if (shift >= 0) numerator <<= shift;
            else denominator <<= -shift;

            var power = numerator.ToString(CultureInfo.InvariantCulture).Length - denominator.ToString(CultureInfo.InvariantCulture).Length;
            if (power >= 0 ? numerator < denominator * BigInteger.Pow(10, power) : numerator * BigInteger.Pow(10, -power) < denominator)
                power--;
            var scale = precision - 1 - power;
            if (scale >= 0) numerator *= BigInteger.Pow(10, scale);
            else denominator *= BigInteger.Pow(10, -scale);
            var rounded = BigInteger.DivRem(numerator, denominator, out var remainder);
            if (remainder * 2 >= denominator) rounded++;
            var digits = rounded.ToString(CultureInfo.InvariantCulture);
            if (digits.Length > precision) power++;
            digits = digits.TrimEnd('0');

            string text;
            if (power < -4 || power >= precision)
                text = digits[0] + (digits.Length > 1 ? "." + digits.Substring(1) : "") +
                    "E" + (power < 0 ? "-" : "+") + Math.Abs(power).ToString("D2", CultureInfo.InvariantCulture);
            else if (power < 0) text = "0." + new string('0', -power - 1) + digits;
            else if (power + 1 >= digits.Length) text = digits + new string('0', power + 1 - digits.Length);
            else text = digits.Insert(power + 1, ".");
            return bits < 0 ? "-" + text : text;
        }
    }
}

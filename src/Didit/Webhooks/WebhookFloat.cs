using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Didit.Internal
{
    /// <summary>
    /// Exact double parsing and shortest round-trip formatting with big integers. The .NET Standard 2.0 build uses them
    /// because .NET Framework neither rounds every parse correctly nor formats more than 15 significant digits
    /// (except through "R", which is not always the shortest form).
    /// </summary>
    internal static class WebhookFloat
    {
        private const int MantissaBits = 53;
        private const int MinExponent = -1074;
        private const int MaxExponent = 971;

        private static readonly BigInteger Two53 = BigInteger.One << MantissaBits;
        private static readonly BigInteger Two54 = BigInteger.One << (MantissaBits + 1);

        /// <summary>Parses a JSON number token to the nearest double (ties to even), like Python's <c>float()</c>.</summary>
        public static double Parse(string token)
        {
            var negative = token.Length > 0 && token[0] == '-';
            var digits = new StringBuilder(token.Length);
            long exponent = 0;
            var i = negative ? 1 : 0;
            for (; i < token.Length && IsDigit(token[i]); i++)
            {
                digits.Append(token[i]);
            }

            if (i < token.Length && token[i] == '.')
            {
                for (i++; i < token.Length && IsDigit(token[i]); i++)
                {
                    digits.Append(token[i]);
                    exponent--;
                }
            }

            if (i < token.Length && (token[i] == 'e' || token[i] == 'E'))
            {
                i++;
                var exponentNegative = i < token.Length && token[i] == '-';
                if (i < token.Length && (token[i] == '-' || token[i] == '+'))
                {
                    i++;
                }

                long written = 0;
                for (; i < token.Length && IsDigit(token[i]); i++)
                {
                    // Anything this large already over- or underflows; stop before the long does.
                    if (written < 1_000_000)
                    {
                        written = (written * 10) + (token[i] - '0');
                    }
                }

                exponent += exponentNegative ? -written : written;
            }

            var significant = digits.ToString().TrimStart('0');
            var magnitude = significant.Length == 0
                ? 0.0
                : ToDouble(BigInteger.Parse(significant, NumberStyles.None, CultureInfo.InvariantCulture), exponent, significant.Length);
            return negative ? -magnitude : magnitude;
        }

        /// <summary>
        /// The shortest digits that read back as <paramref name="value"/> (positive and finite), closest to it on a tie of
        /// length: value = 0.&lt;digits&gt; * 10^point. This is the free-format algorithm of Steele and White as refined by
        /// Burger and Dybvig, with the boundary and last-digit rules of David Gay's dtoa that Python's repr uses.
        /// </summary>
        public static void ShortestDigits(double value, out string digits, out int point)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var biased = (int)((bits >> 52) & 0x7FF);
            var fraction = bits & 0xFFFFFFFFFFFFFL;
            var mantissa = biased == 0 ? new BigInteger(fraction) : new BigInteger(fraction | (1L << 52));
            var exponent = biased == 0 ? MinExponent : biased - 1075;

            // The rounding interval reaches half an ulp each way, and includes its ends when the mantissa is even.
            var inclusive = mantissa.IsEven;
            var narrowBelow = fraction == 0 && biased > 1;

            BigInteger r, s, plus, minus;
            if (exponent >= 0)
            {
                var ulp = BigInteger.One << exponent;
                r = mantissa * ulp * (narrowBelow ? 4 : 2);
                s = narrowBelow ? 4 : 2;
                plus = narrowBelow ? ulp * 2 : ulp;
                minus = ulp;
            }
            else
            {
                r = mantissa * (narrowBelow ? 4 : 2);
                s = BigInteger.One << ((narrowBelow ? 2 : 1) - exponent);
                plus = narrowBelow ? 2 : 1;
                minus = 1;
            }

            var k = (int)Math.Ceiling(Math.Log10(value) - 1e-10);
            if (k >= 0)
            {
                s *= BigInteger.Pow(10, k);
            }
            else
            {
                var scale = BigInteger.Pow(10, -k);
                r *= scale;
                plus *= scale;
                minus *= scale;
            }

            // Settle k so that the upper end of the interval lies in [0.1, 1) after scaling.
            while (inclusive ? r + plus >= s : r + plus > s)
            {
                s *= 10;
                k++;
            }

            while (inclusive ? (r + plus) * 10 < s : (r + plus) * 10 <= s)
            {
                r *= 10;
                plus *= 10;
                minus *= 10;
                k--;
            }

            var builder = new StringBuilder(17);
            while (true)
            {
                var digit = (int)BigInteger.DivRem(r * 10, s, out r);
                plus *= 10;
                minus *= 10;
                var low = inclusive ? r <= minus : r < minus;
                var high = inclusive ? r + plus >= s : r + plus > s;
                if (!low && !high)
                {
                    builder.Append((char)('0' + digit));
                    continue;
                }

                if (high && (!low || IsRoundedUp(r, s, digit)))
                {
                    digit++;
                }

                builder.Append((char)('0' + digit));
                break;
            }

            digits = builder.ToString();
            point = k;
        }

        // Both neighbours of the last digit read back as the value: take the closer one, the even one on a tie.
        private static bool IsRoundedUp(BigInteger remainder, BigInteger scale, int digit)
        {
            var comparison = (remainder * 2).CompareTo(scale);
            return comparison > 0 || (comparison == 0 && (digit & 1) == 1);
        }

        // The double nearest to mantissa * 10^exponent, where the mantissa is positive with digitCount significant digits.
        private static double ToDouble(BigInteger mantissa, long exponent, int digitCount)
        {
            // 10^(digitCount - 1 + exponent) <= value < 10^(digitCount + exponent).
            if (digitCount - 1 + exponent > 308)
            {
                return double.PositiveInfinity;
            }

            if (digitCount + exponent < -324)
            {
                return 0.0;
            }

            BigInteger numerator, denominator;
            if (exponent >= 0)
            {
                numerator = mantissa * BigInteger.Pow(10, (int)exponent);
                denominator = BigInteger.One;
            }
            else
            {
                numerator = mantissa;
                denominator = BigInteger.Pow(10, (int)-exponent);
            }

            // q = floor(numerator / (denominator * 2^shift)) with exactly 54 bits: the mantissa plus one rounding bit.
            var shift = BitLength(numerator) - BitLength(denominator) - (MantissaBits + 1);
            BigInteger quotient, remainder;
            while (true)
            {
                quotient = shift >= 0
                    ? BigInteger.DivRem(numerator, denominator << shift, out remainder)
                    : BigInteger.DivRem(numerator << -shift, denominator, out remainder);
                if (quotient >= Two54)
                {
                    shift++;
                }
                else if (quotient < Two53)
                {
                    shift--;
                }
                else
                {
                    break;
                }
            }

            // value = (quotient + fraction) * 2^shift; keep 53 bits, fewer for subnormals.
            var binaryExponent = Math.Max(shift + 1, MinExponent);
            var drop = binaryExponent - shift;
            BigInteger result;
            if (drop > MantissaBits + 1)
            {
                result = BigInteger.Zero;
            }
            else
            {
                result = quotient >> drop;
                var dropped = quotient - (result << drop);
                var half = BigInteger.One << (drop - 1);
                var comparison = dropped.CompareTo(half);
                if (comparison > 0 || (comparison == 0 && (!remainder.IsZero || !result.IsEven)))
                {
                    result++;
                }
            }

            if (result == Two53)
            {
                result >>= 1;
                binaryExponent++;
            }

            if (binaryExponent > MaxExponent)
            {
                return double.PositiveInfinity;
            }

            var hidden = BigInteger.One << (MantissaBits - 1);
            var resultBits = result >= hidden
                ? ((long)(binaryExponent + 1075) << 52) | (long)(result - hidden)
                : (long)result;
            return BitConverter.Int64BitsToDouble(resultBits);
        }

        private static int BitLength(BigInteger value)
        {
            var bytes = value.ToByteArray();
            var top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0)
            {
                top--;
            }

            var length = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1)
            {
                length++;
            }

            return length;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}

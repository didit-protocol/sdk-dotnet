using System;
using System.Globalization;
using System.Linq;
using Didit.Internal;
using Xunit;

namespace Didit.Tests.Webhooks
{
    /// <summary>
    /// The exact parsing and shortest formatting the .NET Standard 2.0 build relies on. Expected values come from Python's
    /// <c>float()</c> and <c>repr()</c>; on .NET Core the results are also checked against the runtime, which is exact there.
    /// </summary>
    public sealed class WebhookFloatTests
    {
        [Theory]
        [InlineData("0.1", 0.1)]
        [InlineData("-2.5", -2.5)]
        [InlineData("1E2", 100.0)]
        [InlineData("5e-324", 5e-324)]
        [InlineData("4.9406564584124654e-324", 5e-324)]
        [InlineData("2.4703282292062327e-324", 0.0)]
        [InlineData("2.4703282292062328e-324", 5e-324)]
        [InlineData("2.2250738585072011e-308", 2.225073858507201e-308)]
        [InlineData("1.7976931348623157e308", double.MaxValue)]
        [InlineData("1.7976931348623158e308", double.MaxValue)]
        [InlineData("1.7976931348623159e308", double.PositiveInfinity)]
        [InlineData("9007199254740993", 9007199254740992.0)]
        [InlineData("9007199254740995.0", 9007199254740996.0)]
        [InlineData("0.0001e312", 1e308)]
        [InlineData("1e-400", 0.0)]
        [InlineData("1e400", double.PositiveInfinity)]
        [InlineData("-1e400", double.NegativeInfinity)]
        [InlineData("0.1000000000000000055511151231257827021181583404541015625", 0.1)]
        [InlineData("1.00000000000000011102230246251565404236316680908203125", 1.0)]
        [InlineData("1.00000000000000011102230246251565404236316680908203126", 1.0000000000000002)]
        [InlineData("123456789012345678901234567890e-10", 1.2345678901234567e19)]
        public void ParseRoundsToTheNearestDouble(string token, double expected)
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(WebhookFloat.Parse(token)));
        }

        [Fact]
        public void ParseKeepsTheSignOfZero()
        {
            Assert.Equal(BitConverter.DoubleToInt64Bits(-0.0), BitConverter.DoubleToInt64Bits(WebhookFloat.Parse("-0.0")));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(WebhookFloat.Parse("0e10")));
        }

        [Theory]
        [InlineData(0.1, "1", 0)]
        [InlineData(0.5, "5", 0)]
        [InlineData(1.0, "1", 1)]
        [InlineData(4.35, "435", 1)]
        [InlineData(1e-7, "1", -6)]
        [InlineData(0.3333333333333333, "3333333333333333", 0)]
        [InlineData(0.30000000000000004, "30000000000000004", 0)]
        [InlineData(123456789.123, "123456789123", 9)]
        [InlineData(5e-324, "5", -323)]
        [InlineData(1.5e-323, "15", -322)]
        [InlineData(2.2250738585072014e-308, "22250738585072014", -307)]
        [InlineData(double.MaxValue, "17976931348623157", 309)]
        [InlineData(1e23, "1", 24)]
        [InlineData(1152921504606846976.0, "1152921504606847", 19)]
        public void ShortestDigitsMatchPythonRepr(double value, string digits, int point)
        {
            WebhookFloat.ShortestDigits(value, out var actualDigits, out var actualPoint);

            Assert.Equal(digits, actualDigits);
            Assert.Equal(point, actualPoint);
        }

        [Fact]
        public void ShortestDigitsReadBackAsTheValue()
        {
            var random = new Random(20261017);
            for (var i = 0; i < 20000; i++)
            {
                var value = RandomPositiveDouble(random);

                WebhookFloat.ShortestDigits(value, out var digits, out var point);

                Assert.Equal(value, WebhookFloat.Parse("0." + digits + "e" + point.ToString(CultureInfo.InvariantCulture)));
                Assert.NotEqual('0', digits[digits.Length - 1]);
            }
        }

#if NETCOREAPP3_0_OR_GREATER
        [Fact]
        public void ShortestDigitsMatchTheRuntimeRoundTripFormat()
        {
            var random = new Random(17);
            for (var i = 0; i < 50000; i++)
            {
                var value = RandomPositiveDouble(random);

                WebhookFloat.ShortestDigits(value, out var digits, out var point);

                Assert.Equal(RuntimeDigits(value), (digits, point));
            }
        }

        [Fact]
        public void ParseMatchesTheRuntimeParser()
        {
            var random = new Random(42);
            for (var i = 0; i < 50000; i++)
            {
                var digitCount = random.Next(1, 26);
                var digits = new string(Enumerable.Range(0, digitCount).Select(_ => (char)('0' + random.Next(10))).ToArray());
                var exponent = random.Next(-345, 320);
                var token = digits.Insert(random.Next(1, digitCount + 1), ".") + "e" + exponent.ToString(CultureInfo.InvariantCulture);

                var expected = double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);

                Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(WebhookFloat.Parse(token)));
            }
        }

        private static (string Digits, int Point) RuntimeDigits(double value)
        {
            var text = value.ToString("R", CultureInfo.InvariantCulture);
            var exponentAt = text.IndexOf('E', StringComparison.Ordinal);
            var mantissa = exponentAt < 0 ? text : text.Substring(0, exponentAt);
            var exponent = exponentAt < 0 ? 0 : int.Parse(text.Substring(exponentAt + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
            var integerPart = dot < 0 ? mantissa : mantissa.Substring(0, dot);
            var all = dot < 0 ? mantissa : integerPart + mantissa.Substring(dot + 1);
            var significant = all.TrimStart('0');
            return (significant.TrimEnd('0'), integerPart.Length + exponent - (all.Length - significant.Length));
        }
#endif

        private static double RandomPositiveDouble(Random random)
        {
            var bytes = new byte[8];
            while (true)
            {
                random.NextBytes(bytes);
                var value = BitConverter.Int64BitsToDouble(BitConverter.ToInt64(bytes, 0) & long.MaxValue);
                if (value != 0 && !double.IsNaN(value) && !double.IsInfinity(value))
                {
                    return value;
                }
            }
        }
    }
}

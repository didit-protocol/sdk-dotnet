using System;
using System.Linq;
using System.Text.Json;
using Didit.Internal;
using Xunit;

namespace Didit.Tests.Webhooks
{
    /// <summary>
    /// The canonical JSON of DESIGN.md 9.2. Expected values come from Python's
    /// <c>json.dumps(shorten_floats(json.loads(x)), sort_keys=True, separators=(",", ":"), ensure_ascii=False)</c>.
    /// </summary>
    public sealed class CanonicalJsonTests
    {
        private const string LargestDouble =
            "179769313486231570814527423731704356798070567525844996598917476803157260780028538760589558632766878171540458953514382464234321326889464182768467546703537516986049910576551282076245490090389328944075868508455133942304583236903222948165808559332123348274797826204144723168738177180919299881250404026184124858368";

        [Theory]
        [InlineData("1e-05", "1e-05")]
        [InlineData("0.00001", "1e-05")]
        [InlineData("1.5e-07", "1.5e-07")]
        [InlineData("2.5e-5", "2.5e-05")]
        [InlineData("1.23e-18", "1.23e-18")]
        [InlineData("-1.5e-10", "-1.5e-10")]
        [InlineData("5e-324", "5e-324")]
        [InlineData("2.2250738585072014e-308", "2.2250738585072014e-308")]
        [InlineData("0.0001", "0.0001")]
        [InlineData("0.001", "0.001")]
        [InlineData("0.000123456789", "0.000123456789")]
        [InlineData("1e16", "10000000000000000")]
        [InlineData("1E16", "10000000000000000")]
        [InlineData("1e22", "10000000000000000000000")]
        [InlineData("9.999999999999999e15", "10000000000000000")]
        [InlineData("-0.0", "0")]
        [InlineData("-0", "0")]
        [InlineData("0", "0")]
        [InlineData("87.0", "87")]
        [InlineData("100.0e-2", "1")]
        [InlineData("9007199254740993", "9007199254740993")]
        [InlineData("-9007199254740993", "-9007199254740993")]
        [InlineData("123456789012345678901234567890", "123456789012345678901234567890")]
        [InlineData("9007199254740993.0", "9007199254740992")]
        [InlineData("1E2", "100")]
        [InlineData("1e+2", "100")]
        [InlineData("123456789.123", "123456789.123")]
        [InlineData("123456789012345.67", "123456789012345.67")]
        [InlineData("1000000000000000.5", "1000000000000000.5")]
        [InlineData("0.1", "0.1")]
        [InlineData("0.30000000000000004", "0.30000000000000004")]
        [InlineData("0.3333333333333333", "0.3333333333333333")]
        [InlineData("1.0000000000000002", "1.0000000000000002")]
        [InlineData("1234.5", "1234.5")]
        [InlineData("12.50", "12.5")]
        [InlineData("4.35", "4.35")]
        [InlineData("-2.5", "-2.5")]
        [InlineData("1.7976931348623157e308", LargestDouble)]
        [InlineData("1e400", "Infinity")]
        [InlineData("-1e400", "-Infinity")]
        public void NumbersAreFormattedLikePython(string json, string expected)
        {
            Assert.Equal(expected, DiditWebhooks.Canonicalize(json));
        }

        [Fact]
        public void IntegralFloatsInsideDocumentsBecomeIntegers()
        {
            Assert.Equal(
                "{\"a\":[1,2.5,3],\"score\":87,\"tiny\":1e-05}",
                DiditWebhooks.Canonicalize("{\"tiny\":0.00001,\"score\":87.0,\"a\":[1.0,2.5,3]}"));
        }

        [Fact]
        public void StringsEscapeOnlyWhatPythonEscapes()
        {
            var json = "\"a\\u0000b\\u001fc\\u007fd\\u2028e\\u2029/f\\\"g\\\\h\\b\\f\\n\\r\\t\\u0080\\ud83d\\ude00\\u00e9\\/\"";

            var canonical = DiditWebhooks.Canonicalize(json);

            Assert.Equal("\"a\\u0000b\\u001fc\u007fd\u2028e\u2029/f\\\"g\\\\h\\b\\f\\n\\r\\t\u0080\U0001F600\u00e9/\"", canonical);
        }

        [Fact]
        public void ControlCharactersUseLowercaseHex()
        {
            Assert.Equal("\"\\u001b\\u000b\\u001a\"", DiditWebhooks.Canonicalize("\"\\u001B\\u000B\\u001A\""));
        }

        [Fact]
        public void KeysAreSortedByCodePoint()
        {
            var json = "{\"b\":1,\"a\":2,\"\\ufb00\":3,\"\\ud83d\\ude00\":4,\"\\u00e9\":5,\"A\":6,\"10\":7,\"9\":8,\"_\":9,\"a\\u0000\":10,\"\":11}";

            Assert.Equal(
                "{\"\":11,\"10\":7,\"9\":8,\"A\":6,\"_\":9,\"a\":2,\"a\\u0000\":10,\"b\":1,\"\u00e9\":5,\"\uFB00\":3,\"\U0001F600\":4}",
                DiditWebhooks.Canonicalize(json));
        }

        [Fact]
        public void CodePointOrderDiffersFromUtf16Order()
        {
            // U+FB00 is a single UTF-16 unit above the surrogates that encode U+1F600.
            Assert.True(string.CompareOrdinal("\uFB00", "\U0001F600") > 0);
            Assert.True(WebhookCanonicalJson.CompareCodePoints("\uFB00", "\U0001F600") < 0);
            Assert.True(WebhookCanonicalJson.CompareCodePoints("\U0001F600", "\U0001F601") < 0);
            Assert.True(WebhookCanonicalJson.CompareCodePoints("a", "ab") < 0);
            Assert.Equal(0, WebhookCanonicalJson.CompareCodePoints("\U0001F600", "\U0001F600"));
        }

        [Fact]
        public void DuplicateKeysKeepTheLastValue()
        {
            Assert.Equal("{\"a\":3,\"b\":2}", DiditWebhooks.Canonicalize("{\"a\":1,\"b\":2,\"a\":3}"));
            Assert.Equal("{\"x\":{\"k\":{\"y\":2,\"z\":1}}}", DiditWebhooks.Canonicalize("{\"x\":{\"k\":1,\"k\":{\"z\":1,\"y\":2}}}"));
        }

        [Fact]
        public void WhitespaceIsRemovedAndLiteralsKept()
        {
            Assert.Equal(
                "{\"x\":[1,2.5,true,false,null,{},[]]}",
                DiditWebhooks.Canonicalize(" { \"x\" : [ 1 , 2.50 , true , false, null, {} , [] ] } "));
        }

        [Fact]
        public void DeeplyNestedDocumentsAreSupported()
        {
            var depth = 200;
            var json = string.Concat(Enumerable.Repeat("{\"a\":", depth)) + "1" + new string('}', depth);

            Assert.Equal(json, DiditWebhooks.Canonicalize(json));
        }

        [Fact]
        public void ParsedElementGivesTheSameResult()
        {
            using var document = JsonDocument.Parse("{\"b\":[0.5,1.0],\"a\":\"Jos\\u00e9\"}");

            Assert.Equal("{\"a\":\"Jos\u00e9\",\"b\":[0.5,1]}", DiditWebhooks.Canonicalize(document.RootElement));
        }

        [Fact]
        public void InvalidJsonThrows()
        {
            Assert.ThrowsAny<JsonException>(() => DiditWebhooks.Canonicalize("{\"a\":"));
            Assert.ThrowsAny<JsonException>(() => DiditWebhooks.Canonicalize("NaN"));
        }

        [Fact]
        public void LoneSurrogatesThrow()
        {
            Assert.ThrowsAny<JsonException>(() => DiditWebhooks.Canonicalize("\"\\ud800\""));
            Assert.ThrowsAny<JsonException>(() => DiditWebhooks.Canonicalize("{\"\\udc00\":1}"));
        }

        [Fact]
        public void UndefinedElementThrows()
        {
            Assert.Throws<ArgumentException>(() => DiditWebhooks.Canonicalize(default(JsonElement)));
        }

        [Fact]
        public void NullJsonThrows()
        {
            Assert.Throws<ArgumentNullException>(() => DiditWebhooks.Canonicalize((string)null!));
        }

        [Theory]
        [InlineData(0.1, "0.1")]
        [InlineData(1e-5, "1e-05")]
        [InlineData(1.5e-7, "1.5e-07")]
        [InlineData(0.0001, "0.0001")]
        [InlineData(1e16, "10000000000000000")]
        [InlineData(-0.0, "0")]
        [InlineData(87.0, "87")]
        [InlineData(1e300, "1000000000000000052504760255204420248704468581108159154915854115511802457988908195786371375080447864043704443832883878176942523235360430575644792184786706982848387200926575803737830233794788090059368953234970799945081119038967640880074652742780142494579258788820056842838115669472196386865459400540160")]
        [InlineData(double.NaN, "NaN")]
        [InlineData(double.PositiveInfinity, "Infinity")]
        [InlineData(double.NegativeInfinity, "-Infinity")]
        public void DoublesAreFormattedLikePython(double value, string expected)
        {
            Assert.Equal(expected, WebhookCanonicalJson.FormatFloat(value));
        }
    }
}

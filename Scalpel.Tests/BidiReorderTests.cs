using Scalpel.Services;
using Xunit;

namespace Scalpel.Tests
{
    public class BidiReorderTests
    {
        // shalom = \u05E9\u05DC\u05D5\u05DD  (logical: shin lamed vav mem)
        private const string Shalom = "\u05E9\u05DC\u05D5\u05DD";
        // visual (reversed): \u05DD\u05D5\u05DC\u05E9  (mem vav lamed shin)
        private const string ShalomVisual = "\u05DD\u05D5\u05DC\u05E9";

        [Theory]
        [InlineData("hello", false)]
        [InlineData("", false)]
        [InlineData("\u05E9\u05DC\u05D5\u05DD", true)]
        [InlineData("abc \u05E9\u05DC", true)]
        public void ContainsRtl_Detects(string s, bool expected)
            => Assert.Equal(expected, BidiReorder.ContainsRtl(s));

        [Fact]
        public void ToVisual_PureLatin_Unchanged()
            => Assert.Equal("hello world", BidiReorder.ToVisual("hello world"));

        [Fact]
        public void ToVisual_PureHebrew_Reversed()
            => Assert.Equal(ShalomVisual, BidiReorder.ToVisual(Shalom));

        [Fact]
        public void ToVisual_HebrewThenNumber_NumberStaysLtrOnLeft()
        {
            // logical: "shalom 123"  -> visual: "123 " + reversed shalom
            string logical = Shalom + " 123";
            string expected = "123 " + ShalomVisual;
            Assert.Equal(expected, BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_HebrewThenLatinWord_LatinForwardOnLeft()
        {
            // logical: "shalom world" -> visual: "world " + reversed shalom
            string logical = Shalom + " world";
            string expected = "world " + ShalomVisual;
            Assert.Equal(expected, BidiReorder.ToVisual(logical));
        }

        [Fact]
        public void ToVisual_Empty_ReturnsEmpty()
            => Assert.Equal("", BidiReorder.ToVisual(""));

        [Fact]
        public void ToVisual_HebrewWithPunctuation_ContainsExpectedSubstrings()
        {
            // "\u05E9\u05DC\u05D5\u05DD, world." -- base RTL: Hebrew run reversed, Latin forward;
            // neutral punctuation (comma, period) resolves to RTL and may separate from Latin run;
            // assert robust invariants: Latin word present, reversed shalom present, total length preserved
            var result = BidiReorder.ToVisual(Shalom + ", world.");
            Assert.Contains("world", result); // Latin run appears intact (trailing . reorders as neutral)
            Assert.Contains(ShalomVisual, result); // reversed shalom: \u05DD\u05D5\u05DC\u05E9
            Assert.Equal((Shalom + ", world.").Length, result.Length);
        }

        [Fact]
        public void ToVisual_HebrewWithTrailingDigits_ReversedWithDigitsLeading()
        {
            // "\u05E9\u05DC\u05D5\u05DD 42" -> "42 \u05DD\u05D5\u05DC\u05E9"
            Assert.Equal("42 " + ShalomVisual, BidiReorder.ToVisual(Shalom + " 42"));
        }
    }
}

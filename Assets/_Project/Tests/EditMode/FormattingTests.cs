using NUnit.Framework;
using TNLorry.Services;
using TNLorry.Utils;

namespace TNLorry.Tests
{
    public class CurrencyFormatTests
    {
        [TestCase(0L, "₹0")]
        [TestCase(7L, "₹7")]
        [TestCase(999L, "₹999")]
        [TestCase(1000L, "₹1,000")]
        [TestCase(45200L, "₹45,200")]
        [TestCase(150000L, "₹1,50,000")]
        [TestCase(1234567L, "₹12,34,567")]
        [TestCase(12000000L, "₹1,20,00,000")]
        public void GroupsDigitsTheIndianWay(long amount, string expected)
        {
            // 12,34,567 and not 1,234,567. Getting this wrong reads as a foreign game.
            Assert.AreEqual(expected, CurrencyFormat.Rupees(amount));
        }

        [Test]
        public void NegativeBalancesKeepTheSignOutsideTheSymbol()
        {
            Assert.AreEqual("-₹2,000", CurrencyFormat.Rupees(-2000));
        }

        [Test]
        public void VeryLargeBalancesDoNotFallIntoScientificNotation()
        {
            // The float-coercion bug this guards against produced "1.5E+10" on screen.
            string formatted = CurrencyFormat.Rupees(15000000000L);

            Assert.IsFalse(formatted.Contains("E"), "Must never render in scientific notation.");
            Assert.AreEqual("₹15,00,00,00,000", formatted);
        }

        [TestCase(500L, "₹500")]
        [TestCase(45200L, "₹45.2K")]
        [TestCase(150000L, "₹1.5 L")]
        [TestCase(1200000L, "₹12 L")]
        [TestCase(12000000L, "₹1.2 Cr")]
        public void ShortFormUsesLakhAndCrore(long amount, string expected)
        {
            Assert.AreEqual(expected, CurrencyFormat.RupeesShort(amount));
        }

        [Test]
        public void GroupHandlesEmptyAndShortInput()
        {
            Assert.AreEqual("", CurrencyFormat.Group(""));
            Assert.AreEqual("12", CurrencyFormat.Group("12"));
            Assert.AreEqual("123", CurrencyFormat.Group("123"));
            Assert.IsNull(CurrencyFormat.Group(null));
        }
    }

    public class FlatJsonTests
    {
        [Test]
        public void ParsesAFlatStringTable()
        {
            var table = FlatJson.Parse("{ \"ui.start_job\": \"Start Job\", \"hud.fuel_low\": \"Fuel low!\" }");

            Assert.AreEqual(2, table.Count);
            Assert.AreEqual("Start Job", table["ui.start_job"]);
            Assert.AreEqual("Fuel low!", table["hud.fuel_low"]);
        }

        [Test]
        public void HandlesEscapedCharacters()
        {
            var table = FlatJson.Parse("{ \"a\": \"line\\nbreak\", \"b\": \"say \\\"hi\\\"\", \"c\": \"back\\\\slash\" }");

            Assert.AreEqual("line\nbreak", table["a"]);
            Assert.AreEqual("say \"hi\"", table["b"]);
            Assert.AreEqual("back\\slash", table["c"]);
        }

        [Test]
        public void DecodesUnicodeEscapes()
        {
            // Tamil tables may be authored as ASCII with \u escapes by some tooling; if this
            // breaks, every Tamil string silently renders as mojibake.
            var table = FlatJson.Parse("{ \"city.madurai\": \"\\u0bae\\u0ba4\\u0bc1\\u0bb0\\u0bc8\" }");

            Assert.AreEqual("மதுரை", table["city.madurai"]);
        }

        [Test]
        public void HandlesLiteralTamilText()
        {
            var table = FlatJson.Parse("{ \"result.delivered\": \"சரக்கு வழங்கப்பட்டது!\" }");

            Assert.AreEqual("சரக்கு வழங்கப்பட்டது!", table["result.delivered"]);
        }

        [Test]
        public void ToleratesWhitespaceAndNewlines()
        {
            var table = FlatJson.Parse("{\n  \"a\" :  \"one\" ,\n\n  \"b\":\"two\"\n}");

            Assert.AreEqual("one", table["a"]);
            Assert.AreEqual("two", table["b"]);
        }

        [Test]
        public void EmptyAndMalformedInputDoNotThrow()
        {
            Assert.IsEmpty(FlatJson.Parse(""));
            Assert.IsEmpty(FlatJson.Parse(null));
            Assert.IsEmpty(FlatJson.Parse("{}"));

            // A truncated file must yield what it could read rather than taking down boot.
            var partial = FlatJson.Parse("{ \"a\": \"one\", \"b\": \"unterminated");
            Assert.AreEqual("one", partial["a"]);
        }

        [Test]
        public void LaterDuplicateKeyWins()
        {
            var table = FlatJson.Parse("{ \"a\": \"first\", \"a\": \"second\" }");
            Assert.AreEqual("second", table["a"]);
        }
    }
}

using System;
using System.Globalization;
using NUnit.Framework;

namespace Incremental.Tests
{
    public class FmtTests
    {
        static string L(double v, Rounding r = Rounding.Down) => Fmt.Num(v, r, Notation.Letters);
        static string S(double v, Rounding r = Rounding.Down) => Fmt.Num(v, r, Notation.Scientific);

        [TestCase(0, "0")]
        [TestCase(1, "1")]
        [TestCase(999, "999")]
        [TestCase(1000, "1.00K")]
        [TestCase(1234, "1.23K")]
        [TestCase(12345, "12.3K")]
        [TestCase(123456, "123K")]
        [TestCase(999999, "999K")]
        [TestCase(1e6, "1.00M")]
        [TestCase(45.6e6, "45.6M")]
        [TestCase(789e9, "789B")]
        [TestCase(1e12, "1.00T")]
        [TestCase(1e15, "1.00Qa")]
        [TestCase(1e18, "1.00Qi")]
        [TestCase(1e21, "1.00Sx")]
        [TestCase(1e24, "1.00Sp")]
        [TestCase(1e27, "1.00Oc")]
        [TestCase(1e30, "1.00No")]
        [TestCase(1e33, "1.00Dc")]
        [TestCase(999e33, "999Dc")]
        [TestCase(1e36, "1.00e36")]
        [TestCase(1.5e100, "1.50e100")]
        public void Letters_RoundDown(double v, string expected) => Assert.AreEqual(expected, L(v));

        [TestCase(999.1, "1.00K")]
        [TestCase(999, "999")]
        [TestCase(1000, "1.00K")]
        [TestCase(1001, "1.01K")]
        [TestCase(1230, "1.23K")]
        [TestCase(1234, "1.24K")]
        [TestCase(999999, "1.00M")]
        [TestCase(1e33, "1.00Dc")]
        [TestCase(1e36, "1.00e36")]
        public void Letters_RoundUp(double v, string expected) => Assert.AreEqual(expected, L(v, Rounding.Up));

        [Test]
        public void BelowThousand_IsInteger_BothModes()
        {
            Assert.AreEqual("999", L(999.99));
            Assert.AreEqual("999", S(999.99));
            Assert.AreEqual("22", L(22.5));
            Assert.AreEqual("23", L(22.5, Rounding.Up));
        }

        [TestCase(1000, "1.00e3")]
        [TestCase(1234, "1.23e3")]
        [TestCase(1.23e9, "1.23e9")]
        [TestCase(999999, "9.99e5")]
        [TestCase(1e33, "1.00e33")]
        public void Scientific_RoundDown(double v, string expected) => Assert.AreEqual(expected, S(v));

        [Test]
        public void Scientific_RoundUp_CarriesIntoNextExponent() => Assert.AreEqual("1.00e6", S(999999, Rounding.Up));

        [Test]
        public void NonFinite_And_Negative()
        {
            Assert.AreEqual("-", L(double.NaN));
            Assert.AreEqual("∞", L(double.PositiveInfinity));
            Assert.AreEqual("-1.24K", L(-1234)); // floor of a negative rounds away from zero
        }

        [Test]
        public void ModeField_SelectsNotation()
        {
            var saved = Fmt.Mode;
            try
            {
                Fmt.Mode = Notation.Scientific;
                Assert.AreEqual("1.23e9", Fmt.Num(1.23e9));
                Fmt.Mode = Notation.Letters;
                Assert.AreEqual("1.23B", Fmt.Num(1.23e9));
            }
            finally
            {
                Fmt.Mode = saved;
            }
        }

        /// <summary>
        /// The reason currency rounds down and costs round up: whenever the shown currency is at least the shown cost,
        /// the real currency is at least the real cost (checked exactly below 2^53).
        /// </summary>
        [Test]
        public void ShownCurrencyAtLeastShownCost_ImpliesAffordable()
        {
            var rng = new Random(12345);
            int compared = 0;
            for (int i = 0; i < 20000; i++)
            {
                double exp = rng.NextDouble() * 15.0;
                double cost = Math.Ceiling(Math.Pow(10, exp));
                // Currency near the cost, sometimes a little below, sometimes above, often fractional.
                double currency = cost * (0.99 + rng.NextDouble() * 0.02) + rng.NextDouble();
                foreach (var notation in new[] { Notation.Letters, Notation.Scientific })
                {
                    double shownCurrency = Parse(Fmt.Num(currency, Rounding.Down, notation));
                    double shownCost = Parse(Fmt.Num(cost, Rounding.Up, notation));
                    Assert.LessOrEqual(shownCurrency, Math.Floor(currency), "shown currency exceeds the real value: " + currency);
                    Assert.GreaterOrEqual(shownCost, cost, "shown cost is below the real value: " + cost);
                    if (shownCurrency >= shownCost)
                    {
                        compared++;
                        Assert.GreaterOrEqual(currency, cost, $"shows affordable but is not: currency {currency}, cost {cost}");
                    }
                }
            }
            Assert.Greater(compared, 1000);
        }

        static readonly string[] Suffixes = { "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

        /// <summary>Turns a formatted number back into a value (exact for the 3-digit mantissa).</summary>
        static double Parse(string s)
        {
            int e = s.IndexOf('e');
            if (e >= 0)
            {
                decimal m = decimal.Parse(s.Substring(0, e), CultureInfo.InvariantCulture);
                int exp = int.Parse(s.Substring(e + 1), CultureInfo.InvariantCulture);
                for (int k = 0; k < exp; k++) m *= 10m;
                return decimal.ToDouble(m);
            }
            for (int i = Suffixes.Length - 1; i >= 0; i--)
            {
                if (!s.EndsWith(Suffixes[i], StringComparison.Ordinal)) continue;
                decimal mantissa = decimal.Parse(s.Substring(0, s.Length - Suffixes[i].Length), CultureInfo.InvariantCulture);
                decimal scale = 1m;
                for (int k = 0; k <= i; k++) scale *= 1000m;
                return decimal.ToDouble(mantissa * scale);
            }
            return double.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}

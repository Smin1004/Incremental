using System;
using System.Globalization;

namespace Incremental
{
    /// <summary>How large numbers are written (02 §6). Stored in settings.json.</summary>
    public enum Notation
    {
        /// <summary>1.23K, 45.6M, 789B … up to Dc, scientific above that. Default.</summary>
        Letters = 0,
        /// <summary>1.23e9.</summary>
        Scientific = 1,
    }

    /// <summary>Rounding direction for <see cref="Fmt.Num(double, Rounding)"/>.</summary>
    public enum Rounding
    {
        /// <summary>Currency, income, sale price: never shows more than the real value.</summary>
        Down,
        /// <summary>Costs: never shows less than the real value.</summary>
        Up,
    }

    /// <summary>
    /// Number formatting for UI and logs. Currency, income, costs and sale prices all go through <see cref="Num(double, Rounding)"/>:
    /// integers below 1,000, otherwise 3 significant digits. Currency rounds down and costs round up, so whenever the
    /// shown currency is at least the shown cost the purchase really succeeds (exact below 2^53 ≈ 9.0e15).
    /// </summary>
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Letter suffix per power of 1,000. Index 11 (Dc) = 1e33 is the last one; above that is scientific.</summary>
        static readonly string[] Suffixes = { "", "K", "M", "B", "T", "Qa", "Qi", "Sx", "Sp", "Oc", "No", "Dc" };

        /// <summary>2^53: every integer below this is exact in a double.</summary>
        const double ExactIntLimit = 9007199254740992.0;

        static double[] pow10;

        /// <summary>Current notation (settings.json, F7).</summary>
        public static Notation Mode = Notation.Letters;

        public static string Num(double v, Rounding rounding = Rounding.Down) => Num(v, rounding, Mode);

        public static string Num(double v, Rounding rounding, Notation notation)
        {
            if (double.IsNaN(v)) return "-";
            if (double.IsPositiveInfinity(v)) return "∞";
            if (double.IsNegativeInfinity(v)) return "-∞";
            if (v < 0) return "-" + Num(-v, rounding == Rounding.Down ? Rounding.Up : Rounding.Down, notation);

            double iv = rounding == Rounding.Up ? Math.Ceiling(v) : Math.Floor(v);
            if (iv < 1000.0) return iv.ToString("0", Inv);

            // Three significant digits: m in [100, 999], value ≈ m × 10^(exp-2).
            int exp = (int)Math.Floor(Math.Log10(iv));
            if (exp > 308) exp = 308;
            if (Pow10(exp) > iv) exp--;
            else if (exp < 308 && Pow10(exp + 1) <= iv) exp++;

            double m;
            if (iv < ExactIntLimit)
            {
                // Exact integer arithmetic: shown currency never exceeds the real value, shown cost is never below it.
                long n = (long)iv;
                long d = (long)Pow10(exp - 2);
                m = rounding == Rounding.Up ? (n + d - 1) / d : n / d;
            }
            else
            {
                // Above 2^53 doubles are sparse; ignore binary noise (1e36 is stored as 1.0000000000000000429e36).
                double s = iv / Pow10(exp - 2);
                double r = Math.Round(s);
                if (Math.Abs(s - r) <= r * 1e-12) s = r;
                m = rounding == Rounding.Up ? Math.Ceiling(s) : Math.Floor(s);
            }
            if (m >= 1000.0)
            {
                m = 100.0;
                exp++;
            }

            string digits = ((int)m).ToString(Inv);
            int group = exp / 3;
            if (notation == Notation.Letters && group < Suffixes.Length)
            {
                int intDigits = exp - group * 3 + 1;
                string mantissa = intDigits >= 3 ? digits : digits.Substring(0, intDigits) + "." + digits.Substring(intDigits);
                return mantissa + Suffixes[group];
            }
            return digits.Substring(0, 1) + "." + digits.Substring(1) + "e" + exp.ToString(Inv);
        }

        public static string Int(double v) => Math.Floor(v).ToString("N0", Inv);
        public static string Mult(double v) => "×" + v.ToString("F2", Inv);
        public static string Sec(double v) => v.ToString("F1", Inv);
        public static string Csv(double v) => v.ToString("R", Inv);
        /// <summary>Up to two decimals, no trailing zeros (upgrade effect numbers: 1, 1.5, 20).</summary>
        public static string Short(double v) => v.ToString("0.##", Inv);

        static double Pow10(int e)
        {
            if (pow10 == null)
            {
                var table = new double[309];
                for (int i = 0; i < table.Length; i++) table[i] = double.Parse("1e" + i.ToString(Inv), Inv);
                pow10 = table;
            }
            return pow10[Math.Max(0, Math.Min(308, e))];
        }
    }
}

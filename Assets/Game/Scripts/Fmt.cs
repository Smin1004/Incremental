using System;
using System.Globalization;

namespace Incremental
{
    /// <summary>Number formatting for UI and logs. Integers with thousands separators, multipliers as ×1.00.</summary>
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string Int(double v) => Math.Floor(v).ToString("N0", Inv);
        public static string Mult(double v) => "×" + v.ToString("F2", Inv);
        public static string Sec(double v) => v.ToString("F1", Inv);
        public static string Csv(double v) => v.ToString("R", Inv);
    }
}

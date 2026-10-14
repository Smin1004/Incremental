using System;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 28 §4: the record of a measurement play. Every gate purchase, and the very first purchase of a game, writes one
    /// console line "[Progress] …" and one row of &lt;dir&gt;/progress.csv: the play time since the new game (runs, result
    /// screens and the tree, pauses left out: MetaState.playClockSec), the run number, the ring, and the currency then.
    /// A new game (Shift+F9) writes a new_game row, so one file can hold several measurement plays one after another.
    /// </summary>
    public static class ProgressLogger
    {
        public const string FileName = "progress.csv";
        public const string Header = "event,node,play_time_s,play_time,run,ring,currency_before,cost,currency_after,wall_clock";

        public static string FilePath(string dir) => Path.Combine(dir, FileName);

        /// <summary>"m:ss" (or "h:mm:ss"), the way 28 §4's sheet writes its targets.</summary>
        public static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Round(Math.Max(0.0, seconds)));
            return t.TotalHours >= 1
                ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds)
                : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", t.Minutes, t.Seconds);
        }

        public static string Row(string evt, string node, MetaState m, double before, double cost)
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Join(",", evt, node ?? string.Empty, m.playClockSec.ToString("F1", inv), Clock(m.playClockSec),
                m.runCount.ToString(inv), m.unlockedMaxTier.ToString(inv), Fmt.Csv(before), Fmt.Csv(cost), Fmt.Csv(m.currency),
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", inv));
        }

        /// <summary>Appends a row (writing the header into a new file) and logs the console line. Never throws.</summary>
        public static void Write(string dir, string evt, string node, MetaState m, double before, double cost)
        {
            Debug.Log(evt == "new_game"
                ? "[Progress] new game: the play clock starts at 0"
                : $"[Progress] {evt} {node} at {Clock(m.playClockSec)} ({m.playClockSec:F0} s of play), run {m.runCount}, ring {m.unlockedMaxTier}, " +
                  $"currency {Fmt.Num(before)} -> {Fmt.Num(m.currency)}");
            try
            {
                Directory.CreateDirectory(dir);
                string path = FilePath(dir);
                bool exists = File.Exists(path);
                using (var w = new StreamWriter(path, true))
                {
                    if (!exists) w.WriteLine(Header);
                    w.WriteLine(Row(evt, node, m, before, cost));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Progress] progress.csv write failed: " + e.Message);
            }
        }
    }
}

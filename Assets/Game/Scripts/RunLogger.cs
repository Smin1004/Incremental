using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Appends one CSV row per run to &lt;dir&gt;/run_log.csv and builds the console summary.
    /// Columns come from the tables: run, duration_s, income, t1..tN, ratio_vs_last, lvl_&lt;id&gt;..., end_reason.
    /// If the existing file has a different header it is renamed to run_log_&lt;time&gt;.csv and a new file is started.
    /// </summary>
    public static class RunLogger
    {
        public const string FileName = "run_log.csv";

        public static string FilePath(string dir) => Path.Combine(dir, FileName);

        public static string Header(CelestialTable c, UpgradeTable u)
        {
            var sb = new StringBuilder("run,duration_s,income");
            for (int i = 0; i < c.tiers.Count; i++) sb.Append(",t").Append(c.tiers[i].tier);
            sb.Append(",ratio_vs_last");
            for (int i = 0; i < u.upgrades.Count; i++) sb.Append(",lvl_").Append(u.upgrades[i].id);
            sb.Append(",end_reason");
            return sb.ToString();
        }

        public static string Row(RunRecord r, CelestialTable c, UpgradeTable u)
        {
            var sb = new StringBuilder();
            sb.Append(r.run).Append(',').Append(Fmt.Sec(r.durationSec)).Append(',').Append(Fmt.Csv(r.income));
            for (int i = 0; i < c.tiers.Count; i++) sb.Append(',').Append(r.TierCount(c.tiers[i].tier));
            sb.Append(',').Append(r.HasRatio ? r.ratioVsLast.ToString("F3", CultureInfo.InvariantCulture) : string.Empty);
            for (int i = 0; i < u.upgrades.Count; i++) sb.Append(',').Append(MetaState.LevelIn(r.startLevels, u.upgrades[i].id));
            sb.Append(',').Append(r.endReason);
            return sb.ToString();
        }

        /// <summary>Appends a row. Returns the path the old file was moved to when the header changed, else null.</summary>
        public static string Append(string dir, RunRecord r, CelestialTable c, UpgradeTable u)
        {
            string rotated = null;
            try
            {
                Directory.CreateDirectory(dir);
                string path = FilePath(dir);
                string header = Header(c, u);
                if (File.Exists(path) && ReadFirstLine(path) != header)
                {
                    rotated = Path.Combine(dir, "run_log_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".csv");
                    File.Move(path, rotated);
                    Debug.Log("[RunLog] header changed; previous log moved to " + rotated);
                }
                bool exists = File.Exists(path);
                using (var w = new StreamWriter(path, true, new UTF8Encoding(false)))
                {
                    if (!exists) w.WriteLine(header);
                    w.WriteLine(Row(r, c, u));
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("run_log.csv write failed: " + e.Message);
            }
            return rotated;
        }

        public static string Summary(RunRecord r)
        {
            string ratio = r.HasRatio ? Fmt.Mult(r.ratioVsLast) : "-";
            var sb = new StringBuilder();
            sb.Append("[Run ").Append(r.run).Append("] ").Append(Fmt.Sec(r.durationSec)).Append("s income=").Append(Fmt.Num(r.income))
              .Append(" planets=[");
            bool first = true;
            if (r.tierCounts != null)
            {
                for (int i = 0; i < r.tierCounts.Length; i++)
                {
                    if (r.tierCounts[i] == 0) continue;
                    if (!first) sb.Append(' ');
                    sb.Append('t').Append(i + 1).Append('=').Append(r.tierCounts[i]);
                    first = false;
                }
            }
            sb.Append("] ratio=").Append(ratio).Append(" maxTier=").Append(r.unlockedMaxTier)
              .Append(" levels=[").Append(LevelsText(r.startLevels)).Append("] end=").Append(r.endReason);
            return sb.ToString();
        }

        /// <summary>"id=level" pairs for non-zero levels.</summary>
        public static string LevelsText(List<UpgradeLevel> levels)
        {
            if (levels == null) return string.Empty;
            var sb = new StringBuilder();
            for (int i = 0; i < levels.Count; i++)
            {
                if (levels[i].level == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(levels[i].id).Append('=').Append(levels[i].level);
            }
            return sb.ToString();
        }

        static string ReadFirstLine(string path)
        {
            using (var r = new StreamReader(path, new UTF8Encoding(false)))
                return r.ReadLine();
        }
    }
}

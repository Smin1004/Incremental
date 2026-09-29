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
    /// Columns: run, duration_s, income, t1..tN (from the celestial table), ratio_vs_last, max_tier, node_levels
    /// (sum of node levels at run start), the effective stats at run start (12 §3: more useful for balancing than node
    /// levels), end_reason. If the existing file has a different header it is renamed to run_log_&lt;time&gt;.csv and a
    /// new file is started.
    /// </summary>
    public static class RunLogger
    {
        public const string FileName = "run_log.csv";

        public static string FilePath(string dir) => Path.Combine(dir, FileName);

        /// <summary>Effective stat columns, in EffectiveStats field order.</summary>
        public static readonly string[] StatColumns =
        {
            "spawn_rate", "pull_accel", "sale_mult", "stamina_max", "threshold_mult", "dust_cap", "dust_mass",
            "gravity_radius", "stamina_idle_drain", "stamina_hold_drain", "dust_initial", "dust_mass_avg",
        };

        /// <summary>
        /// With <paramref name="p"/> one more column per dust kind holds its share of spawned dust (20 §4): those and
        /// dust_mass_avg are what the balance pass reads to check the per-ring growth of the average dust mass.
        /// </summary>
        public static string Header(CelestialTable c, GameParams p = null)
        {
            var sb = new StringBuilder("run,duration_s,income");
            for (int i = 0; i < c.tiers.Count; i++) sb.Append(",t").Append(c.tiers[i].tier);
            // 20 §5: fragments are counted apart from the bodies made from held mass, and their income is its own column.
            for (int i = 0; i < c.tiers.Count; i++) sb.Append(",f").Append(c.tiers[i].tier);
            sb.Append(",frag_income");
            sb.Append(",ratio_vs_last,max_tier,node_levels");
            for (int i = 0; i < StatColumns.Length; i++) sb.Append(',').Append(StatColumns[i]);
            if (p != null)
                for (int k = 1; k < p.DustKindCount; k++) sb.Append(",p_").Append(p.DustKind(k).id);
            sb.Append(",end_reason");
            return sb.ToString();
        }

        public static string Row(RunRecord r, CelestialTable c, GameParams p = null)
        {
            var sb = new StringBuilder();
            sb.Append(r.run).Append(',').Append(Fmt.Sec(r.durationSec)).Append(',').Append(Fmt.Csv(r.income));
            for (int i = 0; i < c.tiers.Count; i++) sb.Append(',').Append(r.DirectCount(c.tiers[i].tier));
            for (int i = 0; i < c.tiers.Count; i++) sb.Append(',').Append(r.FragmentCount(c.tiers[i].tier));
            sb.Append(',').Append(Fmt.Csv(r.fragmentIncome));
            sb.Append(',').Append(r.HasRatio ? r.ratioVsLast.ToString("F3", CultureInfo.InvariantCulture) : string.Empty);
            sb.Append(',').Append(r.unlockedMaxTier).Append(',').Append(TotalLevels(r.startLevels));
            var s = r.startStats;
            foreach (double v in new[] { s.spawnRate, s.pullAccel, s.saleMult, s.staminaMax, s.thresholdMult, s.dustCap, s.dustMass,
                         s.gravityRadius, s.staminaIdleDrain, s.staminaHoldDrain, s.dustInitial, s.dustMassAvg })
                sb.Append(',').Append(Fmt.Csv(v));
            if (p != null)
                for (int k = 1; k < p.DustKindCount; k++) sb.Append(',').Append(Fmt.Csv(s.KindChance(k)));
            sb.Append(',').Append(r.endReason);
            return sb.ToString();
        }

        static int TotalLevels(List<UpgradeLevel> levels)
        {
            int sum = 0;
            if (levels != null) foreach (var l in levels) sum += l.level;
            return sum;
        }

        /// <summary>Appends a row. Returns the path the old file was moved to when the header changed, else null.</summary>
        public static string Append(string dir, RunRecord r, CelestialTable c, GameParams p = null)
        {
            string rotated = null;
            try
            {
                Directory.CreateDirectory(dir);
                string path = FilePath(dir);
                string header = Header(c, p);
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
                    w.WriteLine(Row(r, c, p));
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
            sb.Append(']');
            if (r.fragmentIncome > 0)
                sb.Append(" frag=").Append(Fmt.Num(r.fragmentIncome)).Append(" (")
                  .Append((r.income > 0 ? r.fragmentIncome / r.income * 100.0 : 0).ToString("F1")).Append("%)");
            sb.Append(" ratio=").Append(ratio).Append(" maxTier=").Append(r.unlockedMaxTier)
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

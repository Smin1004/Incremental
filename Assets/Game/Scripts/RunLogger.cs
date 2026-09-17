using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Incremental
{
    public sealed class RunRecord
    {
        public int run;
        public double durationSec;
        public double income;
        public int[] tierCounts;
        /// <summary>NaN for the first run (or when the last run had no income).</summary>
        public double ratioVsLast;
        /// <summary>Upgrade levels at run start, indexed by UpgradeId.</summary>
        public int[] startLevels;
    }

    /// <summary>Appends one CSV row per run to persistentDataPath/run_log.csv and builds the console summary.</summary>
    public static class RunLogger
    {
        public const string FileName = "run_log.csv";
        public const string Header = "run,duration_s,income,t1,t2,t3,ratio_vs_last,lvl_spawn_rate,lvl_pull_accel,lvl_sale_mult,lvl_stamina_max,lvl_threshold_mult";

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static void Append(RunRecord r)
        {
            try
            {
                bool exists = File.Exists(FilePath);
                using (var w = new StreamWriter(FilePath, true, new UTF8Encoding(false)))
                {
                    if (!exists) w.WriteLine(Header);
                    w.WriteLine(Row(r));
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("run_log.csv write failed: " + e.Message);
            }
        }

        public static string Row(RunRecord r)
        {
            var sb = new StringBuilder();
            sb.Append(r.run).Append(',').Append(Fmt.Sec(r.durationSec)).Append(',').Append(Fmt.Csv(r.income));
            for (int t = 0; t < 3; t++) sb.Append(',').Append(Tier(r, t));
            sb.Append(',').Append(double.IsNaN(r.ratioVsLast) ? string.Empty : r.ratioVsLast.ToString("F3", CultureInfo.InvariantCulture));
            for (int i = 0; i < UpgradeTable.UpgradeCount; i++)
                sb.Append(',').Append(r.startLevels != null && i < r.startLevels.Length ? r.startLevels[i] : 0);
            return sb.ToString();
        }

        public static string Summary(RunRecord r)
        {
            string ratio = double.IsNaN(r.ratioVsLast) ? "-" : Fmt.Mult(r.ratioVsLast);
            string levels = r.startLevels != null ? string.Join(",", r.startLevels) : string.Empty;
            return $"[Run {r.run}] {Fmt.Sec(r.durationSec)}s income={Fmt.Int(r.income)} t1={Tier(r, 0)} t2={Tier(r, 1)} t3={Tier(r, 2)} ratio={ratio} levels=[{levels}]";
        }

        static int Tier(RunRecord r, int idx) => r.tierCounts != null && idx < r.tierCounts.Length ? r.tierCounts[idx] : 0;
    }
}

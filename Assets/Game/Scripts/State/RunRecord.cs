using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Why a run ended. Stored as a string in the run history and run_log.csv.</summary>
    public static class EndReason
    {
        public const string Stamina = "stamina";
        public const string Quit = "quit";
        public const string Crash = "crash";
    }

    /// <summary>One finished run. Raised with GameRoot.RunEnded and written to run_log.csv.</summary>
    [Serializable]
    public sealed class RunRecord
    {
        public int run;
        public double durationSec;
        public double income;
        /// <summary>Index = tier - 1.</summary>
        public int[] tierCounts = new int[0];
        /// <summary>Income / last run income. -1 when there is no ratio (first run, or last run had no income).</summary>
        public double ratioVsLast = -1;
        /// <summary>Upgrade levels at run start, by id.</summary>
        public List<UpgradeLevel> startLevels = new List<UpgradeLevel>();
        /// <summary>Highest unlocked tier during the run.</summary>
        public int unlockedMaxTier;
        public string endReason = EndReason.Stamina;

        public bool HasRatio => ratioVsLast >= 0 && !double.IsNaN(ratioVsLast) && !double.IsInfinity(ratioVsLast);

        public int TierCount(int tier)
        {
            int idx = tier - 1;
            return tierCounts != null && idx >= 0 && idx < tierCounts.Length ? tierCounts[idx] : 0;
        }
    }
}

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
        /// <summary>
        /// Income / income of the most recent stamina-ended run. -1 when there is no ratio: quit and crash runs, the first
        /// full run, or a previous full run without income (12 §10-3).
        /// </summary>
        public double ratioVsLast = -1;
        /// <summary>Node levels at run start, by node id.</summary>
        public List<UpgradeLevel> startLevels = new List<UpgradeLevel>();
        /// <summary>Effective stats at run start (run_log.csv records these instead of node levels).</summary>
        public EffectiveStats startStats;
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

using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Why a run ended. Stored as a string in the run history and run_log.csv.</summary>
    public static class EndReason
    {
        /// <summary>28 §2: the run's time ran out. Saves before v4 wrote "stamina" for the same thing (SaveMigration).</summary>
        public const string TimeUp = "time";
        /// <summary>The v3 name of <see cref="TimeUp"/>, still in old run_log.csv rows.</summary>
        public const string LegacyStamina = "stamina";
        public const string Quit = "quit";
        public const string Crash = "crash";
        /// <summary>The first blue giant ended the run (15 §4.2). Counts as a full run, like the time running out.</summary>
        public const string Ending = "ending";
    }

    /// <summary>One finished run. Raised with GameRoot.RunEnded and written to run_log.csv.</summary>
    [Serializable]
    public sealed class RunRecord
    {
        public int run;
        public double durationSec;
        public double income;
        /// <summary>Bodies made from the held mass, index = tier - 1. Fragments are kept apart in <see cref="fragmentCounts"/>.</summary>
        public int[] tierCounts = new int[0];
        /// <summary>
        /// Fragments dropped this run, index = tier - 1 (20 §5). The result panel and the solar system show the two added
        /// up (<see cref="TierCount"/>); the save and run_log.csv keep them apart, so a later rule can count only the
        /// bodies the player chose to make (19 §6).
        /// </summary>
        public int[] fragmentCounts = new int[0];
        /// <summary>Part of <see cref="income"/> that came from fragments (20 §5: it has to stay under 5% of a run).</summary>
        public double fragmentIncome;
        /// <summary>
        /// Income / income of the most recent run that ran out of time. -1 when there is no ratio: quit and crash runs, the first
        /// full run, or a previous full run without income (12 §10-3).
        /// </summary>
        public double ratioVsLast = -1;
        /// <summary>Node levels at run start, by node id.</summary>
        public List<UpgradeLevel> startLevels = new List<UpgradeLevel>();
        /// <summary>Effective stats at run start (run_log.csv records these instead of node levels).</summary>
        public EffectiveStats startStats;
        /// <summary>Highest unlocked tier during the run.</summary>
        public int unlockedMaxTier;
        public string endReason = EndReason.TimeUp;

        public bool HasRatio => ratioVsLast >= 0 && !double.IsNaN(ratioVsLast) && !double.IsInfinity(ratioVsLast);

        /// <summary>Bodies of that tier on screen this run: the ones made from held mass plus the fragments (20 §5).</summary>
        public int TierCount(int tier) => DirectCount(tier) + FragmentCount(tier);

        /// <summary>Bodies of that tier made from the held mass, fragments not counted.</summary>
        public int DirectCount(int tier) => At(tierCounts, tier);

        /// <summary>Fragments of that tier dropped this run (20 §5).</summary>
        public int FragmentCount(int tier) => At(fragmentCounts, tier);

        static int At(int[] counts, int tier)
        {
            int idx = tier - 1;
            return counts != null && idx >= 0 && idx < counts.Length ? counts[idx] : 0;
        }
    }
}

using System;

namespace Incremental
{
    /// <summary>State of the current run. Discarded when the run ends.</summary>
    [Serializable]
    public sealed class RunState
    {
        public double mass;
        public double stamina;
        public double runIncome;
        /// <summary>Bodies made from the held mass. Index = tier - 1. Fragments are counted apart (20 §5).</summary>
        public int[] planetCounts;
        /// <summary>Fragments dropped while holding (20 §5). Index = tier - 1.</summary>
        public int[] fragmentCounts;
        /// <summary>Part of <see cref="runIncome"/> that came from fragments, for the 20 §5 "under 5% of a run" check.</summary>
        public double fragmentIncome;
        /// <summary>
        /// Highest tier whose threshold the held mass has crossed in this hold (20 §5): a fragment is rolled for once per
        /// threshold, the moment it is crossed. Back to 0 whenever the mass is spent or scattered.
        /// </summary>
        public int crossedTier;
        public double elapsed;
        public bool holding;

        public RunState(int tierCount)
        {
            planetCounts = new int[Math.Max(tierCount, 1)];
            fragmentCounts = new int[Math.Max(tierCount, 1)];
        }
    }
}

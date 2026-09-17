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
        /// <summary>Index = tier - 1.</summary>
        public int[] planetCounts;
        public double elapsed;
        public bool holding;

        public RunState(int tierCount)
        {
            planetCounts = new int[Math.Max(tierCount, 1)];
        }
    }
}

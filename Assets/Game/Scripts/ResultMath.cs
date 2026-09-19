using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Pure helpers behind the result screen's growth highlights (14 §7).</summary>
    public static class ResultMath
    {
        /// <summary>
        /// The run this one is compared with: the most recent stamina-ended run before it, the same run the ratio uses
        /// (12 §10-3). Null when there is none (first full run).
        /// </summary>
        public static RunRecord ComparisonRun(List<RunRecord> history, RunRecord rec)
        {
            if (history == null || rec == null) return null;
            int start = history.IndexOf(rec);
            if (start < 0) start = history.Count;
            for (int i = start - 1; i >= 0; i--)
            {
                var h = history[i];
                if (h != null && h.run < rec.run && Session.IsFullRun(h.endReason)) return h;
            }
            return null;
        }

        /// <summary>Change in the count of a tier against the comparison run (0 without one).</summary>
        public static int Delta(RunRecord rec, RunRecord comparison, int tier) =>
            comparison == null ? 0 : rec.TierCount(tier) - comparison.TierCount(tier);

        /// <summary>The change the result panel shows: increases only; a decrease shows nothing (15 §3).</summary>
        public static int ShownIncrease(RunRecord rec, RunRecord comparison, int tier) => System.Math.Max(0, Delta(rec, comparison, tier));

        /// <summary>
        /// True when this run made the tier for the first time ever: made now, and the lifetime total before the run
        /// (totalPlanets after it minus this run's count) was 0. Valid for the run just applied to <paramref name="meta"/>.
        /// </summary>
        public static bool IsNewTier(MetaState meta, RunRecord rec, int tier)
        {
            int made = rec != null ? rec.TierCount(tier) : 0;
            if (made <= 0 || meta?.totalPlanets == null) return false;
            int idx = tier - 1;
            int after = idx >= 0 && idx < meta.totalPlanets.Length ? meta.totalPlanets[idx] : 0;
            return after - made <= 0;
        }
    }
}

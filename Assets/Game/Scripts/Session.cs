using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// Run bookkeeping as pure functions over MetaState: building the run record, applying a finished run
    /// (currency, best, totals, history) and closing a pending run left behind by a killed game.
    /// Every way a run can end (stamina, ending, quit, crash) goes through <see cref="ApplyRunEnd"/>.
    /// Stamina and ending runs are "full" runs (15 §4.2): quit and crash runs are recorded and paid, but they get no ratio
    /// and do not move the ratio base (lastRunIncome) or the best income (12 §10-3).
    /// </summary>
    public static class Session
    {
        public static bool IsFullRun(string endReason) => endReason == EndReason.Stamina || endReason == EndReason.Ending;

        /// <summary>
        /// Builds the record of a finished run. <paramref name="income"/> is the raw run income (planet incomes keep their
        /// fractions); the record holds it rounded up once, which is what the currency, the ratio and the log get (14 §8).
        /// </summary>
        public static RunRecord MakeRecord(MetaState meta, int run, double durationSec, double income, int[] tierCounts,
            List<UpgradeLevel> startLevels, EffectiveStats startStats, string endReason,
            int[] fragmentCounts = null, double fragmentIncome = 0)
        {
            income = Stats.RunIncome(income);
            return new RunRecord
            {
                run = run,
                durationSec = durationSec,
                income = income,
                tierCounts = tierCounts != null ? (int[])tierCounts.Clone() : new int[0],
                fragmentCounts = fragmentCounts != null ? (int[])fragmentCounts.Clone() : new int[0],
                fragmentIncome = fragmentIncome,
                ratioVsLast = IsFullRun(endReason) && meta.lastRunIncome > 0 ? income / meta.lastRunIncome : -1.0,
                startLevels = MetaState.CopyLevels(startLevels),
                startStats = startStats,
                unlockedMaxTier = meta.unlockedMaxTier,
                endReason = endReason,
            };
        }

        /// <summary>
        /// Income goes into currency; totals and history are updated; ratio base and best only for full runs. The first
        /// ending run is remembered (endingRun) with its sequence still unseen (15 §4.5).
        /// </summary>
        public static void ApplyRunEnd(MetaState meta, RunRecord rec)
        {
            meta.currency += rec.income;
            if (rec.endReason == EndReason.Ending && meta.endingRun == 0)
            {
                meta.endingRun = rec.run;
                meta.endingSeen = false;
            }
            if (IsFullRun(rec.endReason))
            {
                meta.lastRunIncome = rec.income;
                if (rec.income > meta.bestRunIncome) meta.bestRunIncome = rec.income;
            }
            meta.totalIncome += rec.income;
            meta.totalPlayTimeSec += rec.durationSec;
            if (rec.run > meta.runCount) meta.runCount = rec.run;

            int n = rec.tierCounts != null ? rec.tierCounts.Length : 0;
            if (meta.totalPlanets == null) meta.totalPlanets = new int[0];
            if (meta.totalPlanets.Length < n) Array.Resize(ref meta.totalPlanets, n);
            for (int i = 0; i < n; i++)
            {
                meta.totalPlanets[i] += rec.tierCounts[i];
                // Only bodies the player made from held mass raise the highest tier: a fragment is a chip off one of them
                // and must not move the central star stage or the ending on its own (20 §5).
                if (rec.tierCounts[i] > 0 && i + 1 > meta.highestTierCreated) meta.highestTierCreated = i + 1;
            }
            int f = rec.fragmentCounts != null ? rec.fragmentCounts.Length : 0;
            if (meta.totalFragments == null) meta.totalFragments = new int[0];
            if (meta.totalFragments.Length < f) Array.Resize(ref meta.totalFragments, f);
            for (int i = 0; i < f; i++) meta.totalFragments[i] += rec.fragmentCounts[i];

            if (meta.runHistory == null) meta.runHistory = new List<RunRecord>();
            meta.runHistory.Add(rec);
        }

        public static PendingRun MakePending(int run, double elapsed, double income, int[] tierCounts,
            List<UpgradeLevel> startLevels, EffectiveStats startStats, bool ending = false,
            int[] fragmentCounts = null, double fragmentIncome = 0)
        {
            return new PendingRun
            {
                active = true,
                ending = ending,
                run = run,
                elapsed = elapsed,
                income = income,
                tierCounts = tierCounts != null ? (int[])tierCounts.Clone() : new int[0],
                fragmentCounts = fragmentCounts != null ? (int[])fragmentCounts.Clone() : new int[0],
                fragmentIncome = fragmentIncome,
                startLevels = MetaState.CopyLevels(startLevels),
                startStats = startStats,
            };
        }

        /// <summary>
        /// Closes the pending run of a loaded save (the game was killed mid-run): its income is kept and it is recorded
        /// with <paramref name="endReason"/>, or as an ending run when it was killed during its ending (15 §4.5).
        /// Returns the record, or null when there was no pending run.
        /// </summary>
        public static RunRecord FinalizePending(SaveData data, string endReason)
        {
            var p = data.pending;
            if (p == null || !p.active) return null;
            if (p.ending) endReason = EndReason.Ending;
            var rec = MakeRecord(data.meta, p.run, p.elapsed, p.income, p.tierCounts, p.startLevels, p.startStats, endReason,
                p.fragmentCounts, p.fragmentIncome);
            ApplyRunEnd(data.meta, rec);
            data.pending = new PendingRun();
            return rec;
        }
    }
}

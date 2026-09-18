using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// Run bookkeeping as pure functions over MetaState: building the run record, applying a finished run
    /// (currency, best, totals, history) and closing a pending run left behind by a killed game.
    /// Every way a run can end (stamina, quit, crash) goes through <see cref="ApplyRunEnd"/>.
    /// Only stamina-ended runs are "full" runs: quit and crash runs are recorded and paid, but they get no ratio and
    /// do not move the ratio base (lastRunIncome) or the best income (12 §10-3).
    /// </summary>
    public static class Session
    {
        public static bool IsFullRun(string endReason) => endReason == EndReason.Stamina;

        public static RunRecord MakeRecord(MetaState meta, int run, double durationSec, double income, int[] tierCounts,
            List<UpgradeLevel> startLevels, EffectiveStats startStats, string endReason)
        {
            return new RunRecord
            {
                run = run,
                durationSec = durationSec,
                income = income,
                tierCounts = tierCounts != null ? (int[])tierCounts.Clone() : new int[0],
                ratioVsLast = IsFullRun(endReason) && meta.lastRunIncome > 0 ? income / meta.lastRunIncome : -1.0,
                startLevels = MetaState.CopyLevels(startLevels),
                startStats = startStats,
                unlockedMaxTier = meta.unlockedMaxTier,
                endReason = endReason,
            };
        }

        /// <summary>Income goes into currency; totals and history are updated; ratio base and best only for full runs.</summary>
        public static void ApplyRunEnd(MetaState meta, RunRecord rec)
        {
            meta.currency += rec.income;
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
                if (rec.tierCounts[i] > 0 && i + 1 > meta.highestTierCreated) meta.highestTierCreated = i + 1;
            }

            if (meta.runHistory == null) meta.runHistory = new List<RunRecord>();
            meta.runHistory.Add(rec);
        }

        public static PendingRun MakePending(int run, double elapsed, double income, int[] tierCounts,
            List<UpgradeLevel> startLevels, EffectiveStats startStats)
        {
            return new PendingRun
            {
                active = true,
                run = run,
                elapsed = elapsed,
                income = income,
                tierCounts = tierCounts != null ? (int[])tierCounts.Clone() : new int[0],
                startLevels = MetaState.CopyLevels(startLevels),
                startStats = startStats,
            };
        }

        /// <summary>
        /// Closes the pending run of a loaded save (the game was killed mid-run): its income is kept and it is recorded
        /// with <paramref name="endReason"/>. Returns the record, or null when there was no pending run.
        /// </summary>
        public static RunRecord FinalizePending(SaveData data, string endReason)
        {
            var p = data.pending;
            if (p == null || !p.active) return null;
            var rec = MakeRecord(data.meta, p.run, p.elapsed, p.income, p.tierCounts, p.startLevels, p.startStats, endReason);
            ApplyRunEnd(data.meta, rec);
            data.pending = new PendingRun();
            return rec;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// Run bookkeeping as pure functions over MetaState: building the run record, applying a finished run
    /// (currency, best, totals, history) and closing a pending run left behind by a killed game.
    /// Every way a run can end (time, ending, quit, crash) goes through <see cref="ApplyRunEnd"/>.
    /// Time and ending runs are "full" runs (15 §4.2): quit and crash runs are recorded and paid, but they get no ratio
    /// and do not move the ratio base (lastRunIncome) or the best income (12 §10-3).
    /// 20 §6 (W5-3): an ending run no longer counts for the ratio or the best either. It is cut short by the supernova,
    /// so measuring the next run against it would punish the player for reaching the ending. Its income and its numbers
    /// still go into the currency, the totals and the history.
    /// </summary>
    public static class Session
    {
        /// <summary>A run the player played to the end: the time ran out, or the ending (15 §4.2). Drives the comparison run.</summary>
        public static bool IsFullRun(string endReason) => endReason == EndReason.TimeUp || endReason == EndReason.Ending;

        /// <summary>
        /// A run that sets the ratio base and the best income: the time ran out (20 §6 W5-3). Quit, crash and ending runs
        /// are all left out.
        /// </summary>
        public static bool CountsForBest(string endReason) => endReason == EndReason.TimeUp;

        /// <summary>
        /// 28 §2: runs the run's clock down by <paramref name="dt"/>, the same whether the player holds or not. True when
        /// the time is up (it stops at 0), which ends the run.
        /// </summary>
        public static bool TickClock(RunState run, double dt)
        {
            run.timeLeft -= dt;
            if (run.timeLeft > 0) return false;
            run.timeLeft = 0;
            return true;
        }

        /// <summary>
        /// 20 §6 (W5-2): the system the ending sequence shows. An ending run can be over in two seconds, which would
        /// leave the final screen almost empty, so whichever of the two runs made more bodies is used.
        /// </summary>
        public static RunRecord RicherSystem(RunRecord a, RunRecord b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return TotalBodies(b) > TotalBodies(a) ? b : a;
        }

        /// <summary>Bodies of every tier in a run's result system, fragments included (20 §5).</summary>
        public static int TotalBodies(RunRecord r)
        {
            if (r == null) return 0;
            int n = 0;
            if (r.tierCounts != null) foreach (var c in r.tierCounts) n += c;
            if (r.fragmentCounts != null) foreach (var c in r.fragmentCounts) n += c;
            return n;
        }

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
                ratioVsLast = CountsForBest(endReason) && meta.lastRunIncome > 0 ? income / meta.lastRunIncome : -1.0,
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
            if (CountsForBest(rec.endReason))
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

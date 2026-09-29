using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Upgrade level keyed by upgrade id. Ids missing from the table are kept (and ignored).</summary>
    [Serializable]
    public sealed class UpgradeLevel
    {
        public string id;
        public int level;

        public UpgradeLevel() { }
        public UpgradeLevel(string id, int level) { this.id = id; this.level = level; }
    }

    /// <summary>
    /// State that persists between runs. Serialized as-is inside save.json (JsonUtility), so every field here is part of
    /// the save format. Run statistics (totals, history) are the raw data for the balance pass.
    /// </summary>
    [Serializable]
    public sealed class MetaState
    {
        public double currency;
        /// <summary>Skill tree node levels by node id (gates included: level 1 = bought).</summary>
        public List<UpgradeLevel> upgradeLevels = new List<UpgradeLevel>();
        /// <summary>Derived from the gate levels (Shop.RecomputeUnlockedMaxTier); stored for readability.</summary>
        public int unlockedMaxTier = 1;
        public int runCount;
        /// <summary>Income of the most recent stamina-ended run (the base of the next ratio). quit / crash runs do not count.</summary>
        public double lastRunIncome;
        /// <summary>Best income of a stamina-ended run. quit / crash runs do not count.</summary>
        public double bestRunIncome;

        // Run statistics
        public double totalIncome;
        /// <summary>Sum of run lengths, seconds.</summary>
        public double totalPlayTimeSec;
        /// <summary>Planets created over all runs. Index = tier - 1. Fragments are counted apart (20 §5).</summary>
        public int[] totalPlanets = new int[0];
        /// <summary>Fragments dropped over all runs. Index = tier - 1 (20 §5).</summary>
        public int[] totalFragments = new int[0];
        /// <summary>0 until the first planet.</summary>
        public int highestTierCreated;
        /// <summary>Every finished run, oldest first.</summary>
        public List<RunRecord> runHistory = new List<RunRecord>();

        // Ending (15 §4.5). Old saves read the defaults (0, false): JsonUtility keeps field defaults, so no version bump.
        /// <summary>Run number of the run that made the first blue giant and ended in the supernova; 0 = not reached.</summary>
        public int endingRun;
        /// <summary>
        /// The ending sequence was watched to the end or skipped. While endingRun &gt; 0 and this is false (the game was
        /// closed during the sequence), the next launch plays the sequence again instead of the result screen.
        /// </summary>
        public bool endingSeen;

        /// <summary>The ending run's record, or null.</summary>
        public RunRecord EndingRecord => endingRun > 0 && runHistory != null ? runHistory.Find(r => r != null && r.run == endingRun) : null;

        public int GetLevel(string id)
        {
            for (int i = 0; i < upgradeLevels.Count; i++)
                if (upgradeLevels[i].id == id) return upgradeLevels[i].level;
            return 0;
        }

        public void SetLevel(string id, int level)
        {
            for (int i = 0; i < upgradeLevels.Count; i++)
            {
                if (upgradeLevels[i].id != id) continue;
                upgradeLevels[i].level = level;
                return;
            }
            upgradeLevels.Add(new UpgradeLevel(id, level));
        }

        /// <summary>Deep copy of the level list (run start snapshot).</summary>
        public List<UpgradeLevel> CopyLevels() => CopyLevels(upgradeLevels);

        public static List<UpgradeLevel> CopyLevels(List<UpgradeLevel> levels)
        {
            var copy = new List<UpgradeLevel>(levels != null ? levels.Count : 0);
            if (levels == null) return copy;
            for (int i = 0; i < levels.Count; i++) copy.Add(new UpgradeLevel(levels[i].id, levels[i].level));
            return copy;
        }

        public static int LevelIn(List<UpgradeLevel> levels, string id)
        {
            if (levels == null) return 0;
            for (int i = 0; i < levels.Count; i++)
                if (levels[i].id == id) return levels[i].level;
            return 0;
        }

        /// <summary>Most recent finished run, or null.</summary>
        public RunRecord LastRun => runHistory != null && runHistory.Count > 0 ? runHistory[runHistory.Count - 1] : null;

        /// <summary>
        /// The run behind <see cref="bestRunIncome"/>: the stamina-ended run with the highest income, latest one on a tie
        /// (20 §6 W5-2 needs its system). Null when there is none yet.
        /// </summary>
        public RunRecord BestRun
        {
            get
            {
                RunRecord best = null;
                if (runHistory == null) return null;
                foreach (var r in runHistory)
                    if (r != null && Session.CountsForBest(r.endReason) && (best == null || r.income >= best.income)) best = r;
                return best;
            }
        }

        /// <summary>Bodies of that tier over all runs, fragments included (20 §5). Tier is 1-based.</summary>
        public int TotalBodies(int tier)
        {
            int idx = tier - 1;
            int n = 0;
            if (totalPlanets != null && idx >= 0 && idx < totalPlanets.Length) n += totalPlanets[idx];
            if (totalFragments != null && idx >= 0 && idx < totalFragments.Length) n += totalFragments[idx];
            return n;
        }
    }
}

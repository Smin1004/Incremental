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
        public List<UpgradeLevel> upgradeLevels = new List<UpgradeLevel>();
        public int unlockedMaxTier = 1;
        public int runCount;
        public double lastRunIncome;
        public double bestRunIncome;

        // Run statistics
        public double totalIncome;
        /// <summary>Sum of run lengths, seconds.</summary>
        public double totalPlayTimeSec;
        /// <summary>Planets created over all runs. Index = tier - 1.</summary>
        public int[] totalPlanets = new int[0];
        /// <summary>0 until the first planet.</summary>
        public int highestTierCreated;
        /// <summary>Every finished run, oldest first.</summary>
        public List<RunRecord> runHistory = new List<RunRecord>();

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
    }
}

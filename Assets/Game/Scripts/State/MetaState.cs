using System;

namespace Incremental
{
    /// <summary>
    /// State that persists between runs. Not saved yet, but kept in one place with a version field
    /// so it can be serialized as-is later.
    /// </summary>
    [Serializable]
    public sealed class MetaState
    {
        public int version = 1;
        public double currency;
        public int[] upgradeLevels = new int[UpgradeTable.UpgradeCount];
        public int unlockedMaxTier = 1;
        public int runCount;
        public double lastRunIncome;
        public double bestRunIncome;

        public int GetLevel(UpgradeId id) => upgradeLevels[(int)id];
        public void SetLevel(UpgradeId id, int level) => upgradeLevels[(int)id] = level;
    }
}

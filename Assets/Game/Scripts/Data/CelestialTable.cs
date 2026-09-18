using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>One celestial tier. The display name lives in UIStrings under <c>tier.&lt;n&gt;</c>, not in the asset.</summary>
    [Serializable]
    public sealed class CelestialTier
    {
        public int tier;
        public double requiredMass;
        public double salePrice;
        public double sizePx;
        public Color color = Color.gray;
    }

    /// <summary>Celestial body tiers (10 §3.4). Tier numbers start at 1 and are contiguous and ascending.</summary>
    [CreateAssetMenu(fileName = "CelestialTable", menuName = "Incremental/Celestial Table")]
    public sealed class CelestialTable : ScriptableObject
    {
        public List<CelestialTier> tiers = new List<CelestialTier>();

        public int TierCount => tiers.Count;

        /// <summary>Highest tier number in the table (0 when empty).</summary>
        public int MaxTier => tiers.Count > 0 ? tiers[tiers.Count - 1].tier : 0;

        /// <summary>Returns the tier definition, or null if the tier does not exist.</summary>
        public CelestialTier Get(int tier)
        {
            for (int i = 0; i < tiers.Count; i++)
                if (tiers[i].tier == tier) return tiers[i];
            return null;
        }
    }
}

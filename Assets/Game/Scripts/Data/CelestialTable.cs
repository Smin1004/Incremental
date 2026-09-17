using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    [Serializable]
    public sealed class CelestialTier
    {
        public int tier;
        public string name;
        public double requiredMass;
        public double salePrice;
        public double sizePx;
        public Color color = Color.gray;
    }

    /// <summary>Celestial body tiers (spec §6). Tier numbers start at 1 and are contiguous and ascending.</summary>
    [CreateAssetMenu(fileName = "CelestialTable", menuName = "Incremental/Celestial Table")]
    public sealed class CelestialTable : ScriptableObject
    {
        public List<CelestialTier> tiers = new List<CelestialTier>();

        public int TierCount => tiers.Count;

        /// <summary>Returns the tier definition, or null if the tier does not exist.</summary>
        public CelestialTier Get(int tier)
        {
            for (int i = 0; i < tiers.Count; i++)
                if (tiers[i].tier == tier) return tiers[i];
            return null;
        }
    }
}

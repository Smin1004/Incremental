using System;
using System.Collections.Generic;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Stat ids known to <see cref="Stats.Compute"/>. A stat node raises exactly one of these.
    /// String keys: stat.&lt;id&gt;.name, stat.&lt;id&gt;.effect.
    /// </summary>
    public static class StatIds
    {
        public const string SpawnRate = "spawn_rate";
        public const string PullAccel = "pull_accel";
        public const string SaleMult = "sale_mult";
        public const string StaminaMax = "stamina_max";
        public const string ThresholdMult = "threshold_mult";
        public const string DustCap = "dust_cap";
        public const string DustMass = "dust_mass";
        public const string GravityRadius = "gravity_radius";
        public const string StaminaDrain = "stamina_drain";
        public const string StartBonus = "start_bonus";

        public static readonly string[] All =
        {
            SpawnRate, PullAccel, SaleMult, StaminaMax, ThresholdMult,
            DustCap, DustMass, GravityRadius, StaminaDrain, StartBonus,
        };

        public static bool IsKnown(string id) => Array.IndexOf(All, id) >= 0;
    }

    public enum NodeKind
    {
        /// <summary>Raises one stat (statId) by effectPerLevel per level.</summary>
        Stat = 0,
        /// <summary>Unlocks celestial tier <see cref="NodeDef.tier"/>. Bought in order; opens ring (tier).</summary>
        TierUnlock = 1,
    }

    /// <summary>One node of the skill tree (12 §3). Ring = minTier.</summary>
    [Serializable]
    public sealed class NodeDef
    {
        [Tooltip("Stable key for saves, logs and string keys, e.g. r1_spawn, gate_t2.")]
        public string id;
        public NodeKind kind;
        [Tooltip("Stat nodes: one of StatIds.")]
        public string statId;
        [Tooltip("Per level. Additive except dust_mass (compound: effect ^ level). Fraction stats: 0.2 = 20%.")]
        public double effectPerLevel;
        [Tooltip("0 = unlimited (ring 11 endless nodes only).")]
        public int maxLevel = 1;
        public double baseCost;
        public double growth = 1;
        [Tooltip("TierUnlock nodes: the tier this node unlocks.")]
        public int tier;
        [Tooltip("The ring this node belongs to. Needs unlockedMaxTier >= minTier. Ring 1 is open from the start.")]
        public int minTier = 1;
        [Tooltip("Any-of. Empty = next to the center, always met.")]
        public List<string> prereqs = new List<string>();
        [Tooltip("Placement angle on the ring (tree view). Radius comes from minTier.")]
        public float angleDeg;
        [Tooltip("Off: treated as if the node did not exist (not purchasable, owned levels have no effect).")]
        public bool enabled = true;

        public bool IsGate => kind == NodeKind.TierUnlock;
    }

    /// <summary>Skill tree nodes (12 §3) and the per-ring cost multiplier, the pacing lever of the balance pass (12 §7).</summary>
    [CreateAssetMenu(fileName = "NodeTable", menuName = "Incremental/Node Table")]
    public sealed class NodeTable : ScriptableObject
    {
        public List<NodeDef> nodes = new List<NodeDef>();
        [Tooltip("Index = ring (minTier). Missing entries count as 1. Multiplies every node cost in that ring.")]
        public double[] ringCostMult = new double[0];

        [NonSerialized] Dictionary<string, NodeDef> byId;
        [NonSerialized] List<NodeDef> byIdSource;
        [NonSerialized] int byIdCount = -1;

        public NodeDef Get(string id)
        {
            if (id == null) return null;
            if (byId == null || byIdSource != nodes || byIdCount != nodes.Count) Rebuild();
            return byId.TryGetValue(id, out var n) ? n : null;
        }

        public double RingCostMult(int ring) =>
            ringCostMult != null && ring >= 0 && ring < ringCostMult.Length ? ringCostMult[ring] : 1.0;

        /// <summary>The TierUnlock node for a tier, or null.</summary>
        public NodeDef Gate(int tier)
        {
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].IsGate && nodes[i].tier == tier) return nodes[i];
            return null;
        }

        /// <summary>Call after changing ids or replacing entries in <see cref="nodes"/> at runtime.</summary>
        public void Invalidate() => byId = null;

        void OnValidate() => Invalidate();

        void Rebuild()
        {
            byId = new Dictionary<string, NodeDef>();
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && !string.IsNullOrEmpty(nodes[i].id) && !byId.ContainsKey(nodes[i].id))
                    byId.Add(nodes[i].id, nodes[i]);
            byIdSource = nodes;
            byIdCount = nodes.Count;
        }
    }
}

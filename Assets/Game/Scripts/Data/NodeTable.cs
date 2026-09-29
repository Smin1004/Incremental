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
        /// <summary>
        /// 20 §4: raises the share of one dust kind, named by <see cref="NodeDef.dustKindId"/>. Percentage points per level,
        /// so two nodes of this stat only add up when they name the same kind.
        /// </summary>
        public const string DustChance = "dust_chance";
        /// <summary>
        /// 20 §5: turns fragments on and gives them their starting chance. It adds to <see cref="FragChance"/>; fragments
        /// happen while the two together are above 0.
        /// </summary>
        public const string FragUnlock = "frag_unlock";
        /// <summary>20 §5: added chance that crossing a tier threshold drops a fragment of that tier.</summary>
        public const string FragChance = "frag_chance";
        /// <summary>
        /// 22 §3: raises the top speed the gravity source follows the cursor at, as a share of GameParams.followSpeedPx
        /// (0.15 = +15%p of the base). Additive, so five nodes of +15%p make 1.75 × the base.
        /// </summary>
        public const string FollowSpeed = "follow_speed";
        /// <summary>
        /// 20 §5: moves the fragment tier cap one tier up per level. The cap is
        /// unlockedMaxTier − (GameParams.fragmentBaseOffset − this), never below tier 1.
        /// </summary>
        public const string FragOffset = "frag_offset";

        public static readonly string[] All =
        {
            SpawnRate, PullAccel, SaleMult, StaminaMax, ThresholdMult,
            DustCap, DustMass, GravityRadius, StaminaDrain, StartBonus,
            DustChance, FragUnlock, FragChance, FragOffset, FollowSpeed,
        };

        /// <summary>Stats that act on one dust kind and therefore need <see cref="NodeDef.dustKindId"/>.</summary>
        public static bool NeedsDustKind(string id) => id == DustChance;

        public static bool IsKnown(string id) => Array.IndexOf(All, id) >= 0;
    }

    public enum NodeKind
    {
        /// <summary>Raises one stat (statId) by effectPerLevel per level.</summary>
        Stat = 0,
        /// <summary>Unlocks celestial tier <see cref="NodeDef.tier"/>. Bought in order; opens ring (tier).</summary>
        TierUnlock = 1,
        /// <summary>
        /// 20 §4: lets the dust kind named by <see cref="NodeDef.dustKindId"/> start appearing, at its baseChance.
        /// One level; there is one of these per kind.
        /// </summary>
        DustKindUnlock = 2,
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
        [Tooltip("DustKindUnlock nodes and dust_chance stat nodes: the GameParams dust kind they act on (20 §4).")]
        public string dustKindId;
        [Tooltip("The ring this node belongs to. Needs unlockedMaxTier >= minTier. Ring 1 is open from the start.")]
        public int minTier = 1;
        [Tooltip("Any-of. Empty = next to the center, always met.")]
        public List<string> prereqs = new List<string>();
        [Tooltip("Placement angle on the ring (tree view). Radius comes from minTier.")]
        public float angleDeg;
        [Tooltip("Off: treated as if the node did not exist (not purchasable, owned levels have no effect).")]
        public bool enabled = true;

        public bool IsGate => kind == NodeKind.TierUnlock;
        /// <summary>20 §4: this node makes a dust kind start appearing.</summary>
        public bool IsDustUnlock => kind == NodeKind.DustKindUnlock;
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

        /// <summary>The DustKindUnlock node of a dust kind, or null (20 §4).</summary>
        public NodeDef DustUnlock(string dustKindId)
        {
            if (dustKindId == null) return null;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].IsDustUnlock && nodes[i].dustKindId == dustKindId) return nodes[i];
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

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
        public const string ThresholdMult = "threshold_mult";
        public const string DustCap = "dust_cap";
        public const string DustMass = "dust_mass";
        public const string GravityRadius = "gravity_radius";
        /// <summary>
        /// 28 §2: seconds added to the run (D21, time-based runs). Replaced stamina_max and stamina_drain; the nodes kept
        /// their ids (save keys), only the stat changed.
        /// </summary>
        public const string RunTime = "run_time";
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
        /// <summary>
        /// 29 §3.3: raises the top speed of the dust the gravity pulls in (GameParams dustMaxSpeed), as a share
        /// (0.05 = +5%). 13th pass step 1: the nodes exist, the effect comes in step 2.
        /// </summary>
        public const string DustMaxSpeed = "dust_max_speed";
        /// <summary>
        /// 29 §10.7 S4 무르익는 하늘 (time category): the dust spawn runs x (1 + sum x min(1, run seconds / GameParams.spawnRampSec)),
        /// so it grows as the run goes on. A share (0.04 = +4% once the ramp is full).
        /// </summary>
        public const string SpawnRamp = "spawn_ramp";

        /// <summary>
        /// 24 §8: the stats of the ability axis. Each acts on the ability named by <see cref="NodeDef.abilityId"/>, the
        /// way dust_chance acts on one dust kind, so a new ability needs no stat of its own. This one raises the
        /// ability's strength by a share (0.25 = +25%).
        /// </summary>
        public const string AbilityPower = "ability_power";
        /// <summary>24 §8: the share taken off the ability's cooldown (0.1 = −10%).</summary>
        public const string AbilityCooldown = "ability_cooldown";
        /// <summary>24 §8: one more of whatever the ability has a number of (satellites).</summary>
        public const string AbilityCount = "ability_count";
        /// <summary>24 §8: percentage points added to the ability's amount (0.03 = +3%p of the dust cap a meteor sheds).</summary>
        public const string AbilityAmount = "ability_amount";
        /// <summary>
        /// 24 §16.1: one step further out for the ability's reach per level. What a step is worth is the ability's own
        /// data, and so is what the last level means (수축: the whole screen); see <see cref="AbilityLevel.rangeMaxed"/>.
        /// </summary>
        public const string AbilityRange = "ability_range";
        /// <summary>24 §19.1: seconds added to how long the ability lasts (블랙홀: +1 s per level).</summary>
        public const string AbilityDuration = "ability_duration";

        public static readonly string[] All =
        {
            SpawnRate, PullAccel, SaleMult, RunTime, ThresholdMult,
            DustCap, DustMass, GravityRadius, StartBonus,
            DustChance, FragUnlock, FragChance, FragOffset, FollowSpeed, DustMaxSpeed, SpawnRamp,
        };

        /// <summary>
        /// 24 §8: the ability stats. Apart from <see cref="All"/> because Stats.Compute does not read them: what they
        /// raise lives in the AbilityTable, so they are summed per ability by <see cref="Stats.AbilityFromTree"/>.
        /// </summary>
        public static readonly string[] AbilityStats = { AbilityPower, AbilityCooldown, AbilityCount, AbilityAmount, AbilityRange, AbilityDuration };

        /// <summary>Stats that act on one dust kind and therefore need <see cref="NodeDef.dustKindId"/>.</summary>
        public static bool NeedsDustKind(string id) => id == DustChance;

        /// <summary>Stats that act on one ability and therefore need <see cref="NodeDef.abilityId"/> (24 §8).</summary>
        public static bool NeedsAbility(string id) => Array.IndexOf(AbilityStats, id) >= 0;

        public static bool IsKnown(string id) => Array.IndexOf(All, id) >= 0 || NeedsAbility(id);
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
        /// <summary>
        /// 24 §8: turns on the ability named by <see cref="NodeDef.abilityId"/>. One level; there is one of these per
        /// AbilityTable row.
        /// </summary>
        AbilityUnlock = 3,
        /// <summary>
        /// 24 §21.2: the evolution of the ability named by <see cref="NodeDef.abilityId"/>: owned, the ability takes its
        /// evolved form (유성우, 연쇄 붕괴, 공명, 번개 사슬, 특이점). One level; at most one per ability, in the late rings.
        /// </summary>
        AbilityEvolve = 4,
        /// <summary>
        /// 29 §5: a rule node. It raises no stat; it changes a rule of the game, named by <see cref="NodeDef.ruleId"/>
        /// (T1 유예, C2 열 번째 …). 13th pass step 1: the nodes exist and can be bought but do nothing yet.
        /// </summary>
        Rule = 5,
    }

    /// <summary>
    /// 29 §2: the five categories of tree v2. Each owns a 72° sector of the tree, colored by UITheme.tree.categoryColors
    /// (index = this value).
    /// </summary>
    public enum NodeCategory
    {
        Celestial = 0,
        Dust = 1,
        Gravity = 2,
        Ability = 3,
        Time = 4,
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
        [Tooltip("AbilityUnlock nodes and ability_* stat nodes: the AbilityTable row they act on (24 §8).")]
        public string abilityId;
        [Tooltip("The ring this node belongs to. Needs unlockedMaxTier >= minTier. Ring 1 is open from the start.")]
        public int minTier = 1;
        [Tooltip("Any-of. Empty = next to the center, always met.")]
        public List<string> prereqs = new List<string>();
        [Tooltip("Placement angle on the ring (tree view), before the per-ring twist. Radius comes from minTier.")]
        public float angleDeg;
        [Tooltip("29 §3.5: moves the node in or out from its ring circle, in ring steps (0.4 = 40% of the way to the next ring).")]
        public float radiusOffset;
        [Tooltip("29 §2: the category whose sector and color the node takes.")]
        public NodeCategory category;
        [Tooltip("Rule nodes: the 29 §5 id (T1, D3, G2, C1, A2 …).")]
        public string ruleId;
        [Tooltip("Off: treated as if the node did not exist (not purchasable, owned levels have no effect).")]
        public bool enabled = true;

        public bool IsGate => kind == NodeKind.TierUnlock;
        /// <summary>20 §4: this node makes a dust kind start appearing.</summary>
        public bool IsDustUnlock => kind == NodeKind.DustKindUnlock;
        /// <summary>24 §8: this node turns an ability on.</summary>
        public bool IsAbilityUnlock => kind == NodeKind.AbilityUnlock;
        /// <summary>24 §21.2: this node evolves an ability.</summary>
        public bool IsAbilityEvolve => kind == NodeKind.AbilityEvolve;
        /// <summary>29 §5: this node changes a game rule.</summary>
        public bool IsRule => kind == NodeKind.Rule;
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

        /// <summary>The AbilityUnlock node of an ability, or null (24 §8).</summary>
        public NodeDef AbilityUnlock(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) return null;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i].IsAbilityUnlock && nodes[i].abilityId == abilityId) return nodes[i];
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

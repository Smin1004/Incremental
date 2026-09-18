using System;
using System.Collections.Generic;

namespace Incremental
{
    /// <summary>Result of <see cref="DataValidator.Validate"/>: errors break the game, warnings are balance switches worth a look.</summary>
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Consistency checks for the celestial table and the skill tree (12 §3). Used by the editor menu Incremental/Validate Data
    /// and by the EditMode tests.
    /// </summary>
    public static class DataValidator
    {
        /// <summary>Two nodes of the same ring closer than this (degrees) overlap in the tree view.</summary>
        public const float MinAngleGapDeg = 5f;

        public static ValidationReport Validate(CelestialTable c, NodeTable t)
        {
            var r = new ValidationReport();
            if (c == null) r.Errors.Add("CelestialTable is missing.");
            if (t == null) r.Errors.Add("NodeTable is missing.");
            if (!r.Ok) return r;

            ValidateTiers(c, r);
            ValidateNodes(c, t, r);
            ValidateGates(c, t, r);
            ValidateGraph(t, r);
            ValidateAngles(t, r);
            ValidateRingCostMult(c, t, r);
            return r;
        }

        // Tiers: 1..N contiguous ascending, required mass strictly increasing, positive price, string key present.
        static void ValidateTiers(CelestialTable c, ValidationReport r)
        {
            if (c.tiers.Count == 0) r.Errors.Add("CelestialTable: no tiers.");
            for (int i = 0; i < c.tiers.Count; i++)
            {
                var tier = c.tiers[i];
                if (tier.tier != i + 1)
                    r.Errors.Add($"CelestialTable: entry {i} has tier {tier.tier}, expected {i + 1} (tiers must start at 1, contiguous, ascending).");
                if (i > 0 && tier.requiredMass <= c.tiers[i - 1].requiredMass)
                    r.Errors.Add($"CelestialTable: tier {tier.tier} requiredMass {tier.requiredMass} is not greater than tier {c.tiers[i - 1].tier} ({c.tiers[i - 1].requiredMass}).");
                if (tier.requiredMass <= 0) r.Errors.Add($"CelestialTable: tier {tier.tier} requiredMass must be > 0.");
                if (tier.salePrice <= 0) r.Errors.Add($"CelestialTable: tier {tier.tier} salePrice must be > 0.");
                if (!UIStrings.Has(UIStrings.TierKey(tier.tier)))
                    r.Errors.Add($"UIStrings: missing key '{UIStrings.TierKey(tier.tier)}'.");
            }
        }

        // Per node: unique id, known stat, string keys, sane cost curve, ring in range.
        static void ValidateNodes(CelestialTable c, NodeTable t, ValidationReport r)
        {
            var seen = new HashSet<string>();
            for (int i = 0; i < t.nodes.Count; i++)
            {
                var n = t.nodes[i];
                if (n == null || string.IsNullOrEmpty(n.id))
                {
                    r.Errors.Add($"NodeTable: entry {i} has an empty id.");
                    continue;
                }
                if (!seen.Add(n.id)) r.Errors.Add($"NodeTable: duplicate id '{n.id}'.");
                if (n.minTier < 1 || n.minTier > c.MaxTier)
                    r.Errors.Add($"NodeTable: '{n.id}' minTier {n.minTier} is outside 1..{c.MaxTier}.");
                if (n.baseCost <= 0) r.Errors.Add($"NodeTable: '{n.id}' baseCost must be > 0.");
                if (n.growth <= 0) r.Errors.Add($"NodeTable: '{n.id}' growth must be > 0.");
                if (n.maxLevel < 0) r.Errors.Add($"NodeTable: '{n.id}' maxLevel must be >= 0.");

                if (n.kind == NodeKind.Stat)
                {
                    if (!StatIds.IsKnown(n.statId)) r.Errors.Add($"NodeTable: '{n.id}' has unknown statId '{n.statId}'.");
                    else
                    {
                        if (!UIStrings.Has(UIStrings.StatNameKey(n.statId))) r.Errors.Add($"UIStrings: missing key '{UIStrings.StatNameKey(n.statId)}'.");
                        if (!UIStrings.Has(UIStrings.StatEffectKey(n.statId))) r.Errors.Add($"UIStrings: missing key '{UIStrings.StatEffectKey(n.statId)}'.");
                    }
                    if (n.statId == StatIds.DustMass ? n.effectPerLevel <= 1 : n.effectPerLevel <= 0)
                        r.Errors.Add($"NodeTable: '{n.id}' effectPerLevel {n.effectPerLevel} has no effect.");
                    if (n.maxLevel == 0 && n.minTier != c.MaxTier)
                        r.Warnings.Add($"NodeTable: '{n.id}' has unlimited levels outside the last ring ({c.MaxTier}).");
                }
            }
        }

        // Gates: exactly one per tier 2..N, tier exists, one level.
        static void ValidateGates(CelestialTable c, NodeTable t, ValidationReport r)
        {
            var gateTiers = new HashSet<int>();
            foreach (var n in t.nodes)
            {
                if (n == null || !n.IsGate) continue;
                if (c.Get(n.tier) == null) r.Errors.Add($"NodeTable: gate '{n.id}' unlocks tier {n.tier}, which does not exist.");
                else if (n.tier <= 1) r.Errors.Add($"NodeTable: gate '{n.id}' unlocks tier {n.tier}; tier 1 is always unlocked.");
                if (!gateTiers.Add(n.tier)) r.Errors.Add($"NodeTable: more than one gate unlocks tier {n.tier}.");
                if (n.maxLevel != 1) r.Errors.Add($"NodeTable: gate '{n.id}' must have maxLevel 1.");
            }
            for (int i = 0; i < c.tiers.Count; i++)
            {
                int tier = c.tiers[i].tier;
                if (tier > 1 && !gateTiers.Contains(tier))
                    r.Errors.Add($"NodeTable: tier {tier} has no gate; gates are sequential, so tiers from {tier} up are unreachable.");
            }
        }

        // Prereqs exist, no cycles, every node reachable from the center; disabled nodes that cut a path are warned about.
        static void ValidateGraph(NodeTable t, ValidationReport r)
        {
            var ids = new HashSet<string>();
            foreach (var n in t.nodes) if (n != null && !string.IsNullOrEmpty(n.id)) ids.Add(n.id);

            foreach (var n in t.nodes)
            {
                if (n?.prereqs == null) continue;
                foreach (var p in n.prereqs)
                {
                    if (!ids.Contains(p)) r.Errors.Add($"NodeTable: '{n.id}' has prereq '{p}', which does not exist.");
                    else if (p == n.id) r.Errors.Add($"NodeTable: '{n.id}' lists itself as a prereq.");
                }
            }

            foreach (var cycle in FindCycles(t)) r.Errors.Add("NodeTable: prereq cycle " + cycle + ".");

            var reachable = Reachable(t, false);
            var reachableEnabled = Reachable(t, true);
            foreach (var n in t.nodes)
            {
                if (n == null || string.IsNullOrEmpty(n.id)) continue;
                if (!reachable.Contains(n.id)) r.Errors.Add($"NodeTable: '{n.id}' cannot be reached from the center.");
                else if (n.enabled && !reachableEnabled.Contains(n.id))
                    r.Warnings.Add($"NodeTable: '{n.id}' is only reachable through disabled nodes ({string.Join(", ", n.prereqs)}).");
            }
        }

        /// <summary>Any-of reachability: a node is reachable when it has no prereqs or one of its prereqs is reachable.</summary>
        static HashSet<string> Reachable(NodeTable t, bool enabledOnly)
        {
            var reached = new HashSet<string>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var n in t.nodes)
                {
                    if (n == null || string.IsNullOrEmpty(n.id) || reached.Contains(n.id)) continue;
                    if (enabledOnly && !n.enabled) continue;
                    bool ok = n.prereqs == null || n.prereqs.Count == 0;
                    if (!ok)
                        foreach (var p in n.prereqs)
                            if (reached.Contains(p)) { ok = true; break; }
                    if (!ok) continue;
                    reached.Add(n.id);
                    changed = true;
                }
            }
            return reached;
        }

        static List<string> FindCycles(NodeTable t)
        {
            var cycles = new List<string>();
            var state = new Dictionary<string, int>(); // 0 new, 1 on stack, 2 done
            var stack = new List<string>();
            foreach (var n in t.nodes)
                if (n != null && !string.IsNullOrEmpty(n.id)) Visit(t, n.id, state, stack, cycles);
            return cycles;
        }

        static void Visit(NodeTable t, string id, Dictionary<string, int> state, List<string> stack, List<string> cycles)
        {
            state.TryGetValue(id, out int s);
            if (s == 2) return;
            if (s == 1)
            {
                int start = stack.IndexOf(id);
                cycles.Add(string.Join(" → ", stack.GetRange(start, stack.Count - start)) + " → " + id);
                return;
            }
            var n = t.Get(id);
            if (n == null) return;
            state[id] = 1;
            stack.Add(id);
            if (n.prereqs != null)
                foreach (var p in n.prereqs) Visit(t, p, state, stack, cycles);
            stack.RemoveAt(stack.Count - 1);
            state[id] = 2;
        }

        // Same ring, angles closer than MinAngleGapDeg: the tree view would draw them on top of each other.
        static void ValidateAngles(NodeTable t, ValidationReport r)
        {
            for (int i = 0; i < t.nodes.Count; i++)
            for (int j = i + 1; j < t.nodes.Count; j++)
            {
                var a = t.nodes[i];
                var b = t.nodes[j];
                if (a == null || b == null || a.minTier != b.minTier) continue;
                float d = Math.Abs(a.angleDeg - b.angleDeg) % 360f;
                if (d > 180f) d = 360f - d;
                if (d < MinAngleGapDeg)
                    r.Errors.Add($"NodeTable: '{a.id}' and '{b.id}' overlap on ring {a.minTier} ({a.angleDeg}° / {b.angleDeg}°).");
            }
        }

        static void ValidateRingCostMult(CelestialTable c, NodeTable t, ValidationReport r)
        {
            int rings = c.MaxTier + 1;
            if (t.ringCostMult == null || t.ringCostMult.Length < rings)
                r.Warnings.Add($"NodeTable: ringCostMult has {(t.ringCostMult == null ? 0 : t.ringCostMult.Length)} entries, expected {rings} (missing rings count as 1).");
            if (t.ringCostMult != null)
                for (int i = 0; i < t.ringCostMult.Length; i++)
                    if (t.ringCostMult[i] <= 0) r.Errors.Add($"NodeTable: ringCostMult[{i}] must be > 0.");
        }
    }
}

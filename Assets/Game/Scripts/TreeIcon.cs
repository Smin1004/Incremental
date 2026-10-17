using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// 29 §6 (Q10): a skill tree node drawn by the Incremental/TreeIcon shader: a disc in the category color, a border
    /// (two for a rule node) and one symbol, no textures. The disc color is <see cref="Graphic.color"/>; the symbol and
    /// border color is <see cref="Ink"/>. The canvas has to pass TexCoord1 (UIBuilder.CreateCanvas does).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TreeIcon : MaskableGraphic
    {
        /// <summary>The symbols of the shader, by id. Keep in step with Symbol() in TreeIcon.shader.</summary>
        public enum Symbol
        {
            None = 0,
            Spawn = 1, DustCap = 2, StartBonus = 3, DustKind = 4, DustMass = 5,
            Pull = 6, Radius = 7, Follow = 8, DustSpeed = 9,
            Time = 10, Sale = 11, Threshold = 12, Fragment = 13, Gate = 14,
            Power = 15, Duration = 16, Count = 17, Rule = 18, Evolve = 19,
            Crystal = 20, Implode = 21, Satellite = 22, Meteor = 23, BlackHole = 24,
        }

        public const int SymbolCount = 25;

        [SerializeField] Symbol symbol;
        [SerializeField] Color ink = Color.white;
        [SerializeField] bool rule;
        [SerializeField] bool preview;

        public Symbol Glyph
        {
            get => symbol;
            set { if (symbol != value) { symbol = value; SetVerticesDirty(); } }
        }

        /// <summary>Symbol and border color.</summary>
        public Color Ink
        {
            get => ink;
            set { if (ink != value) { ink = value; SetVerticesDirty(); } }
        }

        /// <summary>29 §6: rule nodes have a double border.</summary>
        public bool Rule
        {
            get => rule;
            set { if (rule != value) { rule = value; SetVerticesDirty(); } }
        }

        /// <summary>29 §6 (Q4): a node of the next ring: outline and symbol only.</summary>
        public bool Preview
        {
            get => preview;
            set { if (preview != value) { preview = value; SetVerticesDirty(); } }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            // A preview node has a clear disc but still draws its outline and symbol (Ink, in uv1).
            canvasRenderer.cullTransparentMesh = false;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Vector2 half = r.size * 0.5f;
            Vector2 c = r.center;
            float flags = (rule ? 1f : 0f) + (preview ? 2f : 0f);
            Color32 col = color;
            var inkV = new Vector4(ink.r, ink.g, ink.b, ink.a);
            for (int i = 0; i < 4; i++)
            {
                var k = new Vector2((i == 0 || i == 1) ? -1f : 1f, (i == 0 || i == 3) ? -1f : 1f);
                vh.AddVert(new Vector3(c.x + k.x * half.x, c.y + k.y * half.y, 0f), col, new Vector4(k.x, k.y, (float)symbol, flags), inkV,
                    Vector3.back, new Vector4(1f, 0f, 0f, -1f));
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }

        /// <summary>The symbol of a node (29 §6): by stat, and by kind for gates, unlocks, evolutions and rules.</summary>
        public static Symbol SymbolFor(NodeDef n)
        {
            if (n == null) return Symbol.None;
            switch (n.kind)
            {
                case NodeKind.TierUnlock: return Symbol.Gate;
                case NodeKind.DustKindUnlock: return Symbol.DustKind;
                case NodeKind.AbilityUnlock: return AbilitySymbol(n.abilityId);
                case NodeKind.AbilityEvolve: return Symbol.Evolve;
                case NodeKind.Rule: return Symbol.Rule;
            }
            switch (n.statId)
            {
                case StatIds.SpawnRate: return Symbol.Spawn;
                case StatIds.DustCap: return Symbol.DustCap;
                case StatIds.StartBonus: return Symbol.StartBonus;
                case StatIds.DustChance: return Symbol.DustKind;
                case StatIds.DustMass: return Symbol.DustMass;
                case StatIds.PullAccel: return Symbol.Pull;
                case StatIds.GravityRadius: return Symbol.Radius;
                case StatIds.FollowSpeed: return Symbol.Follow;
                case StatIds.DustMaxSpeed: return Symbol.DustSpeed;
                case StatIds.RunTime: return Symbol.Time;
                case StatIds.SaleMult: return Symbol.Sale;
                case StatIds.ThresholdMult: return Symbol.Threshold;
                case StatIds.FragUnlock:
                case StatIds.FragChance:
                case StatIds.FragOffset: return Symbol.Fragment;
                case StatIds.AbilityPower: return Symbol.Power;
                case StatIds.AbilityCooldown: return Symbol.Time;
                case StatIds.AbilityDuration: return Symbol.Duration;
                case StatIds.AbilityCount: return Symbol.Count;
                case StatIds.AbilityRange: return Symbol.Radius;
                case StatIds.AbilityAmount: return Symbol.Spawn;
                default: return Symbol.None;
            }
        }

        public static Symbol AbilitySymbol(string abilityId)
        {
            switch (abilityId)
            {
                case "crystal": return Symbol.Crystal;
                case "implode": return Symbol.Implode;
                case "satellite": return Symbol.Satellite;
                case "meteor": return Symbol.Meteor;
                case "blackhole": return Symbol.BlackHole;
                default: return Symbol.Power;
            }
        }
    }
}

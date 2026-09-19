using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// Procedural background shared by the run, result and tree screens (14 §6): one camera-sized quad with the
    /// Incremental/Starfield shader. The palette follows the central star stage of the highest tier ever made (this run
    /// included), and the remnant nebula once the ending was reached (endingRun &gt; 0, 15 §5); the ending sequence can
    /// hold a stage for its own timeline. A stage change blends over paletteBlendSec, or over the time an ignition or the
    /// sequence asked for. The light bleed comes from the vanishing point, or from the central star on the
    /// result screen, and is multiplied by effectIntensity. Also hands the dust its palette colors. Render only.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class BackdropView : MonoBehaviour
    {
        static readonly int IdBg = Shader.PropertyToID("_BgColor");
        static readonly int IdGlowColor = Shader.PropertyToID("_GlowColor");
        static readonly int IdGlow = Shader.PropertyToID("_Glow");
        static readonly int IdStarTint = Shader.PropertyToID("_StarTint");
        static readonly int IdLayerA = Shader.PropertyToID("_LayerA");
        static readonly int IdLayerB = Shader.PropertyToID("_LayerB");
        static readonly int IdTwinkle = Shader.PropertyToID("_Twinkle");
        static readonly int IdPpu = Shader.PropertyToID("_PxPerUnit");
        static readonly int IdNebulaA = Shader.PropertyToID("_NebulaA");
        static readonly int IdNebulaB = Shader.PropertyToID("_NebulaB");
        static readonly int IdNebula = Shader.PropertyToID("_Nebula");

        GameRoot root;
        GameParams p;
        MeshRenderer mr;
        MaterialPropertyBlock mpb;
        int stage = -1;
        BackdropPalette from, to;
        float blend = 1f, blendSec = 1f;
        float nextBlendSec = -1f;
        int stageOverride = -1;
        Vector3? glowCenter;

        public int Stage => stage;
        public Color DustColorA { get; private set; } = Color.white;
        public Color DustColorB { get; private set; } = Color.white;
        public Color Background { get; private set; }

        /// <summary>0 below the first star tier (protostar), then 1, 2, 3 … by star tier, capped at <paramref name="stages"/> − 1 (13 §3).</summary>
        public static int StarStage(int highestTier, int orbitMaxTier, int stages)
        {
            int first = orbitMaxTier + 1;
            if (highestTier < first) return 0;
            int s = highestTier - first + 1;
            return Mathf.Clamp(s, 0, Mathf.Max(0, stages - 1));
        }

        /// <summary>The stage after the ending (black hole, remnant nebula): one past the last star stage (13 §3 "엔딩 후", 15 §5).</summary>
        public static int RemnantStage(int maxTier, int orbitMaxTier) => Mathf.Max(1, maxTier - orbitMaxTier + 1);

        /// <summary>
        /// Stage of the central star and the palette: the remnant once the ending was reached (endingRun &gt; 0, whether or not
        /// its sequence was seen), else the star stage of the highest tier ever made.
        /// </summary>
        public static int NaturalStage(int endingRun, int highestTier, int orbitMaxTier, int maxTier)
        {
            int remnant = RemnantStage(maxTier, orbitMaxTier);
            return endingRun > 0 ? remnant : StarStage(highestTier, orbitMaxTier, remnant);
        }

        /// <summary>The stage of this game now (no override).</summary>
        public int CurrentNaturalStage => NaturalStage(root.Meta.endingRun, root.HighestTierEver, p.orbitMaxTier, root.celestialTable.MaxTier);

        /// <summary>
        /// Holds the palette at <paramref name="s"/> regardless of progress (the ending sequence, 15 §4.3), blending over
        /// <paramref name="blendSec"/> (0 = at once).
        /// </summary>
        public void SetStageOverride(int s, float blendSec)
        {
            stageOverride = s;
            if (blendSec <= 0f) snapNext = true;
            else nextBlendSec = blendSec;
        }

        /// <summary>Back to the natural stage (blends if it differs).</summary>
        public void ClearStageOverride() => stageOverride = -1;

        bool snapNext;

        public void Init(GameRoot gameRoot, Material material)
        {
            root = gameRoot;
            p = root.gameParams;
            GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.sortingOrder = -100;
            mpb = new MaterialPropertyBlock();
            mpb.SetVector(IdLayerA, p.bgStarLayerA);
            mpb.SetVector(IdLayerB, p.bgStarLayerB);
            mpb.SetVector(IdTwinkle, new Vector4((float)p.bgTwinkleSpeed, (float)p.bgTwinkleDepth, 0f, 0f));
            mpb.SetFloat(IdPpu, (float)p.pixelsPerUnit);
            Apply();
        }

        /// <summary>The next stage change blends over this many seconds (the first ignition, 14 §6).</summary>
        public void BlendNextChange(float seconds) => nextBlendSec = Mathf.Max(0.01f, seconds);

        /// <summary>Light bleed center in world space (the central star on the result screen); null = the vanishing point.</summary>
        public void SetGlowCenter(Vector3? world) => glowCenter = world;

        public int PaletteCount => p.palettes != null ? p.palettes.Length : 0;

        BackdropPalette Palette(int s)
        {
            if (p.palettes == null || p.palettes.Length == 0) return new BackdropPalette();
            return p.palettes[Mathf.Clamp(s, 0, p.palettes.Length - 1)];
        }

        void LateUpdate() => Apply();

        void Apply()
        {
            var cam = root.Cam;
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var c = cam.transform.position;
            transform.position = new Vector3(c.x, c.y, 0f);
            transform.localScale = new Vector3(w * 1.02f, h * 1.02f, 1f);

            int s = stageOverride >= 0 ? stageOverride : CurrentNaturalStage;
            if (stage < 0 || (snapNext && (s != stage || blend < 1f)))
            {
                stage = s;
                from = to = Palette(s);
                blend = 1f;
                nextBlendSec = -1f;
            }
            else if (s != stage)
            {
                from = Current();
                to = Palette(s);
                stage = s;
                blend = 0f;
                blendSec = nextBlendSec > 0f ? nextBlendSec : Mathf.Max(0.01f, (float)p.paletteBlendSec);
                nextBlendSec = -1f;
            }
            snapNext = false;
            if (blend < 1f) blend = Mathf.Min(1f, blend + Time.deltaTime / blendSec);

            var cur = Current();
            DustColorA = cur.dustA;
            DustColorB = cur.dustB;
            Background = cur.background;
            Vector3 g = glowCenter ?? root.Planets.VanishPoint();
            float ppu = (float)p.pixelsPerUnit;
            mpb.SetColor(IdBg, cur.background);
            mpb.SetColor(IdGlowColor, cur.glowColor);
            mpb.SetVector(IdGlow, new Vector4(g.x * ppu, g.y * ppu, cur.glowRadiusPx, cur.glowStrength * root.EffectIntensity));
            mpb.SetColor(IdStarTint, cur.starTint);
            mpb.SetColor(IdNebulaA, cur.nebulaA);
            mpb.SetColor(IdNebulaB, cur.nebulaB);
            mpb.SetVector(IdNebula, new Vector4(cur.nebulaStrength, Mathf.Max(1f, cur.nebulaScalePx), 0f, 0f));
            mr.SetPropertyBlock(mpb);
        }

        BackdropPalette Current()
        {
            if (blend >= 1f || from == null) return to;
            float k = blend * blend * (3f - 2f * blend);
            return new BackdropPalette
            {
                background = Color.Lerp(from.background, to.background, k),
                glowColor = Color.Lerp(from.glowColor, to.glowColor, k),
                glowStrength = Mathf.Lerp(from.glowStrength, to.glowStrength, k),
                glowRadiusPx = Mathf.Lerp(from.glowRadiusPx, to.glowRadiusPx, k),
                dustA = Color.Lerp(from.dustA, to.dustA, k),
                dustB = Color.Lerp(from.dustB, to.dustB, k),
                starTint = Color.Lerp(from.starTint, to.starTint, k),
                nebulaA = Color.Lerp(from.nebulaA, to.nebulaA, k),
                nebulaB = Color.Lerp(from.nebulaB, to.nebulaB, k),
                nebulaStrength = Mathf.Lerp(from.nebulaStrength, to.nebulaStrength, k),
                nebulaScalePx = Mathf.Lerp(from.nebulaScalePx, to.nebulaScalePx, k),
            };
        }
    }
}

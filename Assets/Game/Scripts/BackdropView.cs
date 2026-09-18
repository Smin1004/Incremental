using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// Procedural background shared by the run, result and tree screens (14 §6): one camera-sized quad with the
    /// Incremental/Starfield shader. The palette follows the central star stage of the highest tier ever made (this run
    /// included), with no save field of its own; a stage change blends over paletteBlendSec, or over the ignition time
    /// when an ignition asked for it. The light bleed comes from the vanishing point, or from the central star on the
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

        GameRoot root;
        GameParams p;
        MeshRenderer mr;
        MaterialPropertyBlock mpb;
        int stage = -1;
        BackdropPalette from, to;
        float blend = 1f, blendSec = 1f;
        float nextBlendSec = -1f;
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

            int s = StarStage(root.HighestTierEver, p.orbitMaxTier, PaletteCount);
            if (stage < 0)
            {
                stage = s;
                from = to = Palette(s);
                blend = 1f;
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
            };
        }
    }
}

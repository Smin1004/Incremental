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
        static readonly int IdParallax = Shader.PropertyToID("_Parallax");
        static readonly int IdNebulaShift = Shader.PropertyToID("_NebulaShift");
        static readonly int IdSwirlAt = Shader.PropertyToID("_SwirlAt");
        static readonly int IdFarObj = Shader.PropertyToID("_FarObj");
        static readonly int IdFarObj2 = Shader.PropertyToID("_FarObj2");
        const int FarKinds = 3;

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
        /// <summary>26 §12.1 B2: the gravity source's smoothed offset from the middle of the screen, −1..1 on each axis.</summary>
        Vector2 parallax;
        /// <summary>B4: how far the nebula swirl is in, 0..1.</summary>
        float swirl;
        /// <summary>B3: the far body shown now, the one wanted, and its fade (1 = fully there).</summary>
        int farShown = -1, farWanted = -1;
        float farFade;
        float farSpin;
        bool loggedNebula, loggedFar;

        /// <summary>B3: which far body ring <paramref name="maxTier"/> shows: the next one with every gate.</summary>
        public static int FarKindFor(int maxTier) => ((Mathf.Max(1, maxTier) - 1) % FarKinds + FarKinds) % FarKinds;

        /// <summary>The far body shown now (captures and checks).</summary>
        public int FarKind => farShown;

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
            // 26 §12.1 B1a: the faint nebula of every stage, in that stage's colors, unless the palette has its own.
            Color nebA = cur.nebulaA, nebB = cur.nebulaB;
            float nebula = cur.nebulaStrength;
            var vis = root.Visual;
            if (vis.nebula)
            {
                var remnant = Palette(RemnantStage(root.celestialTable.MaxTier, p.orbitMaxTier));
                float faint = (float)p.backdropNebulaStrength * (remnant != null ? remnant.nebulaStrength : 0f);
                if (faint > nebula)
                {
                    nebula = faint;
                    nebA = cur.glowColor * (float)p.backdropNebulaGlowMix;
                    Color bg = cur.background;
                    float top = Mathf.Max(1e-3f, Mathf.Max(bg.r, Mathf.Max(bg.g, bg.b)));
                    nebB = new Color(bg.r / top, bg.g / top, bg.b / top) * (float)p.backdropNebulaBgMix;
                }
            }
            if (!loggedNebula || vis.nebula != loggedNebulaOn)
            {
                loggedNebula = true;
                loggedNebulaOn = vis.nebula;
                Debug.Log("[Visual] faint nebula (B1a): " + (vis.nebula ? "on, strength " + nebula.ToString("0.###") : "off"));
            }
            mpb.SetColor(IdNebulaA, nebA);
            mpb.SetColor(IdNebulaB, nebB);
            mpb.SetVector(IdNebula, new Vector4(nebula, Mathf.Max(1f, cur.nebulaScalePx), 0f, 0f));

            ApplyMotion(w, h, c, ppu);
            ApplyFarBody(w, h, c, ppu);
            mr.SetPropertyBlock(mpb);
        }

        bool loggedNebulaOn;

        /// <summary>
        /// 26 §12.1 B2 and B4: on the run screen the star layers and the nebula move against the gravity source's offset
        /// from the middle of the screen, and the nebula winds round it while holding. Both ease in and out and follow
        /// the effect intensity.
        /// </summary>
        void ApplyMotion(float w, float h, Vector3 c, float ppu)
        {
            var vis = root.Visual;
            float dt = Time.deltaTime;
            float intensity = root.EffectIntensity;
            bool run = root.Phase == GamePhase.Run && root.Run != null;
            Vector2 target = Vector2.zero;
            if (run && vis.parallax)
            {
                Vector2 g = root.GravityWorld;
                target = new Vector2(Mathf.Clamp((g.x - c.x) / (w * 0.5f), -1f, 1f), Mathf.Clamp((g.y - c.y) / (h * 0.5f), -1f, 1f));
            }
            float smooth = Mathf.Max(0.01f, (float)p.parallaxSmoothSec);
            parallax += (target - parallax) * (1f - Mathf.Exp(-dt / smooth));
            // Screen size in background px (world px), so the shift is the same share of the screen at any world scale.
            Vector2 screen = new Vector2(w * ppu, h * ppu);
            Vector2 far = -Vector2.Scale(parallax, screen) * (float)p.parallaxFar * intensity;
            Vector2 near = -Vector2.Scale(parallax, screen) * (float)p.parallaxNear * intensity;
            Vector2 neb = -Vector2.Scale(parallax, screen) * (float)p.parallaxNebula * intensity;
            // The shader looks the layers up at px + shift, so a shift against the offset is added the other way round.
            mpb.SetVector(IdParallax, new Vector4(-far.x, -far.y, -near.x, -near.y));
            farOffsetPx = far;

            bool holding = run && root.Run.holding && !root.Paused && vis.nebulaSwirl;
            float inSec = Mathf.Max(0.01f, (float)p.nebulaSwirlInSec);
            swirl = Mathf.MoveTowards(swirl, holding ? 1f : 0f, dt / inSec);
            float ease = swirl * swirl * (3f - 2f * swirl);
            Vector2 at = root.GravityWorld * ppu;
            float radiusPx = (float)(root.Effective.gravityRadius * p.nebulaSwirlReach);
            mpb.SetVector(IdNebulaShift, new Vector4(-neb.x, -neb.y, (float)p.nebulaSwirlRad * ease * intensity, Mathf.Max(1f, radiusPx)));
            mpb.SetVector(IdSwirlAt, new Vector4(at.x, at.y, 0f, 0f));
        }

        Vector2 farOffsetPx;

        /// <summary>
        /// 26 §12.1 B3: one large, very dim far body in a corner of the screen; with every gate it fades out and the next
        /// kind fades in. It stays in its corner whatever the world scale and moves with the far stars.
        /// </summary>
        void ApplyFarBody(float w, float h, Vector3 c, float ppu)
        {
            var vis = root.Visual;
            float dt = Time.deltaTime;
            farWanted = vis.farObject ? FarKindFor(root.Meta.unlockedMaxTier) : -1;
            float fadeSec = Mathf.Max(0.01f, (float)p.farObjectFadeSec * 0.5f);
            if (farShown != farWanted)
            {
                // Out first, then the next one in.
                farFade = Mathf.MoveTowards(farFade, 0f, dt / fadeSec);
                if (farFade <= 0f || farShown < 0)
                {
                    farShown = farWanted;
                    farFade = 0f;
                    if (!loggedFar || farShown >= 0)
                    {
                        loggedFar = true;
                        Debug.Log("[Visual] far body (B3): " + (farShown < 0 ? "off" : FarName(farShown) + " (ring " + root.Meta.unlockedMaxTier + ")"));
                    }
                }
            }
            else if (farShown >= 0) farFade = Mathf.MoveTowards(farFade, 1f, dt / fadeSec);
            farSpin += dt * (float)p.farObjectSpinDegPerSec * Mathf.Deg2Rad;
            if (farShown < 0 || farFade <= 0f)
            {
                mpb.SetVector(IdFarObj, new Vector4(0f, 0f, 1f, 0f));
                return;
            }
            // Each kind has its corner and its own way of turning.
            float radius = (float)p.farObjectSizeShare * h;
            Vector2 at;
            float turn;
            switch (farShown)
            {
                case 0: at = new Vector2(0.36f * w, 0.26f * h); turn = 0.5f + farSpin; radius *= 0.55f; break;
                case 1: at = new Vector2(-0.62f * w, -0.62f * h); turn = 0.35f + 0.08f * Mathf.Sin(farSpin * 0.3f); radius *= 1.1f; break;
                default: at = new Vector2(-0.33f * w, 0.3f * h); turn = -0.42f + 0.06f * Mathf.Sin(farSpin * 0.5f); radius *= 0.6f; break;
            }
            Vector2 centre = (new Vector2(c.x, c.y) + at) * ppu + farOffsetPx;
            float alpha = (float)p.farObjectAlpha * farFade * farFade * (3f - 2f * farFade);
            mpb.SetVector(IdFarObj, new Vector4(centre.x, centre.y, radius * ppu, alpha));
            mpb.SetVector(IdFarObj2, new Vector4(farShown, turn, 0f, 0f));
        }

        static string FarName(int kind) => kind == 0 ? "spiral galaxy" : kind == 1 ? "edge of a ringed giant planet" : "edge-on galaxy";

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

using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// One body drawn by the planet shader (14 §3): a quad MeshRenderer whose per-body values (look, light, fade) go through
    /// a MaterialPropertyBlock. The quad is sized so the disc has the requested diameter; glows, tails and rings use the
    /// extra room (<see cref="PlanetLook.Extent"/>). Keep the transform unrotated: the shader works in screen axes.
    /// Render only.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class PlanetVisual : MonoBehaviour
    {
        static Mesh quad;

        static readonly int IdSurface = Shader.PropertyToID("_Surface");
        static readonly int IdExtent = Shader.PropertyToID("_Extent");
        static readonly int IdBase = Shader.PropertyToID("_BaseColor");
        static readonly int IdDark = Shader.PropertyToID("_DarkColor");
        static readonly int IdAccent = Shader.PropertyToID("_AccentColor");
        static readonly int IdGlow = Shader.PropertyToID("_GlowColor");
        static readonly int IdRing = Shader.PropertyToID("_RingColor");
        static readonly int IdSurf1 = Shader.PropertyToID("_Surf1");
        static readonly int IdSurf2 = Shader.PropertyToID("_Surf2");
        static readonly int IdSurf3 = Shader.PropertyToID("_Surf3");
        static readonly int IdSurf4 = Shader.PropertyToID("_Surf4");
        static readonly int IdSurf5 = Shader.PropertyToID("_Surf5");
        static readonly int IdMotion = Shader.PropertyToID("_Motion");
        static readonly int IdExtra = Shader.PropertyToID("_Extra");
        static readonly int IdLight = Shader.PropertyToID("_Light");

        MeshRenderer mr;
        MaterialPropertyBlock mpb;
        PlanetLook look;
        float extent = 1f;
        float diameter;
        float fade = 1f;
        float glowMul = 1f;
        Vector4 extra;
        Vector4 surf4;

        public PlanetLook Look => look;
        /// <summary>Disc diameter in world units.</summary>
        public float Diameter => diameter;
        public MeshRenderer Renderer => mr;

        /// <summary>Creates a body under <paramref name="parent"/> with the planet material, drawn at <paramref name="sortingOrder"/>.</summary>
        public static PlanetVisual Create(string name, Transform parent, Material material, int sortingOrder)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<PlanetVisual>();
            v.Init(material, sortingOrder);
            return v;
        }

        /// <summary>Shader globals from GameParams: ambient, light z, rim, detail fade (14 §3).</summary>
        public static void ApplyGlobals(GameParams p)
        {
            Shader.SetGlobalFloat("_PlanetAmbient", (float)p.planetAmbient);
            Shader.SetGlobalFloat("_PlanetLightZ", (float)p.planetLightZ);
            Shader.SetGlobalVector("_PlanetRim", new Vector4((float)p.planetRimStrength, (float)p.planetRimPower, 0f, 0f));
            Shader.SetGlobalVector("_PlanetDetail", new Vector4((float)p.planetDetailPx, (float)p.planetCraterRelief, 0f, 0f));
        }

        /// <summary>Shared unit quad (−0.5..0.5) with 0..1 UVs.</summary>
        public static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "PlanetQuad" };
            quad.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(0.5f, -0.5f, 0f) };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) };
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            return quad;
        }

        void Init(Material material, int sortingOrder)
        {
            GetComponent<MeshFilter>().sharedMesh = Quad();
            mr = GetComponent<MeshRenderer>();
            if (material != null) mr.sharedMaterial = material;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.sortingOrder = sortingOrder;
            mpb = new MaterialPropertyBlock();
        }

        /// <summary>
        /// Sets the look. <paramref name="seed"/> varies the noise so bodies of one tier differ; <paramref name="axisPitchDeg"/>
        /// is the spin axis tilt toward the viewer (GameParams.planetAxisPitchDeg).
        /// </summary>
        public void SetLook(PlanetLook l, float seed, double axisPitchDeg)
        {
            look = l;
            if (l == null) return;
            extent = l.Extent();
            mpb.SetFloat(IdSurface, (float)l.surface);
            mpb.SetFloat(IdExtent, extent);
            mpb.SetColor(IdBase, l.baseColor);
            mpb.SetColor(IdDark, l.darkColor);
            mpb.SetColor(IdAccent, l.accentColor);
            mpb.SetColor(IdGlow, l.glowColor);
            mpb.SetColor(IdRing, l.ringColor);
            mpb.SetVector(IdSurf1, new Vector4(l.noiseScale, l.patchLevel, l.patchSoftness, seed));
            mpb.SetVector(IdSurf2, new Vector4(l.craterDensity, l.craterSize, l.craterScale, l.polarCap));
            mpb.SetVector(IdSurf3, new Vector4(l.irregularity, l.cloudCover, l.cloudSpinMult, l.emission));
            surf4 = new Vector4(l.bandCount, l.turbulence, l.glowSize, l.glowStrength);
            mpb.SetVector(IdSurf4, new Vector4(surf4.x, surf4.y, surf4.z, surf4.w * glowMul));
            mpb.SetVector(IdSurf5, new Vector4(l.spotSize, l.spotLatitude, 0f, 0f));
            mpb.SetVector(IdMotion, new Vector4(l.spinDegPerSec * Mathf.Deg2Rad, l.axisTiltDeg * Mathf.Deg2Rad,
                (float)axisPitchDeg * Mathf.Deg2Rad, seed * 2.399f));
            extra = new Vector4(l.tailLength, l.ringInner, l.ringOuter, fade);
            mpb.SetVector(IdExtra, extra);
            ApplyScale();
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>Disc diameter in world units.</summary>
        public void SetDiameter(float worldDiameter)
        {
            diameter = worldDiameter;
            ApplyScale();
        }

        /// <summary>Light from a world point (the vanishing point, the central star).</summary>
        public void SetLightPoint(Vector2 world)
        {
            mpb.SetVector(IdLight, new Vector4(world.x, world.y, 0f, 0f));
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>Light from a screen-plane direction (gallery).</summary>
        public void SetLightDirection(Vector2 dir)
        {
            mpb.SetVector(IdLight, new Vector4(dir.x, dir.y, 0f, 1f));
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>Overall opacity 0..1 (premultiplied, so glows fade too).</summary>
        public void SetFade(float a)
        {
            if (Mathf.Approximately(a, fade)) return;
            fade = a;
            extra.w = a;
            mpb.SetVector(IdExtra, extra);
            mr.SetPropertyBlock(mpb);
        }

        /// <summary>Multiplier on the look's glow strength (central star breathing, highlights).</summary>
        public void SetGlowMul(float k)
        {
            if (Mathf.Approximately(k, glowMul)) return;
            glowMul = k;
            mpb.SetVector(IdSurf4, new Vector4(surf4.x, surf4.y, surf4.z, surf4.w * k));
            mr.SetPropertyBlock(mpb);
        }

        public void SetSortingOrder(int order) => mr.sortingOrder = order;

        void ApplyScale()
        {
            float s = diameter * extent;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}

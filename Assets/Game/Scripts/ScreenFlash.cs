using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// A radial flash over the whole camera view (Incremental/Flash), shared by every flash in the game so they all follow
    /// the flash rule of 15 §3: brightness falls off from the source (flashEdgeShare at flashRadiusPx), the peak alpha
    /// stays at or below flashAlphaCap, the rise takes at least flashMinRiseSec, and everything is multiplied by
    /// effectIntensity (the caller passes the final alpha). Render only.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ScreenFlash : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdFlash = Shader.PropertyToID("_Flash");
        static readonly int IdPpu = Shader.PropertyToID("_PxPerUnit");

        GameRoot root;
        MeshRenderer mr;
        MaterialPropertyBlock mpb;

        public static ScreenFlash Create(string name, Transform parent, GameRoot gameRoot, int sortingOrder)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            var f = go.AddComponent<ScreenFlash>();
            f.root = gameRoot;
            go.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            f.mr = go.GetComponent<MeshRenderer>();
            if (gameRoot.flashMaterial != null) f.mr.sharedMaterial = gameRoot.flashMaterial;
            f.mr.shadowCastingMode = ShadowCastingMode.Off;
            f.mr.receiveShadows = false;
            f.mr.lightProbeUsage = LightProbeUsage.Off;
            f.mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            f.mr.sortingOrder = sortingOrder;
            f.mpb = new MaterialPropertyBlock();
            f.mpb.SetFloat(IdPpu, (float)gameRoot.gameParams.pixelsPerUnit);
            f.mr.enabled = false;
            return f;
        }

        /// <summary>
        /// 0..1 envelope: a smooth rise over <paramref name="riseSec"/>, then a quadratic fade that ends at
        /// <paramref name="endSec"/>. 0 before 0 and after the end.
        /// </summary>
        public static float Envelope(float age, float riseSec, float endSec)
        {
            if (age < 0f || age >= endSec) return 0f;
            float rise = Mathf.Max(1e-3f, riseSec);
            if (age < rise)
            {
                float k = age / rise;
                return k * k * (3f - 2f * k);
            }
            float d = Mathf.Clamp01((age - rise) / Mathf.Max(1e-3f, endSec - rise));
            return (1f - d) * (1f - d);
        }

        /// <summary>
        /// Shows the flash centered on <paramref name="centerWorld"/>. <paramref name="alpha"/> is the final brightness at the
        /// center (envelope × peak × effectIntensity); <paramref name="radiusPx"/> ≤ 0 uses GameParams.flashRadiusPx.
        /// </summary>
        public void Show(Vector3 centerWorld, Color color, float alpha, float radiusPx = -1f)
        {
            if (alpha <= 0f)
            {
                Hide();
                return;
            }
            var p = root.gameParams;
            var cam = root.Cam;
            float h = cam.orthographicSize * 2f, w = h * cam.aspect;
            var c = cam.transform.position;
            transform.position = new Vector3(c.x, c.y, 0f);
            transform.localScale = new Vector3(w * 1.05f, h * 1.05f, 1f);
            float ppu = (float)p.pixelsPerUnit;
            float r = radiusPx > 0f ? radiusPx : (float)p.flashRadiusPx;
            mpb.SetColor(IdColor, new Color(color.r, color.g, color.b, Mathf.Min(alpha, (float)p.flashAlphaCap)));
            mpb.SetVector(IdFlash, new Vector4(centerWorld.x * ppu, centerWorld.y * ppu, r, (float)p.flashEdgeShare));
            mr.SetPropertyBlock(mpb);
            if (!mr.enabled) mr.enabled = true;
        }

        public void Hide()
        {
            if (mr != null && mr.enabled) mr.enabled = false;
        }
    }
}

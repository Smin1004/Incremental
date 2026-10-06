using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// 26 §5.5: the gate expansion ring. When a gate is bought the world scale starts moving to the wider world over
    /// worldExpandSec (22 §6); a light ring spreads from the middle of the screen in step with that zoom-out, so the
    /// purchase reads as the world opening up. Drawn with the FxRing material under the UI canvases; the camera is
    /// read every frame so the ring stays centered and keeps its width on screen as the view pulls back. Strength
    /// follows the effect intensity. Render only.
    /// </summary>
    public sealed class GateExpandFx : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        GameRoot root;
        GameParams p;
        MeshRenderer ring;
        MaterialPropertyBlock mpb;
        float age = -1f;

        /// <summary>Seconds since the ring started, or −1 (captures).</summary>
        public float Age => age;

        public void Init(GameRoot gameRoot, Material ringMaterial)
        {
            root = gameRoot;
            p = root.gameParams;
            var go = new GameObject("GateRing", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            ring = go.GetComponent<MeshRenderer>();
            if (ringMaterial != null) ring.sharedMaterial = ringMaterial;
            ring.shadowCastingMode = ShadowCastingMode.Off;
            ring.receiveShadows = false;
            ring.sortingOrder = 55;
            ring.enabled = false;
            mpb = new MaterialPropertyBlock();
            root.WorldExpanding += OnWorldExpanding;
        }

        void OnWorldExpanding(double from, double to)
        {
            if (to <= from) return;
            age = 0f;
        }

        void Update()
        {
            if (age < 0f) return;
            age += Time.deltaTime;
            float duration = Mathf.Max(0.05f, (float)p.worldExpandSec);
            float k = age / duration;
            float intensity = root.EffectIntensity;
            if (k >= 1f || intensity <= 0f)
            {
                ring.enabled = false;
                if (k >= 1f) age = -1f;
                return;
            }
            var cam = root.Cam;
            var c = cam.transform.position;
            ring.transform.position = new Vector3(c.x, c.y, 0f);
            // In screen px: from a small circle to past the corners, easing out like the zoom it goes with.
            float h = Mathf.Max(1, Screen.height), w = Mathf.Max(1, Screen.width);
            float e = 1f - (1f - k) * (1f - k);
            float diameterPx = Mathf.Lerp(h * (float)p.gateRingStartShare, Mathf.Sqrt(w * w + h * h) * 1.1f, e);
            float widthPx = (float)p.gateRingWidthPx;
            float quadPx = diameterPx + widthPx * 2f;
            // World units per screen px right now, so the ring keeps its size on screen while the camera pulls back.
            float worldPerPx = 2f * cam.orthographicSize / h;
            float s = quadPx * worldPerPx;
            ring.transform.localScale = new Vector3(s, s, 1f);
            float fade = Mathf.Pow(1f - k, 1.2f);
            var col = p.gateRingColor;
            mpb.SetColor(IdColor, new Color(col.r, col.g, col.b, (float)p.gateRingStrength * fade * intensity));
            mpb.SetVector(IdRing, new Vector4(diameterPx / quadPx, widthPx / quadPx, (float)p.gateRingCoreGlow * fade, 0f));
            ring.SetPropertyBlock(mpb);
            ring.enabled = true;
        }
    }
}

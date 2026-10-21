using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// 29 §10.2 D2 갈라지는 먼지: the short crack flash where a large dust splits. A small pool of FxRing quads (no
    /// texture): each one is a bright point that opens into a thin ring over splitFlashSec and fades. Strength follows
    /// the effect intensity (F11). Render only; values in GameParams.rules.
    /// </summary>
    public sealed class SplitFlashFx : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        /// <summary>Flashes on screen at once; a split beyond this reuses the oldest.</summary>
        const int PoolSize = 24;

        GameRoot root;
        MeshRenderer[] rings;
        float[] ages;
        MaterialPropertyBlock mpb;
        int next;

        /// <summary>Seconds since the newest flash started, or −1 (captures).</summary>
        public float LastAge { get; private set; } = -1f;

        public void Init(GameRoot gameRoot, Material ringMaterial)
        {
            root = gameRoot;
            mpb = new MaterialPropertyBlock();
            rings = new MeshRenderer[PoolSize];
            ages = new float[PoolSize];
            for (int i = 0; i < PoolSize; i++)
            {
                var go = new GameObject("SplitFlash" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
                var r = go.GetComponent<MeshRenderer>();
                if (ringMaterial != null) r.sharedMaterial = ringMaterial;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sortingOrder = 12;
                r.enabled = false;
                rings[i] = r;
                ages[i] = -1f;
            }
        }

        /// <summary>A large dust split at <paramref name="world"/>.</summary>
        public void Show(Vector2 world)
        {
            if (rings == null || root.EffectIntensity <= 0f) return;
            int i = next;
            next = (next + 1) % PoolSize;
            ages[i] = 0f;
            rings[i].transform.position = new Vector3(world.x, world.y, 0f);
            LastAge = 0f;
        }

        /// <summary>Hides every flash (run end, ring change).</summary>
        public void Clear()
        {
            if (rings == null) return;
            for (int i = 0; i < PoolSize; i++)
            {
                ages[i] = -1f;
                rings[i].enabled = false;
            }
            LastAge = -1f;
        }

        void Update()
        {
            if (rings == null) return;
            var q = root.gameParams.rules;
            float duration = Mathf.Max(0.02f, (float)q.splitFlashSec);
            float intensity = root.EffectIntensity;
            float ppu = (float)root.gameParams.pixelsPerUnit;
            if (LastAge >= 0f) LastAge += Time.deltaTime;
            for (int i = 0; i < PoolSize; i++)
            {
                if (ages[i] < 0f) continue;
                ages[i] += Time.deltaTime;
                float k = ages[i] / duration;
                if (k >= 1f || intensity <= 0f)
                {
                    ages[i] = -1f;
                    rings[i].enabled = false;
                    continue;
                }
                // A point that opens into a ring, easing out, and fades as it goes.
                float e = 1f - (1f - k) * (1f - k);
                float diameterPx = Mathf.Max(2f, (float)q.splitFlashPx * e);
                float widthPx = (float)q.splitFlashWidthPx;
                float quadPx = (float)q.splitFlashPx + widthPx * 2f;
                float s = quadPx / ppu * (float)root.WorldScale;
                rings[i].transform.localScale = new Vector3(s, s, 1f);
                float fade = (1f - k) * (1f - k);
                var col = q.splitFlashColor;
                mpb.SetColor(IdColor, new Color(col.r, col.g, col.b, (float)q.splitFlashStrength * fade * intensity));
                mpb.SetVector(IdRing, new Vector4(diameterPx / quadPx, widthPx / quadPx, (float)q.splitFlashCoreGlow * (1f - k), 0f));
                rings[i].SetPropertyBlock(mpb);
                rings[i].enabled = true;
            }
        }
    }
}

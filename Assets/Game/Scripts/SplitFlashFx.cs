using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// 29 §10.2 D2 갈라지는 먼지: the short crack flash where a large dust splits. A small pool of FxRing quads (no
    /// texture): each one is a bright point that opens into a thin ring over splitFlashSec and fades. 15th pass: with it,
    /// splitCrackLines thin cracks shoot out of the point for splitCrackSec (FxLines). Strength follows the effect
    /// intensity (F11). Render only; values in GameParams.rules.
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
        /// <summary>Per flash: where it is and the heading of its first crack (the others are spread evenly from there).</summary>
        Vector2[] centers;
        float[] crackAngles;
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
            centers = new Vector2[PoolSize];
            crackAngles = new float[PoolSize];
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
            centers[i] = world;
            crackAngles[i] = Random.value * Mathf.PI * 2f;
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
                Crack(i, q, intensity, ppu);
            }
        }

        /// <summary>15th pass: the cracks of flash <paramref name="i"/>, thin lines that run out and fade in splitCrackSec.</summary>
        void Crack(int i, RuleParams q, float intensity, float ppu)
        {
            int lines = q.splitCrackLines;
            float sec = (float)q.splitCrackSec;
            if (lines <= 0 || sec <= 0f || ages[i] >= sec || root.Lines == null) return;
            float k = ages[i] / sec;
            float scale = (float)root.WorldScale / ppu;
            float len = (float)q.splitCrackPx * scale * (1f - (1f - k) * (1f - k) * (1f - k));
            float width = (float)q.splitCrackWidthPx * scale;
            var col = q.splitFlashColor;
            float b = (float)q.splitCrackBright * (1f - k) * intensity;
            var head = new Color(col.r * b, col.g * b, col.b * b);
            var tip = new Color(col.r * b * 0.3f, col.g * b * 0.3f, col.b * b * 0.3f);
            for (int j = 0; j < lines; j++)
            {
                // Even spread with a little jitter that stays fixed for this flash, so the cracks do not wobble.
                float a = crackAngles[i] + j * Mathf.PI * 2f / lines + Mathf.Sin(crackAngles[i] * 7f + j * 3.1f) * 0.35f;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                // A crack has a kink halfway, which reads as a break rather than a spoke.
                var mid = centers[i] + dir * (len * 0.5f) + new Vector2(-dir.y, dir.x) * (len * 0.12f * Mathf.Sin(a * 5f));
                root.Lines.Segment(centers[i], mid, width, width * 0.8f, head, Color.Lerp(head, tip, 0.5f));
                root.Lines.Segment(mid, centers[i] + dir * len, width * 0.8f, width * 0.2f, Color.Lerp(head, tip, 0.5f), tip);
            }
        }
    }
}

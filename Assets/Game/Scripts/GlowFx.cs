using UnityEngine;
using UnityEngine.Rendering;

namespace Incremental
{
    /// <summary>
    /// 15th pass: soft glows (FxRing quads, no texture), asked for every frame like <see cref="FxLines"/>: the meteor's hot
    /// head (M1) and entry flash (M5), the comet's coma (S3), the S1 cluster's glow and the S2 debris burst. Each call
    /// takes one quad of a pool; the pool grows when a frame asks for more, and quads not asked for in a frame are hidden
    /// in LateUpdate, which runs after every other script's. Colors go in linear, already × the effect intensity.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class GlowFx : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        Material material;
        int sortingOrder;
        MeshRenderer[] pool = new MeshRenderer[0];
        int used;
        MaterialPropertyBlock mpb;

        /// <summary>Glows drawn in the last frame (checks).</summary>
        public int LastCount { get; private set; }

        public void Init(Material ringMaterial, int order)
        {
            material = ringMaterial;
            sortingOrder = order;
            mpb = new MaterialPropertyBlock();
            Grow(16);
        }

        void Grow(int n)
        {
            int had = pool.Length;
            if (n <= had) return;
            System.Array.Resize(ref pool, n);
            for (int i = had; i < n; i++)
            {
                var go = new GameObject("Glow" + i, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                go.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
                var r = go.GetComponent<MeshRenderer>();
                if (material != null) r.sharedMaterial = material;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.sortingOrder = sortingOrder;
                r.enabled = false;
                pool[i] = r;
            }
        }

        /// <summary>
        /// A soft round glow of <paramref name="diameter"/> (world units) at <paramref name="at"/>: a center disc of
        /// strength <paramref name="center"/> plus a ring at <paramref name="ringRadius"/> (share of the half size) of half
        /// width <paramref name="ringWidth"/>. A plain glow is ringRadius 0 with a wide ringWidth. <paramref name="stretch"/>
        /// above 1 draws it longer along <paramref name="along"/> (a hot head leaning forward).
        /// </summary>
        public void Draw(Vector2 at, float diameter, Color color, float ringRadius, float ringWidth, float center, Vector2 along = default, float stretch = 1f)
        {
            if (mpb == null || diameter <= 0f || color.a <= 0f) return;
            if (used >= pool.Length) Grow(pool.Length * 2);
            var r = pool[used++];
            var t = r.transform;
            t.position = new Vector3(at.x, at.y, 0f);
            float angle = along.sqrMagnitude > 1e-8f ? Mathf.Atan2(along.y, along.x) * Mathf.Rad2Deg : 0f;
            t.rotation = Quaternion.Euler(0f, 0f, angle);
            t.localScale = new Vector3(diameter * Mathf.Max(1f, stretch), diameter, 1f);
            mpb.SetColor(IdColor, color);
            mpb.SetVector(IdRing, new Vector4(ringRadius, Mathf.Max(1e-3f, ringWidth), center, 0f));
            r.SetPropertyBlock(mpb);
            r.enabled = true;
        }

        void LateUpdate()
        {
            LastCount = used;
            for (int i = used; i < pool.Length; i++)
                if (pool[i].enabled) pool[i].enabled = false;
            used = 0;
        }
    }
}

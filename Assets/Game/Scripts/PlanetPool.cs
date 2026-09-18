using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Pool of planets shown where one is created during a run, then sent off to the vanishing point (13 §2):
    /// pop (planetPopDurationSec) → stays in place (planetHoldSec) → flies to the vanishing point while shrinking and
    /// fading (planetDepartSec). Each planet carries a crescent shadow lit from the vanishing point (13 §4). Render only;
    /// input is never blocked.
    /// </summary>
    public sealed class PlanetPool : MonoBehaviour
    {
        struct Slot
        {
            public Transform tf;
            public SpriteRenderer sr;
            public SpriteRenderer shadow;
            public Color color;
            public Vector3 start;
            public float targetScale;
            public float age;
            public bool active;
        }

        Slot[] slots = new Slot[0];
        int next;
        GameParams p;
        Camera cam;
        float spriteUnitSize = 1f;

        /// <summary>Seconds a planet is on screen: pop + hold + departure.</summary>
        public float Lifetime => (float)(p.planetPopDurationSec + p.planetHoldSec + p.planetDepartSec);

        public void Init(GameParams gameParams, Sprite sprite, Material material, int poolSize, Camera camera)
        {
            p = gameParams;
            cam = camera;
            spriteUnitSize = sprite != null ? Mathf.Max(sprite.bounds.size.x, 1e-3f) : 1f;
            var shadowSprite = UIBuilder.ShadowSprite();
            float shadowScale = spriteUnitSize / Mathf.Max(shadowSprite.bounds.size.x, 1e-3f);
            slots = new Slot[Mathf.Max(1, poolSize)];
            for (int i = 0; i < slots.Length; i++)
            {
                var go = new GameObject("Planet" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                if (material != null) sr.sharedMaterial = material;
                sr.sortingOrder = 10;

                var sh = new GameObject("Shadow").AddComponent<SpriteRenderer>();
                sh.transform.SetParent(go.transform, false);
                sh.transform.localScale = new Vector3(shadowScale, shadowScale, 1f);
                sh.sprite = shadowSprite;
                if (material != null) sh.sharedMaterial = material;
                sh.sortingOrder = 11;

                go.SetActive(false);
                slots[i] = new Slot { tf = go.transform, sr = sr, shadow = sh };
            }
        }

        /// <summary>World position of the vanishing point (viewport coordinates from GameParams).</summary>
        public Vector3 VanishPoint()
        {
            var v = cam.ViewportToWorldPoint(new Vector3(p.vanishPoint.x, p.vanishPoint.y, 0f));
            return new Vector3(v.x, v.y, 0f);
        }

        public void Show(Vector2 pos, double sizePx, Color color)
        {
            ref Slot s = ref slots[next];
            next = (next + 1) % slots.Length;
            s.active = true;
            s.age = 0f;
            s.color = color;
            s.start = new Vector3(pos.x, pos.y, 0f);
            s.targetScale = (float)(sizePx / p.pixelsPerUnit) / spriteUnitSize;
            s.tf.position = s.start;
            // Shadow +x points away from the light (the vanishing point).
            Vector3 away = s.start - VanishPoint();
            s.shadow.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(away.y, away.x) * Mathf.Rad2Deg);
            s.tf.gameObject.SetActive(true);
            Apply(ref s);
        }

        public void Clear()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].active = false;
                slots[i].tf.gameObject.SetActive(false);
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float life = Lifetime;
            for (int i = 0; i < slots.Length; i++)
            {
                ref Slot s = ref slots[i];
                if (!s.active) continue;
                s.age += dt;
                if (s.age >= life)
                {
                    s.active = false;
                    s.tf.gameObject.SetActive(false);
                    continue;
                }
                Apply(ref s);
            }
        }

        void Apply(ref Slot s)
        {
            float popDur = (float)p.planetPopDurationSec;
            float hold = (float)p.planetHoldSec;
            float depart = Mathf.Max(0.01f, (float)p.planetDepartSec);
            float pop = (float)(p.planetPopScale * p.effectIntensity);

            float tPop = popDur > 0f ? Mathf.Clamp01(s.age / popDur) : 1f;
            float scale = s.targetScale * (1f + pop * (1f - tPop));
            float alpha = 1f;
            Vector3 pos = s.start;

            float tDepart = Mathf.Clamp01((s.age - popDur - hold) / depart);
            if (tDepart > 0f)
            {
                float e = tDepart * tDepart; // accelerate away
                pos = Vector3.Lerp(s.start, VanishPoint(), e);
                scale *= Mathf.Lerp(1f, 0.15f, e);
                alpha = 1f - tDepart * tDepart * tDepart;
            }

            s.tf.position = pos;
            s.tf.localScale = new Vector3(scale, scale, 1f);
            var c = s.color;
            c.a *= alpha;
            s.sr.color = c;
            s.shadow.color = new Color(0f, 0f, 0f, (float)p.planetShadow * alpha);
        }
    }
}

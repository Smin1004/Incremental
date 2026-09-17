using UnityEngine;

namespace Incremental
{
    /// <summary>Pool of circle sprites shown briefly where a planet is created ("sold"). Render only.</summary>
    public sealed class PlanetPool : MonoBehaviour
    {
        struct Slot
        {
            public Transform tf;
            public SpriteRenderer sr;
            public Color color;
            public float targetScale;
            public float age;
            public bool active;
        }

        Slot[] slots = new Slot[0];
        int next;
        GameParams p;
        float spriteUnitSize = 1f;

        public void Init(GameParams gameParams, Sprite sprite, Material material, int poolSize)
        {
            p = gameParams;
            spriteUnitSize = sprite != null ? Mathf.Max(sprite.bounds.size.x, 1e-3f) : 1f;
            slots = new Slot[Mathf.Max(1, poolSize)];
            for (int i = 0; i < slots.Length; i++)
            {
                var go = new GameObject("Planet" + i);
                go.transform.SetParent(transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                if (material != null) sr.sharedMaterial = material;
                sr.sortingOrder = 10;
                go.SetActive(false);
                slots[i] = new Slot { tf = go.transform, sr = sr };
            }
        }

        public void Show(Vector2 pos, double sizePx, Color color)
        {
            ref Slot s = ref slots[next];
            next = (next + 1) % slots.Length;
            s.active = true;
            s.age = 0f;
            s.color = color;
            s.targetScale = (float)(sizePx / p.pixelsPerUnit) / spriteUnitSize;
            s.tf.position = new Vector3(pos.x, pos.y, 0f);
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
            float life = (float)p.planetLifetimeSec;
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
            float life = (float)p.planetLifetimeSec;
            float popDur = (float)p.planetPopDurationSec;
            float pop = (float)(p.planetPopScale * p.effectIntensity);
            float t = popDur > 0f ? Mathf.Clamp01(s.age / popDur) : 1f;
            float scale = s.targetScale * (1f + pop * (1f - t));
            s.tf.localScale = new Vector3(scale, scale, 1f);

            float fadeStart = life * 0.6f;
            float alpha = (s.age <= fadeStart || life <= fadeStart) ? 1f : 1f - (s.age - fadeStart) / (life - fadeStart);
            var c = s.color;
            c.a *= Mathf.Clamp01(alpha);
            s.sr.color = c;
        }
    }
}

using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// Pool of planets shown where one is created during a run, then sent off to the vanishing point (13 §2):
    /// pop (planetPopDurationSec) → stays in place (planetHoldSec) → flies to the vanishing point while shrinking and
    /// fading (planetDepartSec). Drawn by the planet shader, lit from the vanishing point (14 §3). Render only;
    /// input is never blocked.
    /// </summary>
    public sealed class PlanetPool : MonoBehaviour
    {
        struct Slot
        {
            public PlanetVisual v;
            public Vector3 start;
            public float diameter;
            public float fromDiameter;
            public float age;
            public bool active;
            /// <summary>24 §21.3: the pop of this body, x GameParams.planetPopScale (1 for an ordinary body).</summary>
            public float pop;
        }

        Slot[] slots = new Slot[0];
        int next;
        GameRoot root;
        GameParams p;
        Camera cam;

        /// <summary>Seconds a planet is on screen: pop + hold + departure.</summary>
        public float Lifetime => (float)(p.planetPopDurationSec + p.planetHoldSec + p.planetDepartSec);

        public void Init(GameRoot gameRoot, int poolSize)
        {
            root = gameRoot;
            p = root.gameParams;
            cam = root.Cam;
            slots = new Slot[Mathf.Max(1, poolSize)];
            for (int i = 0; i < slots.Length; i++)
            {
                var v = PlanetVisual.Create("Planet" + i, transform, root.planetMaterial, 10);
                v.gameObject.SetActive(false);
                slots[i] = new Slot { v = v };
            }
        }

        /// <summary>World position of the vanishing point (viewport coordinates from GameParams).</summary>
        public Vector3 VanishPoint()
        {
            var v = cam.ViewportToWorldPoint(new Vector3(p.vanishPoint.x, p.vanishPoint.y, 0f));
            return new Vector3(v.x, v.y, 0f);
        }

        /// <summary>
        /// Shows a planet of this tier at <paramref name="pos"/>. <paramref name="seed"/> keeps the forming body's surface;
        /// <paramref name="fromDiameterPx"/> (> 0) is the forming body's size, so the pop starts from it.
        /// </summary>
        public void Show(Vector2 pos, CelestialTier tier, float seed, double fromDiameterPx) =>
            Show(pos, tier, seed, fromDiameterPx, 1.0);

        /// <summary>
        /// Same, at <paramref name="scale"/> of the tier's display size: a fragment is drawn smaller than the body it
        /// fell off (20 §5). It leaves for the vanishing point like any other body.
        /// </summary>
        public void Show(Vector2 pos, CelestialTier tier, float seed, double fromDiameterPx, double scale) =>
            Show(pos, tier, seed, fromDiameterPx, scale, 1.0);

        /// <summary>Same, with its pop x <paramref name="popMult"/>: a bundle (24 §21.3) lands harder than one body.</summary>
        public void Show(Vector2 pos, CelestialTier tier, float seed, double fromDiameterPx, double scale, double popMult)
        {
            ref Slot s = ref slots[next];
            next = (next + 1) % slots.Length;
            s.active = true;
            s.age = 0f;
            s.pop = Mathf.Max(0f, (float)popMult);
            s.start = new Vector3(pos.x, pos.y, 0f);
            // 22 §6: the body keeps its world size as the camera pulls back, but never shrinks past bodyMinScreenPx
            // on screen, so an asteroid or a fragment is still a body at ring 11 and not a dot.
            s.diameter = (float)(root.RunBodyPx(tier.sizePx * scale) / p.pixelsPerUnit);
            s.fromDiameter = fromDiameterPx > 0 ? (float)(fromDiameterPx / p.pixelsPerUnit) : s.diameter;
            s.v.transform.position = s.start;
            s.v.SetLook(tier.look, seed, p.planetAxisPitchDeg);
            s.v.SetLightPoint(VanishPoint());
            s.v.gameObject.SetActive(true);
            Apply(ref s);
        }

        public void Clear()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i].active = false;
                slots[i].v.gameObject.SetActive(false);
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
                    s.v.gameObject.SetActive(false);
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
            float pop = (float)p.planetPopScale * root.EffectIntensity * (s.pop > 0f ? s.pop : 1f);

            float tPop = popDur > 0f ? Mathf.Clamp01(s.age / popDur) : 1f;
            float baseD = Mathf.Lerp(s.fromDiameter, s.diameter, tPop);
            float d = baseD * (1f + pop * Mathf.Sin(tPop * Mathf.PI));
            float alpha = 1f;
            Vector3 pos = s.start;

            float tDepart = Mathf.Clamp01((s.age - popDur - hold) / depart);
            if (tDepart > 0f)
            {
                float e = tDepart * tDepart; // accelerate away
                pos = Vector3.Lerp(s.start, VanishPoint(), e);
                d *= Mathf.Lerp(1f, 0.15f, e);
                alpha = 1f - tDepart * tDepart * tDepart;
            }

            s.v.transform.position = pos;
            s.v.SetDiameter(d);
            s.v.SetFade(alpha);
        }
    }
}

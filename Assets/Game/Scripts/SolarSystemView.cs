using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result screen solar system (13 §3-§5), drawn in world space behind the result panel.
    /// Planets made this run orbit a display-only central star: one orbit per unlocked tier up to orbitMaxTier,
    /// radius r0 + (t − 1) × step, counter-clockwise at ω(r) = ω0 × (r0 / r)^1.5. Tiers up to orbitBeltMaxTier with more than
    /// orbitBeltThreshold planets become an asteroid belt; higher tiers show at most orbitIndividualMax planets, then "×N".
    /// Stars (tiers above orbitMaxTier) made this run go to the sky. The central star's look follows highestTierCreated,
    /// with an ignition flash the first time a star is made. On entry the planets fly in from the vanishing point, lower
    /// tiers first. During runs a faint glow marks the vanishing point (13 §2).
    /// </summary>
    public sealed class SolarSystemView : MonoBehaviour
    {
        struct Orbit
        {
            public Transform pivot;
            public float omegaDeg;
        }

        sealed class Flier
        {
            public Transform tf;
            public Transform pivot;
            public Vector3 local;
            public float scale;
            public float start;
            public bool landed;
        }

        struct Fade
        {
            public SpriteRenderer sr;
            public Color color;
            public float start;
        }

        const int OrbitSegments = 128;
        const float EntryTravel = 0.5f; // share of the entry time one tier's planets travel

        GameRoot root;
        GameParams p;
        Camera cam;
        Material mat;
        Sprite circle, dot, shadow;
        float circleUnit = 1f, dotUnit = 1f, shadowUnit = 1f;

        Transform system, sky, content, vanishGlow;
        SpriteRenderer vanishGlowSr;
        SpriteRenderer starCore, starGlow, flash;
        Canvas labelCanvas;
        readonly List<Text> labels = new List<Text>();
        readonly List<Orbit> orbits = new List<Orbit>();
        readonly List<Flier> fliers = new List<Flier>();
        readonly List<Fade> fades = new List<Fade>();
        float age;
        bool visible;
        bool ignition;
        int stage;

        float Ppu => (float)p.pixelsPerUnit;
        float EntrySec => Mathf.Max(0.01f, (float)p.resultEntrySec);

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;
            cam = root.Cam;
            mat = root.unlitMaterial;
            circle = root.circleSprite;
            dot = UIBuilder.SoftDotSprite();
            shadow = UIBuilder.ShadowSprite();
            circleUnit = circle != null ? Mathf.Max(circle.bounds.size.x, 1e-3f) : 1f;
            dotUnit = Mathf.Max(dot.bounds.size.x, 1e-3f);
            shadowUnit = Mathf.Max(shadow.bounds.size.x, 1e-3f);

            system = new GameObject("System").transform;
            system.SetParent(transform, false);
            sky = new GameObject("Sky").transform;
            sky.SetParent(transform, false);
            BuildSky();

            starGlow = MakeSprite("StarGlow", system, dot, -25);
            starCore = MakeSprite("StarCore", system, circle, -9);
            flash = MakeSprite("Ignition", system, dot, -5);
            flash.gameObject.SetActive(false);

            vanishGlowSr = MakeSprite("VanishGlow", transform, dot, -30);
            vanishGlow = vanishGlowSr.transform;

            labelCanvas = UIBuilder.CreateCanvas("SolarLabels", transform);
            labelCanvas.sortingOrder = 5;

            root.Result.EntrySkipped += Skip;
            root.RunStarted += Hide;
            SetVisible(false);
        }

        // ---------------- show / hide ----------------

        public void Show(RunRecord rec)
        {
            var m = root.Meta;
            Vector3 center = CamCenter() + new Vector3((float)(p.starOffsetPx / Ppu), 0f, 0f);
            system.position = center;
            if (content != null) Destroy(content.gameObject);
            content = new GameObject("Content").transform;
            content.SetParent(system, false);
            orbits.Clear();
            fliers.Clear();
            fades.Clear();
            foreach (var l in labels) l.gameObject.SetActive(false);
            int labelIndex = 0;
            var rng = new System.Random(rec != null ? rec.run * 7919 : 1);

            stage = StarStage(m.highestTierCreated);
            ignition = rec != null && MadeStar(rec) && !MadeStarBefore(m, rec);
            SetStar(stage);

            // Orbits and planets, tier 1 inside.
            int maxOrbit = Mathf.Min(m.unlockedMaxTier, p.orbitMaxTier);
            var present = new List<int>();
            for (int t = 1; t <= maxOrbit; t++)
                if (rec != null && rec.TierCount(t) > 0) present.Add(t);

            for (int t = 1; t <= Mathf.Max(1, maxOrbit); t++)
            {
                float rPx = (float)(p.orbitR0Px + (t - 1) * p.orbitStepPx);
                float r = rPx / Ppu;
                int count = rec != null ? rec.TierCount(t) : 0;
                AddOrbitLine(t, r, count > 0 ? (float)p.orbitAlphaUsed : (float)p.orbitAlpha);

                var pivot = new GameObject("Orbit" + t).transform;
                pivot.SetParent(content, false);
                pivot.localRotation = Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() * 360.0));
                orbits.Add(new Orbit { pivot = pivot, omegaDeg = (float)(p.orbitOmega0Deg * System.Math.Pow(p.orbitR0Px / rPx, 1.5)) });
                if (count <= 0) continue;

                var tier = root.celestialTable.Get(t);
                Color color = tier != null ? tier.color : Color.white;
                int slot = present.IndexOf(t);
                float start = present.Count > 1 ? slot / (float)(present.Count - 1) * (1f - EntryTravel) * EntrySec : 0f;

                bool belt = t <= p.orbitBeltMaxTier && count > p.orbitBeltThreshold;
                if (belt)
                {
                    int dots = Mathf.Min(count, p.orbitBeltMaxDots);
                    for (int i = 0; i < dots; i++)
                    {
                        float a = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                        float rr = r + (float)((rng.NextDouble() * 2.0 - 1.0) * p.orbitBeltJitterPx / Ppu);
                        float size = (float)(p.orbitBeltDotMinPx + rng.NextDouble() * (p.orbitBeltDotMaxPx - p.orbitBeltDotMinPx));
                        var sr = MakeSprite("Dust", pivot, circle, -10);
                        sr.transform.localPosition = new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0f);
                        float s = size / Ppu / circleUnit;
                        sr.transform.localScale = new Vector3(s, s, 1f);
                        float shade = 0.6f + 0.4f * (float)rng.NextDouble();
                        var c = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
                        fades.Add(new Fade { sr = sr, color = c, start = start });
                        sr.color = Color.clear;
                    }
                    PlaceLabel(labelIndex++, r, count);
                    continue;
                }

                int max = t <= p.orbitBeltMaxTier ? p.orbitBeltThreshold : p.orbitIndividualMax;
                int n = Mathf.Min(count, max);
                float planetScale = (float)(tier != null ? tier.sizePx * p.orbitPlanetScale : 8.0) / Ppu / circleUnit;
                for (int i = 0; i < n; i++)
                {
                    float deg = 360f * i / n + (float)((rng.NextDouble() * 2.0 - 1.0) * 8.0);
                    float a = deg * Mathf.Deg2Rad;
                    var planet = MakeSprite("Planet", pivot, circle, -8);
                    planet.color = color;
                    var local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                    planet.transform.localPosition = local;
                    planet.transform.localScale = new Vector3(planetScale, planetScale, 1f);
                    var sh = MakeSprite("Shadow", planet.transform, shadow, -7);
                    float ss = circleUnit / shadowUnit;
                    sh.transform.localScale = new Vector3(ss, ss, 1f);
                    sh.transform.localRotation = Quaternion.Euler(0f, 0f, deg); // +x away from the star
                    sh.color = new Color(0f, 0f, 0f, (float)p.planetShadow);
                    fliers.Add(new Flier { tf = planet.transform, pivot = pivot, local = local, scale = planetScale, start = start });
                }
                if (count > n) PlaceLabel(labelIndex++, r, count);
            }

            // Stars made this run: bright points in the sky (at most skyStarsPerRun).
            int stars = 0;
            for (int t = p.orbitMaxTier + 1; t <= root.celestialTable.MaxTier && rec != null; t++)
            {
                int count = rec.TierCount(t);
                var tier = root.celestialTable.Get(t);
                for (int i = 0; i < count && stars < p.skyStarsPerRun; i++, stars++)
                {
                    var sr = MakeSprite("RunStar", content, dot, -22);
                    float x = (float)(-200.0 + rng.NextDouble() * 1100.0) / Ppu;
                    float y = (float)(-480.0 + rng.NextDouble() * 960.0) / Ppu;
                    sr.transform.position = CamCenter() + new Vector3(x, y, 0f);
                    float s = (float)(18.0 + 3.0 * (t - p.orbitMaxTier) + rng.NextDouble() * 10.0) / Ppu / dotUnit;
                    sr.transform.localScale = new Vector3(s, s, 1f);
                    fades.Add(new Fade { sr = sr, color = tier != null ? tier.color : Color.white, start = (1f - EntryTravel) * EntrySec });
                    sr.color = Color.clear;
                }
            }

            age = rec != null ? 0f : EntrySec;
            SetVisible(true);
            Tick(0f);
        }

        public void Hide() => SetVisible(false);

        /// <summary>Jumps the entry animation to its final state (click on the result screen).</summary>
        public void Skip()
        {
            if (age < EntrySec) age = EntrySec;
        }

        void SetVisible(bool on)
        {
            visible = on;
            system.gameObject.SetActive(on);
            sky.gameObject.SetActive(on);
            labelCanvas.gameObject.SetActive(on);
            if (!on) flash.gameObject.SetActive(false);
        }

        // ---------------- per frame ----------------

        void Update()
        {
            UpdateVanishGlow();
            if (visible) Tick(Time.unscaledDeltaTime);
        }

        void Tick(float dt)
        {
            float before = age;
            age += dt;
            foreach (var o in orbits) o.pivot.Rotate(0f, 0f, o.omegaDeg * dt);

            // Entry: planets fly in from the vanishing point; belts and sky stars fade in.
            Vector3 vanish = root.Planets.VanishPoint();
            float travel = EntryTravel * EntrySec;
            foreach (var f in fliers)
            {
                if (f.landed) continue;
                float k = Mathf.Clamp01((age - f.start) / travel);
                if (k >= 1f)
                {
                    f.tf.localPosition = f.local;
                    f.tf.localScale = new Vector3(f.scale, f.scale, 1f);
                    f.landed = true;
                    continue;
                }
                float e = 1f - (1f - k) * (1f - k);
                f.tf.position = Vector3.Lerp(vanish, f.pivot.TransformPoint(f.local), e);
                float s = f.scale * Mathf.Lerp(0.3f, 1f, e);
                f.tf.localScale = new Vector3(s, s, 1f);
            }
            if (before < EntrySec + travel)
                foreach (var f in fades)
                {
                    float k = Mathf.Clamp01((age - f.start) / travel);
                    var c = f.color;
                    c.a *= k;
                    f.sr.color = c;
                }

            // Central star: protostar breathes slowly.
            float glowA = stage == 0 ? 0.42f + 0.12f * Mathf.Sin(age * 1.3f) : 0.6f;
            var gc = starGlow.color;
            gc.a = glowA;
            starGlow.color = gc;

            // Ignition flash at the end of the entry, the first time a star is made (13 §3).
            if (ignition && age >= EntrySec)
            {
                float k = (age - EntrySec) / Mathf.Max(0.01f, (float)p.ignitionSec);
                float intensity = Mathf.Clamp01((float)p.effectIntensity);
                bool on = k < 1f && intensity > 0f;
                if (flash.gameObject.activeSelf != on) flash.gameObject.SetActive(on);
                if (on)
                {
                    float size = StarSize(p.starGlowPx, stage) * Mathf.Lerp(1f, (float)p.ignitionScale, k) / Ppu / dotUnit;
                    flash.transform.localScale = new Vector3(size, size, 1f);
                    flash.color = new Color(1f, 0.9f, 0.75f, (1f - k) * intensity);
                }
            }
        }

        void UpdateVanishGlow()
        {
            bool on = root.Phase == GamePhase.Run;
            if (vanishGlow.gameObject.activeSelf != on) vanishGlow.gameObject.SetActive(on);
            if (!on) return;
            vanishGlow.position = root.Planets.VanishPoint();
            float s = (float)(p.vanishGlowPx / Ppu) / dotUnit;
            vanishGlow.localScale = new Vector3(s, s, 1f);
            var c = StageColor(StarStage(root.Meta.highestTierCreated));
            c.a = (float)p.vanishGlowAlpha;
            vanishGlowSr.color = c;
        }

        // ---------------- central star ----------------

        /// <summary>0 protostar (below tier 9), 1 red dwarf, 2 sun-like, 3 blue giant (13 §3).</summary>
        int StarStage(int highestTier)
        {
            int first = p.orbitMaxTier + 1;
            if (highestTier < first) return 0;
            return Mathf.Clamp(highestTier - first + 1, 1, 3);
        }

        Color StageColor(int s)
        {
            if (s == 0) return p.protostarColor;
            var tier = root.celestialTable.Get(p.orbitMaxTier + s);
            return tier != null ? tier.color : Color.white;
        }

        void SetStar(int s)
        {
            var c = StageColor(s);
            starCore.color = s == 0 ? new Color(c.r, c.g, c.b, 0.85f) : c;
            starGlow.color = new Color(c.r, c.g, c.b, 0.6f);
            float core = StarSize(p.starCorePx, s) / Ppu / circleUnit;
            float glow = StarSize(p.starGlowPx, s) / Ppu / dotUnit;
            starCore.transform.localScale = new Vector3(core, core, 1f);
            starGlow.transform.localScale = new Vector3(glow, glow, 1f);
            flash.gameObject.SetActive(false);
        }

        static float StarSize(float[] sizes, int s) =>
            sizes != null && sizes.Length > 0 ? sizes[Mathf.Clamp(s, 0, sizes.Length - 1)] : 40f;

        bool MadeStar(RunRecord rec)
        {
            for (int t = p.orbitMaxTier + 1; t <= root.celestialTable.MaxTier; t++)
                if (rec.TierCount(t) > 0) return true;
            return false;
        }

        /// <summary>True when a run before this one already made a star (so no ignition this time).</summary>
        bool MadeStarBefore(MetaState m, RunRecord rec)
        {
            foreach (var h in m.runHistory)
                if (h != rec && h.run < rec.run && MadeStar(h)) return true;
            return false;
        }

        // ---------------- building ----------------

        Vector3 CamCenter()
        {
            var c = cam.transform.position;
            return new Vector3(c.x, c.y, 0f);
        }

        SpriteRenderer MakeSprite(string name, Transform parent, Sprite sprite, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            if (mat != null) sr.sharedMaterial = mat;
            sr.sortingOrder = order;
            return sr;
        }

        void AddOrbitLine(int tier, float r, float alpha)
        {
            var go = new GameObject("OrbitLine" + tier);
            go.transform.SetParent(content, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.alignment = LineAlignment.TransformZ;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (mat != null) lr.sharedMaterial = mat;
            lr.widthMultiplier = 2f / Ppu;
            lr.startColor = lr.endColor = new Color(0.75f, 0.82f, 1f, alpha);
            lr.sortingOrder = -15;
            UIBuilder.UseWhiteTexture(lr);
            lr.positionCount = OrbitSegments;
            for (int i = 0; i < OrbitSegments; i++)
            {
                float a = i / (float)OrbitSegments * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f));
            }
        }

        /// <summary>"×N" beside an orbit, at the lower right where orbits are furthest apart.</summary>
        void PlaceLabel(int index, float r, int count)
        {
            while (labels.Count <= index)
            {
                var t = UIBuilder.CreateText("Count" + labels.Count, labelCanvas.transform, root.Hud.Font, 22, TextAnchor.MiddleLeft,
                    new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(200f, 30f));
                t.color = new Color(0.85f, 0.9f, 1f, 0.9f);
                labels.Add(t);
            }
            var label = labels[index];
            float a = -35f * Mathf.Deg2Rad;
            Vector3 world = system.position + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * (r + 14f / Ppu);
            Vector2 screen = cam.WorldToScreenPoint(world);
            var canvasRect = (RectTransform)labelCanvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local))
                ((RectTransform)label.transform).anchoredPosition = local;
            label.text = string.Format(UIStrings.PlanetMulti, Fmt.Num(count));
            label.gameObject.SetActive(true);
        }

        void BuildSky()
        {
            var rng = new System.Random(11);
            float halfW = cam.orthographicSize * cam.aspect, halfH = cam.orthographicSize;
            for (int i = 0; i < p.skyBackgroundStars; i++)
            {
                var sr = MakeSprite("Star", sky, dot, -30);
                sr.transform.position = CamCenter() + new Vector3((float)(rng.NextDouble() * 2 - 1) * halfW, (float)(rng.NextDouble() * 2 - 1) * halfH, 0f);
                float s = (float)(3.0 + rng.NextDouble() * 5.0) / Ppu / dotUnit;
                sr.transform.localScale = new Vector3(s, s, 1f);
                sr.color = new Color(0.8f, 0.86f, 1f, 0.15f + (float)rng.NextDouble() * 0.45f);
            }
        }
    }
}

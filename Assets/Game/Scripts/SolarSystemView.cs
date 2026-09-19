using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// Result screen solar system (13 §3-§5, 14 §7), drawn in world space behind the result panel.
    /// Planets made this run orbit a display-only central star: one orbit per unlocked tier up to orbitMaxTier,
    /// radius r0 + (t − 1) × step, counter-clockwise at ω(r) = ω0 × (r0 / r)^1.5. Planets and the central star use the
    /// planet shader, lit from the central star. Tiers up to orbitBeltMaxTier with more than orbitBeltThreshold planets
    /// become an asteroid belt of plain dots; higher tiers show at most orbitIndividualMax planets, then "×N" on a dark
    /// backing at a fixed angle outside the orbit. Stars (tiers above orbitMaxTier) made this run shine in the sky.
    /// The central star follows the highest tier ever made (a faint protostar before the first star), with an ignition
    /// flash the first time a star is made (the radial flash rule of 15 §3). A tier made for the first time ever gets a
    /// "new body" label and one pulse of its orbit; a star tier gets them on one of its sky stars (15 §3). On entry the
    /// planets fly in from the vanishing point, lower tiers first. During runs a faint glow marks the vanishing point (13 §2).
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
            public PlanetVisual v;
            public Transform pivot;
            public Vector3 local;
            public float diameter;
            public float start;
            public bool landed;
        }

        struct Fade
        {
            public SpriteRenderer sr;
            public PlanetVisual v;
            public Color color;
            public float start;
        }

        struct NewOrbit
        {
            public LineRenderer line;
            public Color color;
        }

        struct NewStar
        {
            public PlanetVisual v;
            public float diameter;
        }

        struct Label
        {
            public RectTransform root;
            public Image backing;
            public Text text;
        }

        const int OrbitSegments = 128;
        const float EntryTravel = 0.5f; // share of the entry time one tier's planets travel

        GameRoot root;
        GameParams p;
        Camera cam;
        Material mat;
        Sprite circle, dot;
        float circleUnit = 1f, dotUnit = 1f;

        Transform system, content, vanishGlow;
        SpriteRenderer vanishGlowSr;
        ScreenFlash flash;
        PlanetVisual centralStar;
        Canvas labelCanvas;
        readonly List<Label> labels = new List<Label>();
        readonly List<Orbit> orbits = new List<Orbit>();
        readonly List<Flier> fliers = new List<Flier>();
        readonly List<Fade> fades = new List<Fade>();
        readonly List<NewOrbit> newOrbits = new List<NewOrbit>();
        readonly List<NewStar> newStars = new List<NewStar>();
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
            circleUnit = circle != null ? Mathf.Max(circle.bounds.size.x, 1e-3f) : 1f;
            dotUnit = Mathf.Max(dot.bounds.size.x, 1e-3f);

            system = new GameObject("System").transform;
            system.SetParent(transform, false);

            centralStar = PlanetVisual.Create("CentralStar", system, root.planetMaterial, -9);
            flash = ScreenFlash.Create("Ignition", system, root, -5);

            vanishGlowSr = MakeSprite("VanishGlow", transform, dot, -30);
            vanishGlow = vanishGlowSr.transform;

            labelCanvas = UIBuilder.CreateCanvas("SolarLabels", transform);
            labelCanvas.sortingOrder = 5;

            root.Result.EntrySkipped += Skip;
            root.RunStarted += Hide;
            SetVisible(false);
        }

        // ---------------- show / hide ----------------

        /// <summary>
        /// Builds the system for this run (null: no run yet). <paramref name="fresh"/> is true right after the run ended;
        /// only then do first-ever tiers get the "new body" highlight (14 §7), so reopening a save does not repeat it.
        /// </summary>
        public void Show(RunRecord rec, bool fresh)
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
            newOrbits.Clear();
            newStars.Clear();
            foreach (var l in labels) l.root.gameObject.SetActive(false);
            int labelIndex = 0;
            var rng = new System.Random(rec != null ? rec.run * 7919 : 1);

            stage = StarStage(root.HighestTierEver);
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
                var tier = root.celestialTable.Get(t);
                bool isNew = fresh && rec != null && ResultMath.IsNewTier(m, rec, t);
                var line = AddOrbitLine(t, r, count > 0 ? (float)p.orbitAlphaUsed : (float)p.orbitAlpha);
                if (isNew) newOrbits.Add(new NewOrbit { line = line, color = tier != null ? tier.color : Color.white });

                var pivot = new GameObject("Orbit" + t).transform;
                pivot.SetParent(content, false);
                pivot.localRotation = Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() * 360.0));
                orbits.Add(new Orbit { pivot = pivot, omegaDeg = (float)(p.orbitOmega0Deg * System.Math.Pow(p.orbitR0Px / rPx, 1.5)) });
                if (count <= 0) continue;

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
                        float shade = (0.6f + 0.4f * (float)rng.NextDouble()) * (float)p.orbitBeltDotBrightness;
                        var c = new Color(Mathf.Clamp01(color.r * shade), Mathf.Clamp01(color.g * shade), Mathf.Clamp01(color.b * shade), 1f);
                        fades.Add(new Fade { sr = sr, color = c, start = start });
                        sr.color = Color.clear;
                    }
                    PlaceLabel(labelIndex++, rPx + (float)p.orbitBeltJitterPx, count, isNew);
                    continue;
                }

                int max = t <= p.orbitBeltMaxTier ? p.orbitBeltThreshold : p.orbitIndividualMax;
                int n = Mathf.Min(count, max);
                float diameterPx = (float)(tier != null ? tier.sizePx * p.orbitPlanetScale : 8.0);
                for (int i = 0; i < n; i++)
                {
                    float deg = 360f * i / n + (float)((rng.NextDouble() * 2.0 - 1.0) * 8.0);
                    float a = deg * Mathf.Deg2Rad;
                    var v = PlanetVisual.Create("Planet", pivot, root.planetMaterial, -8);
                    v.SetLook(tier != null ? tier.look : null, (float)(rng.NextDouble() * 100.0), p.planetAxisPitchDeg);
                    v.SetLightPoint(center);
                    var local = new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, 0f);
                    v.transform.localPosition = local;
                    v.SetDiameter(diameterPx / Ppu);
                    fliers.Add(new Flier { v = v, pivot = pivot, local = local, diameter = diameterPx / Ppu, start = start });
                }
                if (count > n || isNew) PlaceLabel(labelIndex++, rPx + diameterPx * 0.5f, count > n ? count : 0, isNew);
            }

            // Stars made this run shine in the sky (at most skyStarsPerRun), outside the outermost orbit. A star tier made
            // for the first time always gets one, with the "new body" pulse and label (15 §3).
            float clearPx = (float)(p.orbitR0Px + (Mathf.Max(1, maxOrbit) - 1) * p.orbitStepPx + p.skyStarOrbitClearPx);
            int stars = 0;
            for (int t = p.orbitMaxTier + 1; t <= root.celestialTable.MaxTier && rec != null; t++)
            {
                int count = rec.TierCount(t);
                var tier = root.celestialTable.Get(t);
                bool isNew = fresh && ResultMath.IsNewTier(m, rec, t);
                for (int i = 0; i < count && (stars < p.skyStarsPerRun || (isNew && i == 0)); i++, stars++)
                {
                    var v = PlanetVisual.Create("RunStar", content, root.planetMaterial, -22);
                    v.SetLook(tier != null ? tier.look : null, (float)(rng.NextDouble() * 100.0), p.planetAxisPitchDeg);
                    v.transform.position = CamCenter() + SkyPosition(rng, clearPx) / Ppu;
                    float diameter = (float)((tier != null ? tier.sizePx : 100.0) * p.skyStarScale) / Ppu;
                    v.SetDiameter(diameter);
                    v.SetFade(0f);
                    fades.Add(new Fade { v = v, start = (1f - EntryTravel) * EntrySec });
                    if (!isNew || i > 0) continue;
                    newStars.Add(new NewStar { v = v, diameter = diameter });
                    PlaceLabelBelow(labelIndex++, v.transform.position, diameter * Ppu * 0.5f, NewTag());
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
            root.Backdrop.SetGlowCenter(on ? system.position : (Vector3?)null);
            labelCanvas.gameObject.SetActive(on);
            if (!on) flash.Hide();
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

            // Entry: planets fly in from the vanishing point; belts and sky stars fade in. Bodies stay unrotated (the
            // shader works in screen axes) while their pivots turn.
            Vector3 vanish = root.Planets.VanishPoint();
            float travel = EntryTravel * EntrySec;
            foreach (var f in fliers)
            {
                f.v.transform.rotation = Quaternion.identity;
                if (f.landed) continue;
                float k = Mathf.Clamp01((age - f.start) / travel);
                if (k >= 1f)
                {
                    f.v.transform.localPosition = f.local;
                    f.v.transform.rotation = Quaternion.identity;
                    f.v.SetDiameter(f.diameter);
                    f.landed = true;
                    continue;
                }
                float e = 1f - (1f - k) * (1f - k);
                f.v.transform.position = Vector3.Lerp(vanish, f.pivot.TransformPoint(f.local), e);
                f.v.SetDiameter(f.diameter * Mathf.Lerp(0.3f, 1f, e));
            }
            if (before < EntrySec + travel)
                foreach (var f in fades)
                {
                    float k = Mathf.Clamp01((age - f.start) / travel);
                    if (f.v != null) f.v.SetFade(k);
                    else
                    {
                        var c = f.color;
                        c.a *= k;
                        f.sr.color = c;
                    }
                }

            // Central star: the protostar's glow breathes.
            centralStar.SetGlowMul(stage == 0 ? 1f + (float)p.protostarPulse * Mathf.Sin(age * (float)p.protostarPulseSpeed) : 1f);

            UpdateNewOrbits();

            // Ignition flash at the end of the entry, the first time a star is made (13 §3): a radial flash from the star
            // whose reach grows with the glow (flash rule, 15 §3).
            if (ignition && age >= EntrySec)
            {
                float t = age - EntrySec;
                float dur = Mathf.Max(0.01f, (float)p.ignitionSec);
                float env = ScreenFlash.Envelope(t, (float)p.ignitionFlashRiseSec, dur);
                float radius = StarSize(p.starGlowPx, stage) * 0.5f * Mathf.Lerp(1f, (float)p.ignitionScale, Mathf.Clamp01(t / dur));
                flash.Show(system.position, p.ignitionFlashColor, env * (float)p.resultIgnitionFlashAlpha * root.EffectIntensity, radius);
            }
        }

        /// <summary>
        /// First-ever tiers: after the entry the orbit pulses newTierPulses times in the tier color, then stays brighter.
        /// A first-ever star tier: its sky star swells and brightens with the same pulses.
        /// </summary>
        void UpdateNewOrbits()
        {
            if (newOrbits.Count == 0 && newStars.Count == 0) return;
            float dur = Mathf.Max(0.01f, (float)p.newTierHighlightSec);
            float k = Mathf.Clamp01((age - EntrySec) / dur);
            float pulse = age < EntrySec ? 0f : Mathf.Sin(k * Mathf.PI * Mathf.Max(1, p.newTierPulses)) ;
            pulse = Mathf.Abs(pulse) * (1f - k);
            float baseAlpha = age < EntrySec ? (float)p.orbitAlphaUsed : Mathf.Lerp((float)p.orbitAlphaUsed, (float)p.newTierOrbitAlpha, k);
            float width = 2f / Ppu * (1f + ((float)p.newTierOrbitWidth - 1f) * pulse);
            foreach (var o in newOrbits)
            {
                var c = Color.Lerp(new Color(0.75f, 0.82f, 1f), o.color, age < EntrySec ? 0f : 1f);
                c.a = Mathf.Clamp01(baseAlpha + pulse * 0.4f);
                o.line.startColor = o.line.endColor = c;
                o.line.widthMultiplier = width;
            }
            foreach (var st in newStars)
            {
                st.v.SetDiameter(st.diameter * (1f + (float)p.newTierStarPulseScale * pulse));
                st.v.SetGlowMul(1f + pulse);
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
            var c = StageColor(StarStage(root.HighestTierEver));
            c.a = (float)p.vanishGlowAlpha;
            vanishGlowSr.color = c;
        }

        // ---------------- central star ----------------

        int StarStage(int highestTier) => BackdropView.StarStage(highestTier, p.orbitMaxTier, 4);

        Color StageColor(int s)
        {
            if (s == 0) return p.protostarColor;
            var tier = root.celestialTable.Get(p.orbitMaxTier + s);
            return tier != null ? tier.color : Color.white;
        }

        /// <summary>Protostar: the faint Star look from GameParams. Later stages: the look of that star tier.</summary>
        void SetStar(int s)
        {
            var tier = s > 0 ? root.celestialTable.Get(p.orbitMaxTier + s) : null;
            var look = tier != null ? tier.look : p.protostarLook;
            centralStar.SetLook(look, 3.7f, p.planetAxisPitchDeg);
            centralStar.SetDiameter(StarSize(p.starCorePx, s) / Ppu);
            centralStar.SetGlowMul(1f);
            flash.Hide();
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

        /// <summary>
        /// A random point (px from the screen center) in skyStarArea that is at least <paramref name="clearPx"/> from the
        /// central star; after a few misses the last try is used.
        /// </summary>
        Vector3 SkyPosition(System.Random rng, float clearPx)
        {
            var area = p.skyStarArea;
            var star = new Vector2((float)p.starOffsetPx, 0f);
            Vector2 pos = Vector2.zero;
            for (int tries = 0; tries < 24; tries++)
            {
                pos = new Vector2((float)(area.x + rng.NextDouble() * (area.z - area.x)), (float)(area.y + rng.NextDouble() * (area.w - area.y)));
                if ((pos - star).magnitude >= clearPx) break;
            }
            return new Vector3(pos.x, pos.y, 0f);
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

        LineRenderer AddOrbitLine(int tier, float r, float alpha)
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
            return lr;
        }

        /// <summary>
        /// "×N" (count > 0) and/or "new body" beside an orbit, on a dark backing at the fixed label angle, just outside
        /// the orbit's planets (<paramref name="outerPx"/> = orbit radius + planet radius or belt jitter), 14 §7.
        /// </summary>
        void PlaceLabel(int index, float outerPx, int count, bool isNew)
        {
            string text = count > 0 ? string.Format(UIStrings.PlanetMulti, Fmt.Num(count)) : string.Empty;
            if (isNew) text = text.Length > 0 ? NewTag() + "  " + text : NewTag();
            var label = PrepareLabel(index, text, out float w);

            // Center of the label on the radial line, far enough out that its half width clears the planets.
            float a = (float)p.orbitLabelAngleDeg * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            float halfExtent = Mathf.Abs(dir.x) * w * 0.5f + Mathf.Abs(dir.y) * 14f;
            PutLabel(label, system.position + dir * ((outerPx + (float)p.orbitLabelGapPx + halfExtent) / Ppu));
        }

        /// <summary>A label centered under a body (sky star): <paramref name="radiusPx"/> + gap below its center.</summary>
        void PlaceLabelBelow(int index, Vector3 world, float radiusPx, string text)
        {
            var label = PrepareLabel(index, text, out _);
            PutLabel(label, world + Vector3.down * ((radiusPx + (float)p.orbitLabelGapPx + 14f) / Ppu));
        }

        string NewTag() => "<color=#" + ColorUtility.ToHtmlStringRGB(p.newTierColor) + ">" + UIStrings.NewBody + "</color>";

        Label PrepareLabel(int index, string text, out float width)
        {
            while (labels.Count <= index)
            {
                var c = new Vector2(0.5f, 0.5f);
                var rt = UIBuilder.CreateRect("Label" + labels.Count, labelCanvas.transform, c, c, Vector2.zero, new Vector2(10f, 28f));
                var backing = rt.gameObject.AddComponent<Image>();
                backing.sprite = UIBuilder.SolidSprite();
                backing.raycastTarget = false;
                var t = UIBuilder.CreateText("Text", rt, root.Hud.Font, 21, TextAnchor.MiddleCenter, c, c, Vector2.zero, new Vector2(10f, 28f));
                t.supportRichText = true;
                labels.Add(new Label { root = rt, backing = backing, text = t });
            }
            var label = labels[index];
            label.text.text = text;
            label.text.color = new Color(0.88f, 0.92f, 1f, 1f);
            label.backing.color = p.orbitLabelBacking;
            width = label.text.preferredWidth + 16f;
            label.root.sizeDelta = new Vector2(width, 28f);
            ((RectTransform)label.text.transform).sizeDelta = new Vector2(width, 28f);
            return label;
        }

        void PutLabel(Label label, Vector3 world)
        {
            Vector2 screen = cam.WorldToScreenPoint(world);
            var canvasRect = (RectTransform)labelCanvas.transform;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out var local))
                label.root.anchoredPosition = local;
            label.root.gameObject.SetActive(true);
        }
    }
}

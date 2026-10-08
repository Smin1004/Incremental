using System;
using UnityEngine;

namespace Incremental
{
    /// <summary>
    /// 24 §19.1 블랙홀. The rare, big one: its cooldown is 40 seconds of holding. When it fills, the inhale comes first
    /// (inhaleSec): every dust on screen slows to a share of its speed and the edges of the screen darken, the warning
    /// that something large is coming. Then the gravity source wears the black hole of the ending (GameParams.blackHoleLook,
    /// the black disc with the accretion disc) for durationSec, plus a second per level of its duration node, and pulls
    /// everything dust can be in, the band outside the screen included, at pullMult times the pull and vortexMult times
    /// the swirl: the whole screen winds in. What is pulled is excited (24 §8) and is captured the normal way, which
    /// 24 §19.2 spreads out. At the end a flash (the ending's, ScreenFlash) and the gravity source is itself again.
    ///
    /// 24 §21.2 특이점, the evolved form: the cooldown is twinCooldownSec, and while the hole is open a second black hole
    /// circles the gravity source at twinOrbitShare gravity radii, once in twinOrbitSec, pulling everything as the first
    /// does (x twinPullMult) and capturing what comes within twinCaptureRadiusPx of it for the body being made, while
    /// holding, as the gravity source does. Two attractors, one of them on the move: the whole screen is whipped round
    /// in a figure of eight and wound in.
    ///
    /// The darkening and the flash follow the effect intensity and are gone at 0; the look of the gravity source, the
    /// second black hole and the pull are the ability itself and stay (its ribbon follows the intensity). Everything it
    /// reads is its row of the AbilityTable.
    /// </summary>
    public sealed class BlackHoleAbility : Ability
    {
        /// <summary>The frame of darkness: over the dust, the bodies and the rings, under the HUD canvas.</summary>
        const int DarkSortingOrder = 40;
        /// <summary>The release flash, just over the ending's (60).</summary>
        const int FlashSortingOrder = 61;
        /// <summary>The second black hole: with the forming body (11), over the dust, the satellites and the rings.</summary>
        const int TwinSortingOrder = 11;
        const double TwoPi = Math.PI * 2.0;

        enum Phase { Off, Inhale, Hole, Release }

        Phase phase;
        /// <summary>Seconds since the trigger.</summary>
        double age;
        /// <summary>This trigger's black hole length, the row's seconds plus the duration nodes.</summary>
        double duration;
        int capturedFrom, twinCapturedFrom;
        ScreenFlash flash;
        MeshRenderer dark;
        Mesh darkMesh;
        readonly Vector3[] darkVerts = new Vector3[8];
        readonly Color[] darkColors = new Color[8];
        /// <summary>24 §21.2: the second black hole: whether this trigger has one, its orbit angle, where it is and its orbit radius (world units).</summary>
        PlanetVisual twin;
        TrailRenderer twinTrail;
        bool twinOn, twinShown;
        double twinAngle;
        Vector2 twinPos;
        float twinRadius;

        BlackHoleDef D => root.abilityTable.blackHole;

        public override float EffectAge => phase != Phase.Off ? (float)age : -1f;

        /// <summary>True while the black hole itself is on (after the inhale, before the release): for checks and the perf bench.</summary>
        public bool HoleOpen => phase == Phase.Hole;
        /// <summary>24 §21.2: true while the second black hole is out, and where it is, world units.</summary>
        public bool TwinOpen => twinOn && phase == Phase.Hole;
        public Vector2 TwinPosition => twinPos;

        /// <summary>One at a time: a charge that fills during one waits for it to finish.</summary>
        protected override bool Ready => phase == Phase.Off;

        double InhaleEnd => Math.Max(0.0, D.inhaleSec);
        double HoleEnd => InhaleEnd + duration;
        double ReleaseEnd => HoleEnd + Math.Max(0.0, D.releaseSec);

        protected override void Build(Transform parent)
        {
            flash = ScreenFlash.Create("BlackHoleFlash", parent, root, FlashSortingOrder);

            // The darkness is a frame: a quad ring from the edge of the view (dark) to a rectangle inside it (clear), so
            // the edges go first and the middle, where the player looks, stays as it is.
            var go = new GameObject("BlackHoleDark", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            darkMesh = new Mesh { name = "BlackHoleDark" };
            darkMesh.MarkDynamic();
            var tris = new int[24];
            for (int i = 0; i < 4; i++)
            {
                int o0 = i, o1 = (i + 1) % 4, i0 = 4 + i, i1 = 4 + (i + 1) % 4;
                tris[i * 6] = o0; tris[i * 6 + 1] = o1; tris[i * 6 + 2] = i1;
                tris[i * 6 + 3] = o0; tris[i * 6 + 4] = i1; tris[i * 6 + 5] = i0;
            }
            darkMesh.vertices = darkVerts;
            darkMesh.colors = darkColors;
            darkMesh.triangles = tris;
            go.GetComponent<MeshFilter>().sharedMesh = darkMesh;
            dark = go.GetComponent<MeshRenderer>();
            if (root.unlitMaterial != null) dark.sharedMaterial = root.unlitMaterial;
            dark.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dark.receiveShadows = false;
            dark.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            dark.sortingOrder = DarkSortingOrder;
            UIBuilder.UseWhiteTexture(dark);
            dark.enabled = false;

            // 24 §21.2: the second black hole wears the look the first does, with a ribbon behind it (on a child of its
            // own: a GameObject holds one renderer).
            twin = PlanetVisual.Create("BlackHoleTwin", parent, root.planetMaterial, TwinSortingOrder);
            var ribbon = new GameObject("Ribbon");
            ribbon.transform.SetParent(twin.transform, false);
            twinTrail = ribbon.AddComponent<TrailRenderer>();
            if (root.unlitMaterial != null) twinTrail.sharedMaterial = root.unlitMaterial;
            twinTrail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            twinTrail.receiveShadows = false;
            twinTrail.alignment = LineAlignment.TransformZ;
            twinTrail.textureMode = LineTextureMode.Stretch;
            twinTrail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
            twinTrail.sortingOrder = TwinSortingOrder - 1;
            twinTrail.autodestruct = false;
            UIBuilder.UseWhiteTexture(twinTrail);
            twin.gameObject.SetActive(false);
        }

        protected override void Trigger(in EffectiveStats s)
        {
            var d = D;
            phase = Phase.Inhale;
            age = 0;
            duration = Math.Max(0.0, d.Duration(Level));
            capturedFrom = root.Dust.CapturedCount;
            twinCapturedFrom = root.Dust.ForceCapturedCount;
            // 24 §21.2: evolved, the second black hole comes with it, starting anywhere on its circle.
            twinOn = Level.evolved;
            twinAngle = UnityEngine.Random.value * TwoPi;
            if (twinOn)
            {
                twin.SetLook(p.blackHoleLook, UnityEngine.Random.value, p.planetAxisPitchDeg);
                twin.SetLightDirection(new Vector2(0.45f, 0.9f).normalized);
                twin.SetFade(1f);
            }
            Log("fired at " + RunClock + ": inhale " + InhaleEnd.ToString("0.##") + "s (dust slows to x" + d.slowFactor.ToString("0.##") +
                "), black hole " + duration.ToString("0.##") + "s (" + d.durationSec.ToString("0.##") + " + " + Level.duration.ToString("0.##") +
                " from the tree), pull x" + d.pullMult.ToString("0.##") + " over " + (root.Dust.Count - root.Dust.OutsideCount) + " dust on screen and " +
                root.Dust.OutsideCount + " outside, vortex x" + d.vortexMult.ToString("0.##") + ", release " + d.releaseSec.ToString("0.##") + "s" +
                (twinOn ? "; a second black hole at " + (s.gravityRadius * d.twinOrbitShare).ToString("F0") + " px, round in " + d.twinOrbitSec.ToString("0.##") +
                    "s, pull x" + (d.pullMult * d.twinPullMult).ToString("0.##") + ", capturing within " + d.twinCaptureRadiusPx.ToString("F0") + " px" : ""));
        }

        protected override void Step(double dt, bool holding, in EffectiveStats s)
        {
            if (phase == Phase.Off) return;
            var d = D;
            age += dt;
            if (age < InhaleEnd)
            {
                // The field slows down to slowFactor of its speed by the end of the inhale, a little every tick.
                double slow = Math.Min(1.0, Math.Max(1e-3, d.slowFactor));
                root.Dust.SpeedMul = (float)Math.Pow(slow, dt / InhaleEnd);
                return;
            }
            root.Dust.SpeedMul = 1f;
            if (age < HoleEnd)
            {
                if (phase != Phase.Hole)
                {
                    phase = Phase.Hole;
                    Log("open at " + RunClock + " for " + duration.ToString("0.##") + "s" + (twinOn ? ", the second black hole with it" : ""));
                }
                // The pull over everything dust can be in: the radius is set so that even the far corner of the band
                // outside the screen is inside the full-strength part of the falloff.
                Rect all = root.Dust.BoundsOf(root.CameraArea());
                Vector2 g = root.GravityWorld;
                float dx = Mathf.Max(g.x - all.xMin, all.xMax - g.x), dy = Mathf.Max(g.y - all.yMin, all.yMax - g.y);
                float farthest = Mathf.Sqrt(dx * dx + dy * dy);
                float radius = farthest / Mathf.Max(0.05f, (float)p.pullFullRatio) + 1f;
                float accel = (float)(s.pullAccel * d.pullMult / p.pixelsPerUnit);
                float vortex = (float)(p.vortexShare * d.vortexMult);
                system.AddForce(DustForce.Global(g, radius, accel, vortex));
                root.Cursor.SetLookOverride(p.blackHoleLook, d.sizePx);
                if (twinOn)
                {
                    // 24 §21.2: the second black hole goes round the gravity source, counter-clockwise like everything
                    // else, pulling the same everything toward itself and eating what reaches it.
                    float ppu = (float)p.pixelsPerUnit;
                    twinAngle = (twinAngle + TwoPi * dt / Math.Max(1e-3, d.twinOrbitSec)) % TwoPi;
                    twinRadius = (float)(s.gravityRadius * Math.Max(0.0, d.twinOrbitShare) / ppu);
                    twinPos = g + new Vector2((float)Math.Cos(twinAngle), (float)Math.Sin(twinAngle)) * twinRadius;
                    float twinReach = radius + twinRadius;
                    system.AddForce(DustForce.GlobalCapture(twinPos, twinReach, accel * (float)Math.Max(0.0, d.twinPullMult), vortex,
                        (float)(Math.Max(0.0, d.twinCaptureRadiusPx) / ppu)));
                }
                return;
            }
            if (phase == Phase.Hole)
            {
                phase = Phase.Release;
                root.Cursor.ClearLookOverride();
                int twinTook = root.Dust.ForceCapturedCount - twinCapturedFrom;
                Log("closed at " + RunClock + ": " + (root.Dust.CapturedCount - capturedFrom) + " dust captured since it fired" +
                    (twinOn ? " (" + twinTook + " of them by the second black hole)" : "") + ", " +
                    (root.Dust.Count - root.Dust.OutsideCount) + " on screen and " + root.Dust.OutsideCount + " outside it now");
            }
            if (age >= ReleaseEnd) End();
        }

        void End()
        {
            phase = Phase.Off;
            twinOn = false;
            root.Dust.SpeedMul = 1f;
            root.Cursor.ClearLookOverride();
            flash.Hide();
            if (dark.enabled) dark.enabled = false;
            HideTwin();
        }

        void HideTwin()
        {
            if (!twinShown) return;
            twinShown = false;
            twin.gameObject.SetActive(false);
        }

        protected override void OnClear()
        {
            if (phase != Phase.Off) End();
            age = 0;
        }

        public override void Render(bool show, float lead)
        {
            float intensity = root.EffectIntensity;
            var d = D;
            RenderTwin(show, lead, d, intensity);

            bool on = show && phase != Phase.Off && intensity > 0f;
            if (!on)
            {
                if (dark.enabled) dark.enabled = false;
                flash.Hide();
                return;
            }
            double t = age + lead;

            // The darkness: in over the inhale, full while the hole is open, out over the release.
            float k;
            if (t < InhaleEnd) k = InhaleEnd > 0 ? (float)(t / InhaleEnd) : 1f;
            else if (t < HoleEnd) k = 1f;
            else k = ReleaseEnd > HoleEnd ? 1f - Mathf.Clamp01((float)((t - HoleEnd) / (ReleaseEnd - HoleEnd))) : 0f;
            float alpha = Mathf.Clamp01((float)d.darkenAlpha) * Mathf.Clamp01(k) * intensity;
            if (alpha > 0.002f)
            {
                DrawDark(alpha, Mathf.Clamp01((float)d.darkenReach));
                if (!dark.enabled) dark.enabled = true;
            }
            else if (dark.enabled) dark.enabled = false;

            // The release flash, the ending's flash at the gravity source (flash rule, 15 §3).
            float env = t >= HoleEnd ? ScreenFlash.Envelope((float)(t - HoleEnd), (float)d.flashRiseSec, (float)Math.Max(0.01, d.releaseSec)) : 0f;
            float flashAlpha = env * Mathf.Clamp01((float)d.flashAlpha) * intensity;
            if (flashAlpha > 0f) flash.Show(root.GravityWorld, d.flashColor, flashAlpha);
            else flash.Hide();
        }

        /// <summary>24 §21.2: the second black hole, where it is a frame's lead on; hidden while paused like the gravity source's own look.</summary>
        void RenderTwin(bool show, float lead, BlackHoleDef d, float intensity)
        {
            bool visible = show && twinOn && phase == Phase.Hole && !root.Paused;
            if (!visible)
            {
                HideTwin();
                return;
            }
            float ppu = (float)p.pixelsPerUnit;
            double angle = twinAngle + TwoPi * lead / Math.Max(1e-3, d.twinOrbitSec);
            Vector2 g = root.GravityWorld;
            Vector2 at = g + new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * twinRadius;
            twin.transform.position = new Vector3(at.x, at.y, 0f);
            // 22 §6: it keeps the floor the bodies keep as the camera pulls back, like the first.
            float sizePx = (float)(d.sizePx * Math.Max(0.05, d.twinSizeMult));
            float diameter = (float)(root.RunBodyPx(sizePx) / ppu);
            twin.SetDiameter(diameter);
            float scale = diameter / Mathf.Max(1e-6f, sizePx / ppu);
            bool ribbon = d.twinTrailSec > 0 && d.twinTrailWidthPx > 0 && intensity > 0f;
            twinTrail.time = (float)d.twinTrailSec;
            twinTrail.widthMultiplier = (float)(d.twinTrailWidthPx / ppu) * scale;
            Color rc = d.twinTrailColor;
            rc.a *= intensity;
            twinTrail.startColor = rc;
            twinTrail.endColor = new Color(rc.r, rc.g, rc.b, 0f);
            twinTrail.emitting = ribbon;
            if (!twinShown)
            {
                twinShown = true;
                twin.gameObject.SetActive(true);
                // It appears where it is now; without this the ribbon would be drawn from where it last was.
                twinTrail.Clear();
            }
        }

        /// <summary>The frame over the camera view: black at the edge, clear <paramref name="reach"/> of the half screen in.</summary>
        void DrawDark(float alpha, float reach)
        {
            var cam = root.Cam;
            float h = cam.orthographicSize * 1.05f, w = h * cam.aspect;
            var c = cam.transform.position;
            float ih = h * (1f - reach), iw = w * (1f - reach);
            darkVerts[0] = new Vector3(c.x - w, c.y - h, 0f);
            darkVerts[1] = new Vector3(c.x + w, c.y - h, 0f);
            darkVerts[2] = new Vector3(c.x + w, c.y + h, 0f);
            darkVerts[3] = new Vector3(c.x - w, c.y + h, 0f);
            darkVerts[4] = new Vector3(c.x - iw, c.y - ih, 0f);
            darkVerts[5] = new Vector3(c.x + iw, c.y - ih, 0f);
            darkVerts[6] = new Vector3(c.x + iw, c.y + ih, 0f);
            darkVerts[7] = new Vector3(c.x - iw, c.y + ih, 0f);
            var edge = new Color(0f, 0f, 0f, alpha);
            var clear = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < 4; i++)
            {
                darkColors[i] = edge;
                darkColors[4 + i] = clear;
            }
            darkMesh.vertices = darkVerts;
            darkMesh.colors = darkColors;
            darkMesh.RecalculateBounds();
        }
    }
}

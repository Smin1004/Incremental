using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Incremental
{
    /// <summary>
    /// The supernova sequence on the ending run's result screen (15 §4.3), and the credits (§4.4). No panel: the run's
    /// solar system at the screen center around a blue giant (SolarSystemView in ending mode). Seconds from the start:
    /// entry (resultEntrySec) → calm, the star's pulse grows faster and larger and turns white (supernovaCalmEndSec) →
    /// collapse to a point, the corona drawn in (supernovaCollapseEndSec) → blast, a radial flash (flash rule, 15 §3) and
    /// light at the center (supernovaBlastEndSec) → shock ring; every body it passes becomes outward-flying dust (the run's
    /// dust renderer) and the orbit lines it passes fade (supernovaShockEndSec) → remnant, the dust drifts, the palette
    /// turns to the remnant nebula and a black hole fades in (supernovaRemnantEndSec) → credits, one line after another →
    /// "continue (Space)" → skill tree, then endless play.
    /// Input is ignored before the blast; after it Space or a click jumps straight to "continue". At effect intensity 0
    /// there is no flash and the shock ring keeps supernovaShockMinIntensity of its brightness; the rest is unchanged.
    /// Reaching "continue" (watched or skipped) marks the ending seen and saves; a replay (debug key) changes nothing.
    /// Timing uses unscaled time, like the rest of the result screen. Render and UI only.
    /// </summary>
    public sealed class EndingView : MonoBehaviour
    {
        static readonly int IdColor = Shader.PropertyToID("_Color");
        static readonly int IdRing = Shader.PropertyToID("_Ring");

        GameRoot root;
        GameParams p;
        ScreenFlash flash;
        MeshRenderer ring, core;
        MaterialPropertyBlock ringMpb, coreMpb;
        SpriteRenderer fadeIn;
        Canvas canvas;
        Text[] credits;
        Text continueText;

        bool replay;
        bool playing;
        bool atEnd;
        bool blasted;
        bool remnant;
        bool blackHole;
        float age;
        float pulsePhase;

        /// <summary>The credit lines in order (UIStrings keys; placeholder values until the developer sets them, 15 §4.4).</summary>
        public static readonly string[] CreditKeys = { "credits.title", "credits.made_by", "credits.engine", "credits.thanks", "credits.last" };

        public bool IsPlaying => playing;
        public bool IsReplay => replay;
        /// <summary>Seconds since the sequence started (capture clock).</summary>
        public float Age => age;
        /// <summary>After the blast and before "continue": Space / click (or the bot) may skip.</summary>
        public bool CanSkip => playing && !atEnd && age >= (float)p.supernovaCollapseEndSec;
        /// <summary>"Continue" is showing.</summary>
        public bool AtEnd => playing && atEnd;
        /// <summary>The remnant dust is on screen (GameRoot shows the dust renderer between runs only then).</summary>
        public bool ShowsDust => playing && blasted;
        public float CreditsEndSec => (float)(p.supernovaRemnantEndSec + CreditKeys.Length * p.creditLineSec);

        /// <summary>The ending was reached but its sequence not seen to the end: play it instead of the result screen (15 §4.5).</summary>
        public static bool IsUnseen(MetaState m) => m.endingRun > 0 && !m.endingSeen;

        /// <summary>This finished run is the ending run whose sequence has not been seen.</summary>
        public static bool PlaysFor(MetaState m, RunRecord rec) =>
            rec != null && rec.endReason == EndReason.Ending && rec.run == m.endingRun && !m.endingSeen;

        public void Init(GameRoot gameRoot)
        {
            root = gameRoot;
            p = root.gameParams;

            flash = ScreenFlash.Create("SupernovaFlash", transform, root, 60);

            ring = MakeFx("ShockRing", 55);
            core = MakeFx("CoreLight", 56);
            ringMpb = new MaterialPropertyBlock();
            coreMpb = new MaterialPropertyBlock();

            fadeIn = new GameObject("FadeIn").AddComponent<SpriteRenderer>();
            fadeIn.transform.SetParent(transform, false);
            fadeIn.sprite = UIBuilder.SolidSprite();
            if (root.unlitMaterial != null) fadeIn.sharedMaterial = root.unlitMaterial;
            fadeIn.sortingOrder = 80;
            fadeIn.enabled = false;

            canvas = UIBuilder.CreateCanvas("Credits", transform);
            canvas.sortingOrder = 30;
            var rect = (RectTransform)canvas.transform;
            var c = new Vector2(0.5f, 0.5f);
            credits = new Text[CreditKeys.Length];
            for (int i = 0; i < credits.Length; i++)
            {
                int size = i == 0 ? p.creditTitleFontSize : p.creditFontSize;
                credits[i] = UIBuilder.CreateText("Credit" + i, rect, root.Hud.Font, size, TextAnchor.MiddleCenter, c, c,
                    p.creditOffsetPx, new Vector2(1600f, size * 2f));
                credits[i].text = UIStrings.Get(CreditKeys[i]);
                credits[i].gameObject.SetActive(false);
            }
            var bottom = new Vector2(0.5f, 0f);
            continueText = UIBuilder.CreateText("Continue", rect, root.Hud.Font, p.continueFontSize, TextAnchor.MiddleCenter, bottom, bottom,
                p.continueOffsetPx, new Vector2(800f, p.continueFontSize * 2f));
            continueText.text = UIStrings.EndingContinue;
            canvas.gameObject.SetActive(false);

            root.RunStarted += Stop;
        }

        MeshRenderer MakeFx(string name, int order)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = PlanetVisual.Quad();
            var r = go.GetComponent<MeshRenderer>();
            if (root.fxMaterial != null) r.sharedMaterial = root.fxMaterial;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sortingOrder = order;
            r.enabled = false;
            return r;
        }

        // ---------------- start / stop ----------------

        /// <summary>
        /// Starts the sequence for this run (null: an empty system around the blue giant). <paramref name="isReplay"/>: the
        /// debug replay, which leaves the save alone.
        /// </summary>
        public void Play(RunRecord rec, bool isReplay)
        {
            replay = isReplay;
            playing = true;
            atEnd = false;
            blasted = false;
            remnant = false;
            blackHole = false;
            age = 0f;
            pulsePhase = 0f;
            root.Result.Hide();
            root.Tree.Hide();
            root.Dust.Clear();
            root.Solar.Show(rec, false, true);
            root.Backdrop.SetStageOverride(root.Solar.BlueGiantStage, 0f);
            canvas.gameObject.SetActive(true);
            foreach (var t in credits) t.gameObject.SetActive(false);
            continueText.gameObject.SetActive(false);
            flash.Hide();
            ring.enabled = false;
            core.enabled = false;
            Step(0f);
            Debug.Log(isReplay ? "[Ending] sequence replay (save untouched)" : $"[Ending] sequence for run {(rec != null ? rec.run : 0)}");
        }

        /// <summary>Ends the sequence without going on (a new run started, a save was loaded).</summary>
        public void Stop()
        {
            if (!playing) return;
            playing = false;
            flash.Hide();
            ring.enabled = false;
            core.enabled = false;
            fadeIn.enabled = false;
            canvas.gameObject.SetActive(false);
            root.Backdrop.ClearStageOverride();
            root.Dust.Clear();
            root.Solar.Hide();
        }

        /// <summary>After the blast: jump to "continue" with the remnant settled (Space, click, the bot).</summary>
        public void Skip()
        {
            if (!CanSkip) return;
            if (!blasted) Blast();
            root.Solar.Shock(float.MaxValue, Scatter);
            float step = 1f / 30f;
            for (float t = 0f; t < (float)p.supernovaSkipSettleSec; t += step) DriftDust(step);
            if (!remnant)
            {
                remnant = true;
                root.Backdrop.SetStageOverride(root.Solar.RemnantStage, 0f);
            }
            age = CreditsEndSec;
            Step(0f);
            ReachEnd();
        }

        /// <summary>"Continue": the skill tree, then endless play.</summary>
        public void Continue()
        {
            if (!AtEnd) return;
            Stop();
            root.Tree.Show();
        }

        void ReachEnd()
        {
            if (atEnd) return;
            atEnd = true;
            foreach (var t in credits) t.gameObject.SetActive(false);
            continueText.gameObject.SetActive(true);
            var m = root.Meta;
            if (!replay && m.endingRun > 0 && !m.endingSeen)
            {
                m.endingSeen = true;
                root.Save();
                Debug.Log("[Ending] sequence seen; saved");
            }
        }

        // ---------------- per frame ----------------

        void Update()
        {
            if (!playing || (root.Gallery != null && root.Gallery.IsOpen)) return;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            // A click counts on release: the press is already over, so it cannot land on the tree that "continue" opens.
            bool press = (kb != null && kb.spaceKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasReleasedThisFrame);
            if (press)
            {
                if (atEnd)
                {
                    Continue();
                    return;
                }
                if (CanSkip)
                {
                    Skip();
                    return;
                }
            }
            float dt = Time.unscaledDeltaTime;
            if (!atEnd) age += dt;
            Step(dt);
            if (!atEnd && age >= CreditsEndSec) ReachEnd();
        }

        void Step(float dt)
        {
            float entry = root.Solar.EntrySec;
            float calmEnd = (float)p.supernovaCalmEndSec;
            float collapseEnd = (float)p.supernovaCollapseEndSec;
            float blastEnd = (float)p.supernovaBlastEndSec;
            float shockEnd = (float)p.supernovaShockEndSec;
            float intensity = root.EffectIntensity;

            // Fade in from the dark the run ended on.
            float fin = Mathf.Clamp01(age / Mathf.Max(0.01f, (float)p.endingFadeSec));
            fadeIn.enabled = fin < 1f;
            if (fadeIn.enabled)
            {
                SupernovaRunFx.Cover(fadeIn, root.Cam);
                fadeIn.color = new Color(0f, 0f, 0f, (1f - fin) * (float)p.endingFadeAlpha);
            }

            UpdateStar(dt, entry, calmEnd, collapseEnd);

            if (age >= collapseEnd && !blasted) Blast();

            // Flash from the center (flash rule) and the light at the center.
            float tb = age - collapseEnd;
            float env = ScreenFlash.Envelope(tb, (float)p.supernovaFlashRiseSec, (float)p.supernovaFlashSec);
            if (blasted && env > 0f) flash.Show(root.Solar.Center, p.supernovaFlashColor, env * (float)p.supernovaFlashAlpha * intensity);
            else flash.Hide();

            // Light at the center while the flash lasts.
            DrawCore(blasted ? (float)p.supernovaCoreGlow * env * intensity : 0f);

            // Shock ring (from the blast end), scattering what it passes.
            float ringMul = Mathf.Lerp((float)p.supernovaShockMinIntensity, 1f, intensity);
            if (blasted && age >= blastEnd && age < shockEnd)
            {
                float k = Mathf.Clamp01((age - blastEnd) / Mathf.Max(0.01f, shockEnd - blastEnd));
                float e = 1f - Mathf.Pow(1f - k, Mathf.Max(0.1f, (float)p.supernovaShockEase));
                float rPx = Mathf.Lerp((float)p.supernovaShockStartPx, (float)p.supernovaShockEndPx, e);
                root.Solar.Shock(rPx, Scatter);
                float ringFade = 1f - Mathf.SmoothStep(0.65f, 1f, k);
                DrawRing(rPx, ringFade * (float)p.supernovaShockStrength * ringMul);
            }
            else ring.enabled = false;
            if (age >= shockEnd) root.Solar.Shock(float.MaxValue, Scatter);

            // Remnant: palette and black hole.
            if (age >= shockEnd && !remnant)
            {
                remnant = true;
                root.Backdrop.SetStageOverride(root.Solar.RemnantStage, (float)p.remnantPaletteBlendSec);
            }

            if (blasted && dt > 0f) DriftDust(dt);

            // Credits: one line after another, each fading in and out.
            float lineSec = Mathf.Max(0.01f, (float)p.creditLineSec);
            float fade = Mathf.Clamp((float)p.creditFadeSec, 0.01f, lineSec * 0.5f);
            for (int i = 0; i < credits.Length; i++)
            {
                float t = age - (float)p.supernovaRemnantEndSec - i * lineSec;
                bool on = !atEnd && t >= 0f && t < lineSec;
                if (credits[i].gameObject.activeSelf != on) credits[i].gameObject.SetActive(on);
                if (!on) continue;
                float a = Mathf.Min(Mathf.Clamp01(t / fade), Mathf.Clamp01((lineSec - t) / fade));
                var col = p.creditColor;
                col.a = a;
                credits[i].color = col;
            }
            if (atEnd)
            {
                var col = continueText.color;
                col.a = 1f - (float)p.continuePulseDepth * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (float)p.continuePulseSpeed));
                continueText.color = col;
            }
        }

        /// <summary>
        /// Central star: steady through the entry; the calm makes its pulse faster and larger and whitens it; the collapse
        /// shrinks it to a point and draws the corona in; from blackHoleFadeStartSec the black hole fades in.
        /// </summary>
        void UpdateStar(float dt, float entry, float calmEnd, float collapseEnd)
        {
            var star = root.Solar.CentralStar;
            int gs = root.Solar.BlueGiantStage;
            float basePx = root.Solar.CentralSizePx(gs);
            float ppu = (float)p.pixelsPerUnit;

            if (age >= (float)p.blackHoleFadeStartSec)
            {
                if (!blackHole)
                {
                    blackHole = true;
                    root.Solar.SetCentralLook(p.blackHoleLook, root.Solar.CentralSizePx(root.Solar.RemnantStage));
                }
                star.SetFade(Mathf.Clamp01((age - (float)p.blackHoleFadeStartSec) / Mathf.Max(0.01f, (float)p.blackHoleFadeSec)));
                return;
            }
            if (age >= collapseEnd)
            {
                if (star.gameObject.activeSelf) star.gameObject.SetActive(false);
                return;
            }
            if (age < entry) return;

            if (age < calmEnd)
            {
                float k = Mathf.Clamp01((age - entry) / Mathf.Max(0.01f, calmEnd - entry));
                float hz = Mathf.Lerp((float)p.supernovaPulseStartHz, (float)p.supernovaPulseEndHz, k);
                pulsePhase += dt * hz * Mathf.PI * 2f;
                float amp = Mathf.Lerp((float)p.supernovaPulseStartAmp, (float)p.supernovaPulseEndAmp, k * k);
                float s = Mathf.Sin(pulsePhase);
                star.SetDiameter(basePx * (1f + amp * s) / ppu);
                star.SetGlowMul(1f + amp * (float)p.supernovaCalmGlowPulse * s + k * (float)p.supernovaCalmGlowGain);
                star.SetWhiten((float)p.supernovaWhiten * k);
                return;
            }

            float c = Mathf.Clamp01((age - calmEnd) / Mathf.Max(0.01f, collapseEnd - calmEnd));
            float shrink = 1f - c * c;
            star.SetDiameter(Mathf.Max(0.5f, basePx * shrink) / ppu);
            star.SetGlowSizeMul(shrink * shrink);
            star.SetGlowMul(1f + (float)p.supernovaCalmGlowGain + (float)p.supernovaCollapseGlowGain * c);
            star.SetWhiten((float)p.supernovaWhiten + (1f - (float)p.supernovaWhiten) * c);
        }

        /// <summary>The blast: the star throws out its own dust (supernovaEjectaDust) from the center.</summary>
        void Blast()
        {
            blasted = true;
            var star = root.Solar.CentralStar;
            if (star.gameObject.activeSelf && !blackHole) star.gameObject.SetActive(false);
            Vector3 c = root.Solar.Center;
            float ppu = (float)p.pixelsPerUnit;
            for (int i = 0; i < p.supernovaEjectaDust; i++)
            {
                Vector2 dir = root.Dust.VisualDir();
                float speed = (float)p.supernovaDustSpeedPx * (1f + (float)p.supernovaDustSpeedJitter * (root.Dust.VisualValue() * 2f - 1f)) / ppu;
                float r = root.Dust.VisualValue() * (float)p.supernovaEjectaRadiusPx / ppu;
                root.Dust.AddFree(new Vector2(c.x, c.y) + dir * r, dir * speed);
            }
        }

        /// <summary>A body the shock passed becomes dust flying outward from the center (15 §4.3).</summary>
        void Scatter(Vector3 pos, float diameterPx)
        {
            float ppu = (float)p.pixelsPerUnit;
            Vector2 c = root.Solar.Center;
            Vector2 at = pos;
            Vector2 outward = at - c;
            outward = outward.sqrMagnitude > 1e-8f ? outward.normalized : root.Dust.VisualDir();
            int n = diameterPx <= (float)p.orbitBeltDotMaxPx + 0.01f
                ? p.supernovaDustPerBeltDot
                : Mathf.Clamp(Mathf.RoundToInt(diameterPx * (float)p.supernovaDustPerPx), p.supernovaDustPerPlanetMin, p.supernovaDustPerPlanetMax);
            float radius = diameterPx * 0.5f / ppu;
            for (int i = 0; i < n; i++)
            {
                Vector2 jitter = root.Dust.VisualDir() * (root.Dust.VisualValue() * radius);
                Vector2 dir = (outward + root.Dust.VisualDir() * (float)p.supernovaDustSpread).normalized;
                float speed = (float)p.supernovaDustSpeedPx * (1f + (float)p.supernovaDustSpeedJitter * (root.Dust.VisualValue() * 2f - 1f)) / ppu;
                root.Dust.AddFree(at + jitter, dir * speed);
            }
        }

        void DriftDust(float dt) =>
            root.Dust.TickFree(dt, (float)p.supernovaDustDrag, (float)(p.remnantDriftSpeedPx / p.pixelsPerUnit), root.CameraArea());

        void DrawRing(float radiusPx, float strength)
        {
            if (strength <= 0f)
            {
                ring.enabled = false;
                return;
            }
            float width = (float)p.supernovaShockWidthPx;
            float quadPx = radiusPx * 2f + width * 2f;
            float s = quadPx / (float)p.pixelsPerUnit;
            ring.transform.position = root.Solar.Center;
            ring.transform.localScale = new Vector3(s, s, 1f);
            var col = p.supernovaShockColor;
            ringMpb.SetColor(IdColor, new Color(col.r, col.g, col.b, strength));
            ringMpb.SetVector(IdRing, new Vector4(radiusPx * 2f / quadPx, width / quadPx, 0f, 0f));
            ring.SetPropertyBlock(ringMpb);
            ring.enabled = true;
        }

        /// <summary>Soft light at the center (FxRing's center glow alone) of supernovaCoreGlowPx.</summary>
        void DrawCore(float strength)
        {
            if (strength <= 0f)
            {
                core.enabled = false;
                return;
            }
            float s = (float)(p.supernovaCoreGlowPx / p.pixelsPerUnit);
            core.transform.position = root.Solar.Center;
            core.transform.localScale = new Vector3(s, s, 1f);
            var col = p.supernovaFlashColor;
            coreMpb.SetColor(IdColor, new Color(col.r, col.g, col.b, strength));
            coreMpb.SetVector(IdRing, new Vector4(0f, 1e-3f, 1f, 0f));
            core.SetPropertyBlock(coreMpb);
            core.enabled = true;
        }    }
}

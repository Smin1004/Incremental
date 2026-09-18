using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Incremental
{
    /// <summary>
    /// Debug keys. F1: overlay (FPS, dust count, tick ms). F2: performance mode (runtime override of dust_cap / spawn_rate,
    /// assets untouched). F3: +currency. F4: autoplay bot toggle. F12: screenshot to persistentDataPath.
    /// </summary>
    public sealed class DebugTools : MonoBehaviour
    {
        public double perfDustCap = 1000;
        public double perfSpawnRate = 100;
        public double cheatCurrency = 1000;

        GameRoot root;
        bool overlay;
        bool perfMode;
        float fpsTimer;
        int fpsFrames;
        float fps;

        public bool Overlay => overlay;
        public bool PerfMode => perfMode;
        public bool BotEnabled { get; set; }
        public float Fps => fps;

        public void Init(GameRoot gameRoot) { root = gameRoot; }

        void Update()
        {
            fpsFrames++;
            fpsTimer += Time.unscaledDeltaTime;
            if (fpsTimer >= 0.5f)
            {
                fps = fpsFrames / fpsTimer;
                fpsFrames = 0;
                fpsTimer = 0f;
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f1Key.wasPressedThisFrame) overlay = !overlay;
                if (kb.f2Key.wasPressedThisFrame) SetPerfMode(!perfMode);
                if (kb.f3Key.wasPressedThisFrame) root.Meta.currency += cheatCurrency;
                if (kb.f4Key.wasPressedThisFrame && root.Bot != null) root.Bot.SetEnabled(!root.Bot.Enabled);
                if (kb.f12Key.wasPressedThisFrame) Screenshot();
            }

            root.Hud.SetDebug(overlay
                ? string.Format(UIStrings.DebugOverlay, fps, root.Dust.Count, root.LastTickMs, root.MaxTickMs,
                    perfMode ? UIStrings.On : UIStrings.Off, BotEnabled ? UIStrings.On : UIStrings.Off)
                : string.Empty);
        }

        public void SetPerfMode(bool on)
        {
            perfMode = on;
            root.Override = new StatsOverride { enabled = on, dustCap = perfDustCap, spawnRate = perfSpawnRate };
        }

        public string Screenshot()
        {
            string file = Path.Combine(Application.persistentDataPath, "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            ScreenCapture.CaptureScreenshot(file);
            Debug.Log(string.Format(UIStrings.ScreenshotSaved, file));
            return file;
        }
    }
}

namespace Incremental
{
    /// <summary>All user-facing strings in one place (Korean). Placeholders are string.Format indices.</summary>
    public static class UIStrings
    {
        // HUD
        public const string CurrencyAndRun = "재화 {0}    런 {1}";
        public const string RunIncome = "이번 런 수입 {0}";
        public const string Stamina = "스태미나 {0} / {1}";

        // Temporary result (checkpoint A)
        public const string RunEndedTemp = "런 종료   수입 {0}\n스페이스: 다음 런";

        // Debug overlay
        public const string DebugOverlay = "FPS {0:F1}   먼지 {1}   틱 {2:F2} ms (최대 {3:F2})   성능모드 {4}   봇 {5}";
        public const string On = "켜짐";
        public const string Off = "꺼짐";
        public const string ScreenshotSaved = "스크린샷 저장: {0}";
    }
}

namespace Incremental
{
    /// <summary>All user-facing strings in one place (Korean). Placeholders are string.Format indices.</summary>
    public static class UIStrings
    {
        // HUD
        public const string CurrencyAndRun = "재화 {0}    런 {1}";
        public const string RunIncome = "이번 런 수입 {0}";
        public const string Stamina = "스태미나 {0} / {1}";

        // Result (spec §4)
        public const string ResultTitle = "런 {0} 결과";
        public const string ResultIncome = "이번 런 수입";
        public const string ResultPlanets = "만든 행성";
        public const string ResultRatio = "지난 런 대비";
        public const string ResultBest = "최고 런 수입";
        public const string PlanetCount = "{0} {1}";
        public const string PlanetSeparator = "     ";

        // Shop (spec §5)
        public const string ShopTitle = "상점";
        public const string ShopCurrency = "보유 재화 {0}";
        public const string ColumnName = "항목";
        public const string ColumnEffect = "효과 / 레벨";
        public const string ColumnLevel = "레벨";
        public const string ColumnCost = "비용";
        /// <summary>Indexed by UpgradeId.</summary>
        public static readonly string[] UpgradeNames = { "먼지 생성 빈도", "모으는 힘", "판매 비용", "최대 스태미나", "필요 먼지 수 감소" };
        /// <summary>Indexed by UpgradeId. {0} = effect number (percent for fraction effects).</summary>
        public static readonly string[] UpgradeEffects = { "+{0}/초", "+{0}%", "+{0}%", "+{0}", "−{0}%" };
        public const string UnlockName = "{0} 해금";
        public const string UnlockEffect = "티어 {0} 개방";
        public const string Level = "Lv {0}";
        public const string LevelMax = "Lv {0} (최대)";
        public const string Unlocked = "해금됨";
        public const string Locked = "잠김";
        public const string Buy = "구매";
        public const string Max = "최대";
        public const string NoCost = "-";
        public const string NextRun = "다음 런";

        // Debug overlay
        public const string DebugOverlay = "FPS {0:F1}   먼지 {1}   틱 {2:F2} ms (최대 {3:F2})   성능모드 {4}   봇 {5}";
        public const string On = "켜짐";
        public const string Off = "꺼짐";
        public const string ScreenshotSaved = "스크린샷 저장: {0}";
    }
}

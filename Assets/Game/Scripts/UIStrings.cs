using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// All user-facing strings in one place (Korean). Placeholders are string.Format indices.
    /// Names that belong to data rows (tiers, upgrades) are looked up by id key in <see cref="Keyed"/>
    /// (tier.&lt;n&gt;, upg.&lt;id&gt;.name, upg.&lt;id&gt;.effect) so the data assets hold no display text.
    /// </summary>
    public static class UIStrings
    {
        // HUD
        public const string CurrencyAndRun = "재화 {0}    런 {1}";
        public const string RunIncome = "이번 런 수입 {0}";
        public const string Stamina = "스태미나 {0} / {1}";
        public const string Paused = "일시정지";

        // Result (11 §4)
        public const string ResultTitle = "런 {0} 결과";
        public const string ResultIncome = "이번 런 수입";
        public const string ResultPlanets = "만든 행성";
        public const string ResultRatio = "지난 런 대비";
        public const string ResultBest = "최고 런 수입";
        public const string PlanetCount = "{0}  {1}";
        public const string NoPlanets = "-";

        // Shop
        public const string ShopTitle = "상점";
        public const string ShopCurrency = "보유 재화 {0}";
        public const string ColumnName = "항목";
        public const string ColumnEffect = "효과 / 레벨";
        public const string ColumnLevel = "레벨";
        public const string ColumnCost = "비용";
        public const string UnlockName = "{0} 해금";
        public const string UnlockEffect = "티어 {0} · 판매가 {1}";
        public const string AllUnlocked = "모두 해금";
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

        /// <summary>Strings keyed by data id. Every tier and upgrade id needs its keys here (Validate Data checks).</summary>
        static readonly Dictionary<string, string> Keyed = new Dictionary<string, string>
        {
            { "tier.1", "소행성" },
            { "tier.2", "혜성" },
            { "tier.3", "왜소행성" },
            { "tier.4", "위성" },
            { "tier.5", "암석 행성" },
            { "tier.6", "지구형 행성" },
            { "tier.7", "가스 행성" },
            { "tier.8", "갈색 왜성" },
            { "tier.9", "적색 왜성" },
            { "tier.10", "태양형 항성" },
            { "tier.11", "청색 거성" },

            // {0} = Stats.EffectDisplayNumber (percent for fraction effects).
            { "upg.spawn_rate.name", "먼지 생성 빈도" },
            { "upg.spawn_rate.effect", "+{0}/초" },
            { "upg.pull_accel.name", "모으는 힘" },
            { "upg.pull_accel.effect", "+{0}%" },
            { "upg.sale_mult.name", "판매 비용" },
            { "upg.sale_mult.effect", "+{0}%" },
            { "upg.stamina_max.name", "최대 스태미나" },
            { "upg.stamina_max.effect", "+{0}" },
            { "upg.threshold_mult.name", "필요 먼지 수 감소" },
            { "upg.threshold_mult.effect", "−{0}%" },
            { "upg.dust_cap.name", "화면 상한" },
            { "upg.dust_cap.effect", "+{0}개" },
            { "upg.dust_mass.name", "먼지 질량" },
            { "upg.dust_mass.effect", "×{0}" },
            { "upg.gravity_radius.name", "중력 반경" },
            { "upg.gravity_radius.effect", "+{0}%" },
            { "upg.stamina_drain.name", "스태미나 소모 감소" },
            { "upg.stamina_drain.effect", "−{0}%" },
            { "upg.start_bonus.name", "시작 보너스" },
            { "upg.start_bonus.effect", "먼지 +{0}" },
        };

        public static bool Has(string key) => key != null && Keyed.ContainsKey(key);

        /// <summary>The string for a key, or the key itself when missing (visible in the UI, reported by Validate Data).</summary>
        public static string Get(string key) => key != null && Keyed.TryGetValue(key, out var s) ? s : key;

        public static string TierKey(int tier) => "tier." + tier;
        public static string UpgradeNameKey(string id) => "upg." + id + ".name";
        public static string UpgradeEffectKey(string id) => "upg." + id + ".effect";

        public static string TierName(int tier) => Get(TierKey(tier));
        public static string UpgradeName(string id) => Get(UpgradeNameKey(id));
        public static string UpgradeEffect(UpgradeDef def) =>
            string.Format(Get(UpgradeEffectKey(def.id)), Fmt.Short(Stats.EffectDisplayNumber(def)));
    }
}

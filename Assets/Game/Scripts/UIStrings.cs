using System.Collections.Generic;

namespace Incremental
{
    /// <summary>
    /// All user-facing strings in one place (Korean). Placeholders are string.Format indices.
    /// Names that belong to data rows (tiers, stats) are looked up by id key in <see cref="Keyed"/>
    /// (tier.&lt;n&gt;, stat.&lt;id&gt;.name, stat.&lt;id&gt;.effect) so the data assets hold no display text.
    /// Node names are generated (12 §3 "이름"): stat name + roman numeral; gates use the tier name.
    /// </summary>
    public static class UIStrings
    {
        // HUD
        public const string CurrencyAndRun = "재화 {0}    런 {1}";
        public const string RunIncome = "이번 런 수입 {0}";
        public const string Stamina = "스태미나 {0} / {1}";
        public const string Paused = "일시정지";

        // Result (11 §4, 13 §3)
        public const string ResultTitle = "런 {0} 결과";
        public const string ResultIncome = "이번 런 수입";
        public const string ResultPlanets = "만든 행성";
        public const string ResultRatio = "지난 런 대비";
        public const string ResultBest = "최고 런 수입";
        public const string PlanetCount = "{0}  {1}";
        public const string NoPlanets = "-";
        public const string OpenTree = "스킬트리  (Space)";
        public const string PlanetMulti = "×{0}";

        // Skill tree (12 §9)
        public const string TreeTitle = "스킬트리";
        public const string TreeCurrency = "보유 재화 {0}";
        public const string TreeHint = "클릭: 구매   누르고 있기: 연속 구매   드래그: 이동   휠: 확대";
        public const string NextRun = "다음 런  (Space)";
        public const string Level = "Lv {0}";
        public const string LevelOf = "Lv {0}/{1}";
        public const string LevelShort = "{0}/{1}";
        public const string EffectPerLevel = "레벨당 {0}";
        public const string GateEffect = "티어 {0} · 판매가 {1} · 필요 질량 {2}";
        public const string Cost = "비용 {0}";
        public const string Maxed = "완료";
        public const string LockedRing = "{0} 해금 필요";
        public const string LockedPrereq = "이어진 노드 필요";
        public const string CenterName = "성운";
        public const string CenterDesc = "모든 것이 시작된 곳";

        // Debug overlay
        public const string DebugOverlay = "FPS {0:F1}   먼지 {1}   틱 {2:F2} ms (최대 {3:F2})   성능모드 {4}   봇 {5}";
        public const string On = "켜짐";
        public const string Off = "꺼짐";
        public const string ScreenshotSaved = "스크린샷 저장: {0}";

        /// <summary>Strings keyed by data id. Every tier and stat id needs its keys here (Validate Data checks).</summary>
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
            { "stat.spawn_rate.name", "먼지 생성 빈도" },
            { "stat.spawn_rate.effect", "+{0}/초" },
            { "stat.pull_accel.name", "모으는 힘" },
            { "stat.pull_accel.effect", "+{0}%" },
            { "stat.sale_mult.name", "판매 비용" },
            { "stat.sale_mult.effect", "+{0}%" },
            { "stat.stamina_max.name", "최대 스태미나" },
            { "stat.stamina_max.effect", "+{0}" },
            { "stat.threshold_mult.name", "필요 먼지 수 감소" },
            { "stat.threshold_mult.effect", "−{0}%" },
            { "stat.dust_cap.name", "화면 상한" },
            { "stat.dust_cap.effect", "+{0}개" },
            { "stat.dust_mass.name", "먼지 질량" },
            { "stat.dust_mass.effect", "×{0}" },
            { "stat.gravity_radius.name", "중력 반경" },
            { "stat.gravity_radius.effect", "+{0}%" },
            { "stat.stamina_drain.name", "스태미나 소모 감소" },
            { "stat.stamina_drain.effect", "−{0}%" },
            { "stat.start_bonus.name", "시작 보너스" },
            { "stat.start_bonus.effect", "먼지 +{0}" },
        };

        public static bool Has(string key) => key != null && Keyed.ContainsKey(key);

        /// <summary>The string for a key, or the key itself when missing (visible in the UI, reported by Validate Data).</summary>
        public static string Get(string key) => key != null && Keyed.TryGetValue(key, out var s) ? s : key;

        public static string TierKey(int tier) => "tier." + tier;
        public static string StatNameKey(string statId) => "stat." + statId + ".name";
        public static string StatEffectKey(string statId) => "stat." + statId + ".effect";

        public static string TierName(int tier) => Get(TierKey(tier));
        public static string StatName(string statId) => Get(StatNameKey(statId));

        /// <summary>Effect of one level of a stat node, e.g. "+20%", "×1.5". Empty for gates.</summary>
        public static string NodeEffect(NodeDef n) =>
            n.IsGate ? string.Empty : string.Format(Get(StatEffectKey(n.statId)), Fmt.Short(Stats.EffectDisplayNumber(n)));

        /// <summary>
        /// Stat nodes: stat name + roman numeral by position among the nodes of that stat (ring order, then table order),
        /// e.g. 먼지 생성 빈도 II. Gates: the tier name.
        /// </summary>
        public static string NodeName(NodeTable t, NodeDef n)
        {
            if (n.IsGate) return TierName(n.tier);
            int self = t.nodes.IndexOf(n);
            int index = 1;
            for (int i = 0; i < t.nodes.Count; i++)
            {
                var o = t.nodes[i];
                if (i == self || o.IsGate || o.statId != n.statId) continue;
                if (o.minTier < n.minTier || (o.minTier == n.minTier && i < self)) index++;
            }
            return StatName(n.statId) + " " + Roman(index);
        }

        static readonly int[] RomanValues = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        static readonly string[] RomanDigits = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };

        public static string Roman(int n)
        {
            if (n <= 0) return n.ToString();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < RomanValues.Length; i++)
                while (n >= RomanValues[i]) { sb.Append(RomanDigits[i]); n -= RomanValues[i]; }
            return sb.ToString();
        }
    }
}

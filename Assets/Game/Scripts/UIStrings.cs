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
        public const string NoPlanets = "-";
        public const string OpenTree = "스킬트리  (Space)";
        public const string PlanetMulti = "×{0}";
        public const string DeltaUp = "▲{0}";
        public const string NewBody = "새 천체";

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

        // Ending (15 §4)
        public const string EndingContinue = "계속하기  (Space)";

        // Debug overlay
        public const string DebugOverlay = "FPS {0:F1}   먼지 {1}   틱 {2:F2} ms (최대 {3:F2})   성능모드 {4}   봇 {5}   연출 {6:0.0}";
        public const string EffectIntensity = "연출 강도 {0:0.0}";
        public const string On = "켜짐";
        public const string Off = "꺼짐";
        public const string ScreenshotSaved = "스크린샷 저장: {0}";
        public const string GalleryHint = "천체 갤러리   F8 닫기   마우스: 광원 방향   휠: 크기";
        public const string EndingReplay = "엔딩 시퀀스 재생 (저장은 그대로)";
        public const string EndingReplayBetweenRuns = "엔딩 재생은 런 사이에만 됩니다";
        public const string DebugStaminaDrained = "스태미나 0 (런 종료)";
        public const string StaminaPreset = "스태미나 소모: {0} (홀드 {1:0.0}/초, 대기 {2:0.0}/초)";
        public const string DebugDuringRunOnly = "런 중에만 됩니다";

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

            // Credits (15 §4.4): placeholder values exactly as in the spec table; the developer sets them (backlog D11).
            { "credits.title", "(게임 제목, D7 미정)" },
            { "credits.made_by", "제작: (개발자 이름)" },
            { "credits.engine", "Unity로 만들었습니다" },
            { "credits.thanks", "플레이해 주셔서 감사합니다" },
            { "credits.last", "먼지는 다시 별이 된다 (제안)" },

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
            { "stat.dust_chance.name", "출현 확률" },
            { "stat.dust_chance.effect", "+{0}%p" },

            // Fragments (20 §5).
            { "stat.frag_unlock.name", "파편 해금" },
            { "stat.frag_unlock.effect", "파편 생성 시작, 확률 {0}%" },
            { "stat.frag_chance.name", "파편 확률" },
            { "stat.frag_chance.effect", "+{0}%p" },
            { "stat.frag_offset.name", "파편 범위" },
            { "stat.frag_offset.effect", "티어 +{0}" },

            // Dust kinds (20 §4): the name is the node name of the unlock and the prefix of its chance node.
            { "dust.base", "기본 먼지" },
            { "dust.medium", "중형 먼지" },
            { "dust.large", "대형 먼지" },
            { "dust.crystal", "결정 먼지" },
            { "dust.interstellar", "성간 먼지" },

            // Stamina drain presets of the debug key (18 §A1); Validate Data checks that every preset id has a key.
            { "stamina.preset.loose", "느슨함" },
            { "stamina.preset.proposed", "제안" },
            { "stamina.preset.fast", "빠름" },
        };

        public static bool Has(string key) => key != null && Keyed.ContainsKey(key);

        /// <summary>The string for a key, or the key itself when missing (visible in the UI, reported by Validate Data).</summary>
        public static string Get(string key) => key != null && Keyed.TryGetValue(key, out var s) ? s : key;

        /// <summary>Effect line of a dust kind unlock node (20 §4): {0} = the kind name, {1} = its starting share in percent.</summary>
        public const string DustUnlockEffect = "{0} 출현 시작 (확률 {1}%)";
        /// <summary>Name of a dust kind's chance node: the kind name plus the stat name, e.g. 중형 먼지 출현 확률 I.</summary>
        public const string DustChanceName = "{0} {1}";

        public static string TierKey(int tier) => "tier." + tier;
        public static string DustKindKey(string id) => "dust." + id;
        public static string DustKindName(string id) => Get(DustKindKey(id));
        public static string StaminaPresetKey(string id) => "stamina.preset." + id;
        public static string StatNameKey(string statId) => "stat." + statId + ".name";
        public static string StatEffectKey(string statId) => "stat." + statId + ".effect";

        public static string TierName(int tier) => Get(TierKey(tier));
        public static string StaminaPresetName(string id) => Get(StaminaPresetKey(id));
        public static string StatName(string statId) => Get(StatNameKey(statId));

        /// <summary>
        /// Effect of one level of a stat node, e.g. "+20%", "×1.5". Empty for gates; a dust kind unlock says which kind
        /// starts appearing and at what share (20 §4), which needs the kind's data.
        /// </summary>
        public static string NodeEffect(NodeDef n, GameParams p = null)
        {
            if (n.IsGate) return string.Empty;
            if (n.IsDustUnlock)
            {
                var kind = p != null ? p.DustKind(p.DustKindIndex(n.dustKindId)) : null;
                return string.Format(DustUnlockEffect, DustKindName(n.dustKindId), Fmt.Short(kind != null ? kind.baseChance * 100.0 : 0.0));
            }
            return string.Format(Get(StatEffectKey(n.statId)), Fmt.Short(Stats.EffectDisplayNumber(n)));
        }

        /// <summary>
        /// Stat nodes: stat name + roman numeral by position among the nodes of that stat (ring order, then table order),
        /// e.g. 먼지 생성 빈도 II. Gates: the tier name.
        /// </summary>
        public static string NodeName(NodeTable t, NodeDef n)
        {
            if (n.IsGate) return TierName(n.tier);
            if (n.IsDustUnlock) return DustKindName(n.dustKindId);
            int self = t.nodes.IndexOf(n);
            int index = 1;
            bool perKind = StatIds.NeedsDustKind(n.statId);
            for (int i = 0; i < t.nodes.Count; i++)
            {
                var o = t.nodes[i];
                if (i == self || o.IsGate || o.IsDustUnlock || o.statId != n.statId) continue;
                // Nodes of a per-kind stat are numbered within their own kind, so every kind starts at I.
                if (perKind && o.dustKindId != n.dustKindId) continue;
                if (o.minTier < n.minTier || (o.minTier == n.minTier && i < self)) index++;
            }
            string name = StatName(n.statId) + " " + Roman(index);
            return perKind ? string.Format(DustChanceName, DustKindName(n.dustKindId), name) : name;
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

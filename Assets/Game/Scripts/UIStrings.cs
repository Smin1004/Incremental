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
        /// <summary>26 §9.2: the HUD's labels; the values stand under them.</summary>
        public const string HudCurrency = "재화";
        public const string HudRun = "런 {0}";
        public const string HudIncome = "이번 런 수입";
        public const string Stamina = "스태미나 {0} / {1}";
        public const string Paused = "일시정지";

        // Result (11 §4, 13 §3)
        public const string ResultTitle = "런 {0} 결과";
        public const string ResultIncome = "이번 런 수입";
        public const string ResultPlanets = "만든 행성";
        public const string ResultRatio = "지난 런 대비";
        public const string ResultBest = "최고 런 수입";
        public const string NoPlanets = "-";
        /// <summary>26 §5.2: the button's label; the key is shown beside it as a chip (<see cref="KeySpace"/>).</summary>
        public const string OpenTreeLabel = "스킬트리";
        public const string KeySpace = "Space";
        /// <summary>26 §5.2: the badge on a run that beat the best income.</summary>
        public const string ResultNewBest = "최고 기록";
        public const string PlanetMulti = "×{0}";
        public const string DeltaUp = "▲{0}";
        public const string NewBody = "새 천체";

        // Skill tree (12 §9)
        public const string TreeTitle = "스킬트리";
        /// <summary>26 §9.2: the label over the currency value in the tree's top bar.</summary>
        public const string TreeCurrencyLabel = "보유 재화";
        public const string TreeHint = "클릭: 구매   누르고 있기: 연속 구매   드래그: 이동   휠: 확대";
        /// <summary>26 §9.2: the button's label; the key is a chip beside it (<see cref="KeySpace"/>).</summary>
        public const string NextRunLabel = "다음 런";
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
        /// <summary>{8} = the UI direction id (key U), {9} = the stamina display, a or b (key G), 26 §6.</summary>
        public const string DebugOverlay = "FPS {0:F1}   먼지 {1} (화면 밖 {7}, 생성 ×{11:0.0})   틱 {2:F2} ms (최대 {3:F2})   성능모드 {4}   봇 {5}   연출 {6:0.0}   UI {8} [U]   스태미나 {9} [G]   노출 억제 {10} [X]";
        /// <summary>26 §5.5: the toast of the overexposure key. {0} = on / off.</summary>
        public const string ExposureFixToast = "과노출 억제: {0}  (X)";
        /// <summary>26 §6: toasts of the two UI debug keys. {0} = the direction name / the display name.</summary>
        public const string UiDirectionToast = "UI 방향: {0}  (U)";
        public const string StaminaDisplayToast = "스태미나 표시: {0}  (G)";
        /// <summary>26 §10.1: the T key, the spiral twist of the tree's arms. {0} = degrees per ring.</summary>
        public const string TreeTwistToast = "트리 나선 비틀기: 링당 {0}°  (T)";
        public const string StaminaDisplayBar = "(a) 위쪽 막대";
        public const string StaminaDisplayRing = "(b) 중력 고리";
        public const string StaminaDisplayBarShort = "a";
        public const string StaminaDisplayRingShort = "b";
        public const string EffectIntensity = "연출 강도 {0:0.0}";
        public const string On = "켜짐";
        public const string Off = "꺼짐";
        public const string ScreenshotSaved = "스크린샷 저장: {0}";
        public const string GalleryHint = "천체 갤러리   F8 닫기   마우스: 광원 방향   휠: 크기";
        public const string EndingReplay = "엔딩 시퀀스 재생 (저장은 그대로)";
        public const string EndingReplayBetweenRuns = "엔딩 재생은 런 사이에만 됩니다";
        public const string DebugStaminaDrained = "스태미나 0 (런 종료)";
        public const string StaminaPreset = "스태미나 소모: {0} (홀드 {1:0.0}/초, 대기 {2:0.0}/초)";
        /// <summary>24 §23.2, key K: {0} preset name, {1} radius node scale, {2} crystal x at unlock, {3} per level, {4}/{5} ring 10 diameter share.</summary>
        public const string GravityRangeToast = "중력 범위: {0} (성장 ×{1:0.##}, 결정 ×{2:0.00} / 레벨당 +{3:0.00}) · 링 10 평소 {4:0.0}% / 버프 Lv3 {5:0.0}%";
        /// <summary>24 §23.3, key L: {0} stamina_max node scale, {1} stamina, {2} seconds of a run held throughout.</summary>
        public const string StaminaScaleToast = "스태미나 성장 ×{0:0.##} (다음 판부터) · 스태미나 {1:0} · 계속 홀드 시 최대 길이 {2:0}초";
        public const string DebugDuringRunOnly = "런 중에만 됩니다";
        /// <summary>22 §3: the follow debug key's toast; {0} = the top speed in px/s.</summary>
        public const string FollowOn = "중력원 추종: 켜짐 (최대 {0:0} px/초)";
        public const string FollowOff = "중력원 추종: 꺼짐 (커서에 고정)";
        /// <summary>
        /// 24 §7: the ability status line in the corner. {0} = the abilities that are on, each written as
        /// <see cref="AbilityDebugEntry"/> (name, level) plus <see cref="AbilityDebugCharge"/> (seconds of holding to its
        /// next trigger) when it has a cooldown.
        /// </summary>
        public const string AbilityDebugForced = "능력(강제): {0}";
        public const string AbilityDebugTree = "능력(트리): {0}";
        public const string AbilityDebugNone = "없음";
        public const string AbilityDebugEntry = "{0} Lv{1}";
        /// <summary>24 §21.2: after the entry of an ability in its evolved form.</summary>
        public const string AbilityDebugEvolved = " 진화";
        public const string AbilityDebugCharge = " {0:0.0}초";
        public const string AbilityDebugSeparator = " · ";
        /// <summary>
        /// 24 §7: which keys do what, after the status line while the F1 overlay is on. {0} = the last number that forces
        /// one ability alone, {1} = the number that forces all of them.
        /// </summary>
        public const string AbilityDebugHint = "     [숫자 1~{0}: 하나만   {1}: 전부   0: 끔   - / =: 레벨]";
        /// <summary>
        /// 24 §7: added to the status line while an ability is on and the effect intensity is below 1 (F11). {0} = the
        /// intensity. At 0 the ability rings, glows and the brightening of excited dust are not drawn at all.
        /// </summary>
        public const string AbilityDebugDimmed = "   ※ 연출 강도 {0:0.0}: 능력의 고리·빛·들뜸이 흐리거나 안 보임 (F11로 1.0까지)";
        /// <summary>24 §7: toasts of the ability debug keys. {0} = the ability name or the level, {1} = the level.</summary>
        public const string AbilityForceOff = "능력 강제: 꺼짐 (트리대로)";
        public const string AbilityForceOne = "능력 강제: {0}만 Lv{1}";
        public const string AbilityForceAll = "능력 강제: 전부 Lv{0}";
        /// <summary>A level key was pressed with nothing forced. {0} = the number that forces all abilities.</summary>
        public const string AbilityForceFirst = "숫자 1~{0}로 능력을 먼저 켜세요";

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
            { "stat.follow_speed.name", "중력원 추종 속도" },
            { "stat.follow_speed.effect", "+{0}%p" },
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

            // Abilities (24): the name of each AbilityTable row and the one line its unlock node shows. Validate Data
            // checks that every row has both.
            { "ability.crystal", "증폭 결정" },
            { "ability.crystal.effect", "홀드 중 나타나는 결정을 먹으면 잠시 중력이 세진다" },
            // 24 §21.2: each ability's evolution, the name and the one line its evolve node shows.
            { "ability.crystal.evolve", "공명" },
            { "ability.crystal.evolve.effect", "결정이 더 자주 나오고, 버프 중에 또 먹으면 두 겹까지 쌓인다" },
            { "ability.meteor.evolve", "유성우" },
            { "ability.meteor.evolve.effect", "유성이 더 자주 떨어진다" },
            { "ability.implode", "수축" },
            { "ability.implode.effect", "주기적으로 주변 먼지를 한 번에 당긴다" },
            // 24 §23.5: shown as 궤도석; the id stays satellite (save key), and tier 4 is still 위성.
            { "ability.satellite", "궤도석" },
            { "ability.satellite.effect", "중력원을 도는 작은 중력원" },
            { "ability.meteor", "유성" },
            { "ability.meteor.effect", "유성이 지나가며 먼지를 흘린다" },
            { "ability.blackhole", "블랙홀" },
            { "ability.blackhole.effect", "충전되면 중력원이 잠시 블랙홀이 되어 화면 전체를 빨아들인다" },

            // Ability stats (24 §8). The node name puts the ability's name in front: 수축 범위 I, 궤도석 수 I, 유성 양 I.
            // The last level of a range node is the whole screen whatever the step is worth (24 §16.1).
            { "stat.ability_range.name", "범위" },
            { "stat.ability_range.effect", "+{0}단계 (마지막 레벨은 화면 전체)" },
            { "stat.ability_power.name", "힘" },
            { "stat.ability_power.effect", "+{0}%" },
            { "stat.ability_cooldown.name", "쿨다운 감소" },
            { "stat.ability_cooldown.effect", "−{0}%" },
            { "stat.ability_count.name", "수" },
            { "stat.ability_count.effect", "+{0}" },
            { "stat.ability_amount.name", "양" },
            { "stat.ability_amount.effect", "+{0}%p" },
            { "stat.ability_duration.name", "지속" },
            { "stat.ability_duration.effect", "+{0}초" },

            // Visual directions of the UI (26 §3), by UITheme direction id; Validate Data checks that every id has a key.
            { "ui.direction.A", "A 관측 기기" },
            { "ui.direction.B", "B 유리 패널" },
            { "ui.direction.C", "C 패널 없음" },

            // Stamina drain presets of the debug key (18 §A1); Validate Data checks that every preset id has a key.
            // Gravity range presets of the debug key K (24 §23.2); Validate Data checks that every preset id has a key.
            { "gravity.preset.now", "지금" },
            { "gravity.preset.plan1", "안 1" },
            { "gravity.preset.plan2", "안 2" },

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
        /// <summary>
        /// Name of an ability's unlock node (24 §6): {0} = the ability name. The prefix keeps abilities apart from bodies
        /// (the 궤도석 ability was called 위성, like tier 4, until 10/12).
        /// </summary>
        public const string AbilityUnlockName = "능력: {0}";
        /// <summary>24 §21.2: name of an ability's evolve node: {0} = the evolution's own name (e.g. 공명).</summary>
        public const string AbilityEvolveName = "진화: {0}";
        /// <summary>Name of an ability's stat node: the ability name plus the stat name, e.g. 수축 범위 I.</summary>
        public const string AbilityStatName = "{0} {1}";

        public static string TierKey(int tier) => "tier." + tier;
        public static string DustKindKey(string id) => "dust." + id;
        public static string DustKindName(string id) => Get(DustKindKey(id));
        public static string AbilityKey(string id) => "ability." + id;
        public static string AbilityEffectKey(string id) => "ability." + id + ".effect";
        public static string AbilityEvolveKey(string id) => "ability." + id + ".evolve";
        public static string AbilityEvolveEffectKey(string id) => "ability." + id + ".evolve.effect";
        public static string AbilityName(string id) => Get(AbilityKey(id));
        public static string StaminaPresetKey(string id) => "stamina.preset." + id;
        public static string UiDirectionKey(string id) => "ui.direction." + id;
        public static string UiDirectionName(string id) => Get(UiDirectionKey(id));
        public static string StatNameKey(string statId) => "stat." + statId + ".name";
        public static string StatEffectKey(string statId) => "stat." + statId + ".effect";

        public static string TierName(int tier) => Get(TierKey(tier));
        public static string StaminaPresetName(string id) => Get(StaminaPresetKey(id));
        public static string GravityPresetKey(string id) => "gravity.preset." + id;
        public static string StatName(string statId) => Get(StatNameKey(statId));

        /// <summary>
        /// Effect of one level of a stat node, e.g. "+20%", "×1.5". Empty for gates; a dust kind unlock says which kind
        /// starts appearing and at what share (20 §4), which needs the kind's data; an ability unlock says in one line
        /// what the ability does (24 §6).
        /// </summary>
        public static string NodeEffect(NodeDef n, GameParams p = null)
        {
            if (n.IsGate) return string.Empty;
            if (n.IsAbilityUnlock) return Get(AbilityEffectKey(n.abilityId));
            if (n.IsAbilityEvolve) return Get(AbilityEvolveEffectKey(n.abilityId));
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
            if (n.IsAbilityUnlock) return string.Format(AbilityUnlockName, AbilityName(n.abilityId));
            if (n.IsAbilityEvolve) return string.Format(AbilityEvolveName, Get(AbilityEvolveKey(n.abilityId)));
            int self = t.nodes.IndexOf(n);
            int index = 1;
            bool perKind = StatIds.NeedsDustKind(n.statId);
            bool perAbility = StatIds.NeedsAbility(n.statId);
            for (int i = 0; i < t.nodes.Count; i++)
            {
                var o = t.nodes[i];
                if (i == self || o.IsGate || o.IsDustUnlock || o.IsAbilityUnlock || o.IsAbilityEvolve || o.statId != n.statId) continue;
                // Nodes of a per-kind stat are numbered within their own kind, so every kind starts at I; the same
                // goes for the stats of one ability (24 §8).
                if (perKind && o.dustKindId != n.dustKindId) continue;
                if (perAbility && o.abilityId != n.abilityId) continue;
                if (o.minTier < n.minTier || (o.minTier == n.minTier && i < self)) index++;
            }
            string name = StatName(n.statId) + " " + Roman(index);
            if (perAbility) return string.Format(AbilityStatName, AbilityName(n.abilityId), name);
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

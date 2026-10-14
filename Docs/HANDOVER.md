# 인수인계 요약 (계정 이전용)

| 항목 | 값 |
|---|---|
| 작성 | 2026-10-07 |
| 용도 | Claude Code 계정을 옮긴 뒤 **새 세션이 이 파일 하나로 프로젝트 상태를 파악**한다. 이전 세션들의 자동 메모리에 있던 내용도 §9에 옮겨 적었다 |
| 읽는 순서 | 이 파일 → [CLAUDE.md](../CLAUDE.md) → [README.md](README.md) → 맡은 역할의 문서 (§10) |

---

## 0. 새 세션 첫 5분 체크리스트

1. `git status`, `git branch`로 지금 브랜치가 `proto6`인지(10/12 기준), `Docs/`에 미커밋 변경이 있는지 본다 (§2.1).
2. 어떤 역할인지 확인한다. **기획 세션**(문서만, 코드 안 씀)인지 **구현 세션**(프롬프트를 받아 코드)인지 (§4.1).
3. 유니티를 다룰 거면 `curl http://127.0.0.1:8080/api/instances`로 에디터가 붙어 있는지 보고, `set_active_instance`로 `Incremental@…`를 고정한다 (§5.2).
4. 구현 세션이면 가장 최근 프롬프트를 읽는다. **10/12 기준 Proto7까지 끝났고 7차 회신 반영도 끝났다.** 다음 프롬프트는 24 §22.3 회신을 받은 뒤 기획 세션이 쓴다. 먼저 아래 §2의 10/12 갱신을 본다.
5. 개발자 세이브(`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Incremental\save.json`)는 **플레이 테스트 전에 복사해 두고 끝나면 되돌린다** (§5.4).

---

## 1. 프로젝트 한 줄

**우주 먼지를 마우스 홀드로 만든 중력으로 끌어모아 행성을 만들고 파는, 짧은 능동형 런 기반 증분(incremental) 게임.** 1인 개발(개발자 Smin, GitHub `Smin1004`), Unity 6000.0.66f2 + 2D URP + Input System, 외부 아트 없이 셰이더와 파티클만으로 그린다. Steam 출시 목표(윈도우 + 리눅스, 한국어 + 영어). 출시일은 정하지 않고 **완성 기준 표**([16](16_Release_Roadmap.md) §3)가 채워지면 출시 판단을 한다.

루프: 런 시작 → 홀드(스태미나 소모)로 먼지 흡수 → 놓으면 질량만큼의 천체(티어 1~11) 생성, 즉시 런 수입 → 스태미나 0에 런 종료 → 결과 화면(이번 런 천체가 공전하는 태양계) → 스킬트리(방사형 별자리, 관문 노드가 링 경계) → 다음 런. 티어 11 청색 거성 → 초신성 엔딩 → 엔들리스.

---

## 2. 지금 상태 (2026-10-07 스냅샷)

> **10/12 갱신 (기획 세션).** 이 절의 아래 내용은 10/7 기준이다. 그 뒤에 바뀐 것은 다음과 같다.
>
> - **깃:** `abilities`를 main에 병합했다(`014f820`, 10/7). 작업 브랜치는 **`proto6`**(main보다 앞서고, 병합하지 않음). 10/12에 `main`, `abilities`, `proto6`을 `origin`에 푸시했다(R26 해소). `Docs/Summary_Week1-6.pptx` 삭제는 개발자가 의도한 것이다(10/12). 8차 워커의 "기획 문서 갱신" 커밋에 들어간다.
> - **6차 (10/8):** 증폭 결정(링 2·3, 버프 능력), 중력 반경 축소(링 1 27.8% → 링 10 47.8%), 트리 비틀기 16°.
> - **7차 (10/8):** 진화 5종과 묶음 완성(×N)을 구현했다. 같은 날 회신을 받아 **공명·유성우만 간격형으로 남기고** 연쇄 붕괴·번개 사슬·특이점은 기각했다. 수축 쿨다운 20초, 먼지 보충 가속(`dustRefillBoost` 3). 24 §22, 보고서 §12.
> - **지금:** 마감 회신을 받아(10/12) **8차 프롬프트 [Prompts/Proto8.md](Prompts/Proto8.md)**를 썼다(24 §23: 중력 범위 축소 키 `K`, 판 길이 키 `L`, 런 시작 중앙·먼지 고르게, 블랙홀 크게·60초, 능력 "위성" → "궤도석"). 그 뒤 순서는 빈 진화 자리 안건(기획) → `proto6` main 병합 + 빌드 확인 2차 → 밸런스 패스(강화 레벨 세분화 포함) → 출시 기능. 판단 예상은 12월 초 (16 §3c, R27).
> - **디버그 숫자 키가 바뀌었다:** 1 증폭 결정, 2 수축, 3 위성, 4 유성, 5 블랙홀, 6 전부. 공명과 유성우는 Lv4(진화)까지 간다 (17 §1).
> - **10/14:** 8차 확인 끝. 진화 빈 자리 안건은 전부 보류 → **능력 단계 확정**. 다음 프롬프트 [Prompts/Proto9_Build2.md](Prompts/Proto9_Build2.md): 블랙홀 `sizePx` 138, `proto6` main 병합, 빌드 확인 2차. 그 뒤 밸런스 패스(`K`·`L` 배율을 노드 값에 반영, 강화 레벨 세분화 포함).
> - **개발 방향 D20:** 몰아침은 빈도와 숫자로 만들고, 화면을 한꺼번에 덮는 연출은 넣지 않는다.

### 2.1 깃

| 항목 | 상태 |
|---|---|
| 현재 브랜치 | **`abilities`** (main보다 33커밋 앞, 미병합). 마지막 커밋 `aa320bd` "UI 2·3단계 F: 캡처 16장과 보고서" (10/6) |
| `main` | 7주차까지 병합됨. 마지막 커밋 `32250fc` (9/30) |
| 원격 `origin` | `https://github.com/Smin1004/Incremental.git`. **`origin/main`은 9/18 `f1844ef`에 멈춰 있다.** 로컬 `main`이 34커밋, `abilities`가 거기서 33커밋 더 앞선다. 즉 **9/18 이후 작업 전부가 로컬에만 있다.** 푸시는 개발자 판단 (§8) |
| 미커밋 변경 | `Docs/16, 17, 24, 26, 90, README.md` 수정, `Docs/Prompts/Build_Check1.md`, `Satellite_Build.md` 신규. **기획 세션의 문서 작업**(10/7 개발자 확인 결과 반영). 구현 세션이 시작할 때 "기획 문서 갱신"으로 먼저 커밋하는 관례 (§4.3). **10/7 저녁 `Assets/Game/Scripts/PerfBench.cs`도 수정 중**: 구현 세션이 Satellite_Build의 빌드 확인(D)에 쓸 "먼지 상한 + 능력 전부 최대 + 화면 밖 먼지" 장면을 `-perfbench`에 추가하기 시작한 것. 7주차 장면은 능력·여백 없이 그대로 두어 표를 줄 단위로 비교하게 했다. 커밋 전이라면 그 세션의 작업이 어디까지 갔는지 `git diff`로 본다 |
| 다른 브랜치 | `skilltree-model`, `planet-visuals`, `ending`, `quickfix1`, `week6`, `scale`: 각 주차 작업 브랜치. 전부 main에 병합 완료. 지워도 된다 |
| 줄바꿈 | `core.autocrlf=true`. Edit 도구는 CRLF 유지, Write는 LF. 한 파일에 섞이지만 않으면 된다 |

### 2.2 개발 단계 ([16](16_Release_Roadmap.md) §3b)

| 순서 | 내용 | 상태 |
|---|---|---|
| 1 | 7주차: 궤적, 화면 확장, 성능, 밸런스 | 완료, main |
| 2 | 특수 능력 프로토타입 1~3차 (수축, 위성, 유성) | 완료, `abilities` |
| 3 | 능력 마무리 | **개발자 확인 완료 (10/7).** 수축·유성 확정. **위성은 "끌고 오는 힘이 아쉽다" → 4차 견인 작업** (24 §18). 개발자: **"아직 임팩트가 모자라다, 추가할 것 같다"** (C3-12). 기획 후보: 블랙홀(링 10), 충격파(링 2, 첫 능력), 강화 진화(25 §4). 강화 레벨 수도 늘린다(C3-6). 결정 대기 |
| 3b | UI·연출 보강 (시안 → 방향 A 확정 → 2·3단계) | **개발자 확인 완료 (10/7).** HUD·결과·전환 확정, 과노출 억제 켠 채 닫음. **트리 배치만 다시**: 개발자 "노드들이 너무 1자로 뻗어 있다, 고르지 않다" → 기획 제안 **나선 팔**(링마다 비틀기 12°, 팔 7개 고른 간격, 곡선 연결선, 비틀기 값을 키로 비교). 26 §10.1, 채택 대기 |
| 4 | 마지막 밸런스 패스 (I_k 재측정, 링 비용 재생성, `maxReduction` 비교, 능력·화면 밖 먼지 처리량, **후반 중력 고리 크기**) | 대기 |
| 5 | 출시 기능 (사운드, Esc 메뉴, 옵션, 첫 실행 안내, 한영, 도전과제 목록, 이름 반영) | 대기 |
| 6 | 외부 테스트 3~5명 | 대기 |
| 7 | 완성 기준 대조 → 출시 판단 → Steam 절차 (판단일 + 6주 이상) | - |

### 2.3 바로 다음 작업

> **10/7 갱신 (기획 세션): 다음 작업은 [Prompts/Proto5_Build.md](Prompts/Proto5_Build.md)다.** Satellite_Build의 A(위성 견인)와 B~D(빌드 확인)에 천체 완성 간격, 블랙홀, 트리 나선, 중력 반경 화면 점유 측정을 더했다. Satellite_Build를 이미 시작했다면 그것을 끝낸 뒤 Proto5의 B부터 하고 빌드 확인(F)은 다시 한다.

**[Prompts/Satellite_Build.md](Prompts/Satellite_Build.md)** (10/7 작성, Proto5에 합쳐짐). 내용:

- A. **위성 견인** (24 §18.1): 위성 핵 반경 안 먼지를 위성 속도로 끌어당김(`towRadiusShare` 0.15, `towRate` 6/초), 들뜬 먼지 속도 상한 ×1.8(`exciteSpeedCapMult`), `pullShare` 1.0 → 1.4. 2차와 같은 장면에서 10초 홀드 포획 수 재측정(2차: 위성 없음 717, 1기 871, 4기 1,116).
- B~D. **빌드 확인 1차** (R25): 7주차 이후 빌드를 한 번도 안 만들었다. 패키지 5개 제거, TMP + 실행 중 Pretendard SDF 폰트 생성, 능력·화면 밖 먼지·과노출 억제 비용이 빌드에서 미검증. 윈도우 빌드 → 화면 확인 캡처 → `-perfbench` 재측정(7주차 표와 나란히) → 리눅스 모듈 있으면 빌드만.
- 결과는 `Reports/Abilities_Proto.md` "4차" 절과 새 `Reports/Build_Check1.md`. main 병합은 하지 않는다 (빌드 통과 뒤 다음 작업 첫 단계에서).

그 뒤 순서: 트리 배치(나선 팔 채택 여부) → 추가 능력과 레벨 수 결정 → 능력 구현 → **밸런스 패스는 능력이 확정된 뒤** (C3-12) → 출시 기능.

> 이 문서를 쓰는 동안에도 기획 세션이 `Docs/`(24, 26, 90)를 고치고 있었다. 새 세션은 `git diff Docs/`로 이 문서 이후의 변경을 한 번 훑는다.

### 2.4 개발자 세이브 상태

`save.json` 버전 3, **링 11, 재화 약 261억**, 트리 거의 전부 구매. 10/7 17:31에 플레이함. 능력과 UI를 바로 볼 수 있는 상태라 지우지 않는다. 플레이 테스트 전 백업 필수 (§5.4).

---

## 3. 저장소 구조

```
Incremental/
├─ CLAUDE.md                 모든 세션 공통 규칙 (한국어 커밋, 빌드·성능 규칙)
├─ .mcp.json                 unityMCP → http://127.0.0.1:8080/mcp
├─ Assets/
│  ├─ Editor/McpAutoStartBootstrap.cs      MCP HTTP 자동 시작
│  ├─ Game/
│  │  ├─ Data/        AbilityTable, CelestialTable, GameParams, NodeTable, UITheme (.asset) ← 모든 수치
│  │  ├─ Scripts/     런타임 (아래 표)
│  │  ├─ Scripts/Editor/  CelestialSeed, SkillTreeSeed, DataValidationMenu, PrototypeSetup, SaveMenu, SnapshotMenu
│  │  ├─ Scripts/Save/    SaveStore, SaveMigration (세이브 v3)
│  │  ├─ Scripts/State/   MetaState, RunState, RunRecord, SaveData
│  │  ├─ Shaders/     Dust, DustTrail, Planet, Starfield, Flash, FxRing, UIPanel, Noise.hlsl
│  │  ├─ Materials/, UI/Fonts/ (Pretendard OTF + OFL 라이선스), GameRoot.prefab
│  │  └─ Tests/EditMode/  EditMode 테스트 약 227개 (DataTests … Week7Tests)
│  ├─ Scenes/SampleScene.unity
│  └─ TextMesh Pro/    TMP Essential Resources
├─ Builds/Win64/        마지막 윈도우 빌드 (7주차, gitignore)
├─ Captures/            MCP 스크린샷 임시 폴더 (gitignore)
└─ Docs/                기획 문서, 프롬프트, 보고서 (Unity가 임포트하지 않음)
```

### 3.1 핵심 스크립트 (전부 `Assets/Game/Scripts/`)

| 영역 | 파일 | 역할 |
|---|---|---|
| 루트 | `GameRoot` | 씬의 중심. 런 상태 머신, 화면 전환 요청, 디버그 토글(`ExposureFix` 등) |
| 먼지 | `DustField`, `DustMeshRenderer`, `DustTrailRenderer`, `DynamicMesh` | 먼지 시뮬레이션(화면 + 둘레 여백 `dustMarginPx`), 메시 렌더, 궤적 리본. `Impulse` / `DustForce` / `Shed`가 능력의 입구 |
| 중력·입력 | `CursorView`, `Session`, `Stats` | 중력원, 중력 고리(스태미나 게이지 모드 포함), 능력 충전 호. 스탯 합산 |
| 능력 | `AbilitySystem`, `Ability`, `ImplodeAbility`, `SatelliteAbility`, `MeteorAbility`, `Data/AbilityTable` | 능력 하나 = 표 한 행 + `Ability` 서브클래스 + 트리 노드 둘. 매 틱 표를 읽어 인스펙터 수정이 즉시 반영 |
| 천체 | `PlanetVisual`, `PlanetPool`, `PlanetIcon`, `IgnitionFx`, `SupernovaRunFx` | 셰이더 기반 티어별 외형, 점화, 초신성 |
| 화면 | `HudView`, `ResultView`, `ResultMath`, `SolarSystemView`, `TreeView`, `TreeInput`, `EndingView`, `GalleryView`, `ScreenTransition`, `GateExpandFx`, `ScreenFlash`, `BackdropView` | HUD, 결과 태양계, 트리, 엔딩, 갤러리(F8), 0.3초 전환, 관문 확장 고리 |
| UI 기반 | `UIBuilder`, `UIStyle`, `UITheme`, `UIShape`, `UIButton`, `UIRing`, `UIStrings`, `Fmt` | TMP 글자 생성(`CreateText`), 시작 시 Pretendard 동적 SDF 폰트 생성, `Incremental/UIPanel` 셰이더로 패널·버튼·칩·막대·배지, 문자열 테이블, 숫자 표기 |
| 경제 | `Shop`, `Data/NodeTable`, `Data/CelestialTable`, `Data/GameParams` | 노드 구매, 티어 데이터, 전역 매개변수(약 280 필드) |
| 도구 | `DebugTools`, `AutoplayBot`, `PerfBench`, `RunLogger`, `Data/DataValidator` | 디버그 키(F1~F12, 숫자 줄, X/U/G), 봇(F4), 빌드 `-perfbench`, `run_log.csv`, Validate Data |

### 3.2 에디터 메뉴 (`Incremental/…`)

`Validate Data`(데이터 검사, 체크포인트마다 0 오류 0 경고가 기준), `Setup/5. Rebuild Node Table From Seed`, `Setup/6. Reset UI Theme To Code Defaults`, `Save/Open Folder`, `Save/Delete Save`, `Snapshots/Load...`(7주차에 스냅샷 파일을 지웠으므로 비어 있음; `Tools.ApplyRingStartState(k)`로 대체).

### 3.3 디버그 키

[17_Playtest_Guide.md](17_Playtest_Guide.md) §1이 전체 표. 요약: **F1** 오버레이(+받은 키 콘솔 로그), **F2** 성능 모드, **F3** 재화, **F4** 봇, **F5** 저장, **F6** 티어 해금, **F7** 숫자 표기, **F8** 갤러리, **Shift+F9** 새 게임, **F10** 스태미나 프리셋, **F11** 연출 강도, **F12** 스크린샷, **End** 엔딩, **Home** 런 종료, **Insert** 추종 속도. 능력: 숫자 줄 **1/2/3**(하나씩), **4**(전부), **0**(끔), **=/-**(레벨). UI: **X** 과노출 억제, **U** UI 방향 A/B/C, **G** 스태미나 표시 (a)/(b). **F11로 연출 강도를 0으로 두면 능력 연출이 안 보인다** (개발자가 두 번 겪음).

---

## 4. 작업 방식

### 4.1 세션 역할

| 역할 | 하는 일 | 하지 않는 일 |
|---|---|---|
| **기획 세션** (PM) | `Docs/NN_*.md` 스펙 작성·검토, 보고서를 받아 수치·스펙 갱신, `Docs/Prompts/*.md` 작업 프롬프트 작성, `90_Backlog.md` 결정 기록, 스크린샷 검토, 제안 | **코드를 쓰지 않는다** |
| **구현 세션** (워커) | 개발자가 붙여 넣은 프롬프트를 브랜치에서 구현, 체크포인트마다 Validate Data + 커밋, 끝에 `Docs/Reports/*.md` 보고서 + 캡처 | 스펙에 없는 기능 추가, main 병합(프롬프트가 시키기 전까지) |

두 세션이 **같은 워킹 트리**를 쓴다. 기획 세션이 워커 작업 중에도 `Docs/`를 고칠 수 있으므로, 워커는 커밋 전마다 `git status`를 보고 `Docs/`가 움직였으면 한 줄로 알리고 계속한다(개발자 답은 늘 "넣고 진행"). 커밋은 **명시한 경로만 스테이지**한다.

흐름: 기획 세션이 스펙 + 프롬프트 → 개발자가 프롬프트 블록을 워커에 통째로 붙여 넣음(워커는 AskUserQuestion 하나로 "전체 실행" 확인, 지금까지 늘 그 답) → 워커가 먼저 기획 문서 변경을 "기획 문서 갱신"으로 커밋 → 구현 → 보고서 → 개발자 확인 → 기획 세션이 결과를 스펙·백로그에 기록 → 다음 프롬프트.

### 4.2 규칙 (개발자가 정한 것, 어기면 지적받는다)

1. **커밋 메시지는 한국어** (제목·본문). 식별자·파일명·절 번호는 원문. 트레일러 유지. 메시지는 스크래치패드 파일에 쓰고 `git commit -F`.
2. **프로토타입 우선.** 새 기능은 간단하게 만들어 개발자가 눈으로 먼저 본다. 그 단계에서는 봇 완주 런, 밸런스, 빌드 성능 측정, 새 테스트를 하지 않고 **기존 EditMode 테스트는 마지막에 한 번만** ("테스트 돌릴 때 시간 너무 걸림"). Validate Data는 체크포인트마다.
3. **디버그 키는 숫자 줄·F키·글자 키.** PageUp/PageDown/Insert/Home/End 묶음은 개발자 키보드에서 게임에 닿지 않은 적이 있다. 프로토타입은 `[Feature] …` 콘솔 로그를 남기고 데이터 플래그로 끌 수 있게 한다. "확인이 안 된다"는 회신이 오면 먼저 `%LOCALAPPDATA%\Unity\Editor\Editor.log`를 본다.
4. **수치는 전부 데이터 에셋에. 코드 상수 금지.** 새 필드는 `DataValidationMenu.CopyParamsFromCode("field")`로 에셋에 밀어 넣는다. Validate Data가 `seed drift` 경고로 인스펙터 튜닝을 알려 준다.
5. **텍스처·아이콘 그림 금지.** 셰이더와 글자로만. UI 색·크기·시간은 `UITheme`.
6. **연출 강도(F11)를 따른다.** UI와 월드 연출의 빛·움직임은 강도 0에서 꺼진다.
7. **외부 에셋은 공식 출처 + 라이선스 확인 + 라이선스 파일 동봉.** 내려받기 전에 묻는다. Claude가 고른 폰트(Pretendard)는 개발자가 "일단 유지"라고 했으나 여전히 바꿀 수 있는 임시 선택이다.
8. **재량 처리 후 보고.** 사소한 문제는 알아서 고치고, 확인이 필요한 것은 질문으로 막지 말고 보고서 끝에 목록으로. 요청 없는 커밋은 하지 않는다(프롬프트가 체크포인트 커밋을 시키는 경우는 예외).
9. **`com.unity.ai.assistant` 재설치 금지.** 성능은 **빌드에서** 잰다. `GameParams.dustCapCeiling`을 넘기면 Validate Data 오류.
10. **개발자 세이브 보존.** 플레이 테스트 전 `save.json`, `save.bak`, `run_log.csv`, `settings.json`을 persistentDataPath 아래 `<작업명>_tests\dev_current_<시각>\`에 복사, 끝나면 `cp -pf`로 되돌리고 `cmp`로 확인.
11. **긴 대기 금지.** 10~20초 `ping -n` 한 번씩. 3분 폴링 루프는 거절당했다.
12. 개발자는 한국어로 쓰고 한국어 답을 기대한다.

### 4.3 보고서 형식

`Docs/Reports/<작업>.md`: 표 머리(스펙, 브랜치, 커밋 해시, Validate Data, 테스트, 캡처, 하지 않은 것) → 만든 것 → 확인 방법(키) → 바로 바꿀 수 있는 값 표(필드, 지금 값, 바뀌는 것, 즉시/다음 플레이) → 캡처 목록 → 스펙과 다르게 한 것 → 알려진 문제. 캡처는 `Docs/Reports/<작업>_Screens/`. 기획 세션은 보고서의 질문에 스펙 다음 절(예: "§10은 2주차 보고에 대한 답")로 답한다.

---

## 5. 도구와 환경

### 5.1 머신

Windows 11, Claude 데스크톱 앱(Code 탭), PowerShell 5.1 + Git Bash, Python 3.12(uv, python-pptx), **Node 없음**, Office 16 있음(PowerPoint COM으로 pptx 렌더). 자매 프로젝트 `C:\Users\Smin\Programming\Peak`가 같은 MCP 서버를 쓴다. Hub가 만든 `_Incremental` 폴더 잔재가 있을 수 있다(캐시만).

### 5.2 Unity MCP (CoplayDev MCP for Unity v10.2.0)

- 서버 하나(`127.0.0.1:8080`)에 열린 에디터가 전부 플러그인으로 붙는다. 인스턴스 id **`Incremental@08e7bc3397e05b08`** (Peak는 `Peak@1167be47d0f04dce`). 세션마다 `set_active_instance` 먼저. 서버 재연결로 선택이 풀리면 다시 호출.
- 어느 에디터든 닫히면 공유 서버가 죽을 수 있다 → Window > MCP for Unity > Start Server.
- 커넥터가 "failed"인데 `curl …/api/instances`가 되면 `/mcp`로 재연결해야 한다. 그동안은 Python으로 MCP streamable HTTP를 직접 말해도 된다.
- **`execute_code`는 CodeDom C# 6**: 람다 OK, 로컬 함수·`out var`·튜플·`in` 매개변수 불가(`ref`로), `internal`은 리플렉션. `File.Delete` 차단(Bash로).
- 새 .cs 파일은 `refresh_unity mode=force scope=all`. `refresh_unity`가 60초 타임아웃을 내도 컴파일은 끝난 경우가 많다 → 콘솔에서 `error CS`만 확인. 리컴파일 대기는 `curl …/api/instances`의 `connected_at` 변화를 폴링(약 30초).
- 긴 C#·Python 소스는 Bash 히어독으로 넣으면 깨진다 → **Write 도구로 파일을 쓰고 실행**.
- 플레이 모드 팁: 첫 `execute_code`의 `Screen.width/height`는 에디터 패널 값이니 `root.Cam.pixelWidth/Height`를 쓴다. 툴 호출마다 에디터 메인 스레드가 멈추므로 **짧은 연출은 같은 호출 안에서 `Tools.After` 중첩 체인 + 연출 자체 시계로 `DebugTools.ScheduleCaptures`**. 여러 `After`를 나란히 걸면 같은 프레임에 다 터진다(중첩할 것). `Time.unscaledDeltaTime`은 스톨 뒤 캡이 없다.
- 키 시뮬레이션 `InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.X))` + 다음 호출에서 해제. **Game 뷰에 포커스가 없으면 버려진다**(`PointersAndKeyboardsRespectGameViewFocus`) → 플로우 테스트는 핸들러를 리플렉션으로 직접 호출하고 보고서에 적는다.
- 봇 측정: `Time.timeScale = 8`(또는 20 + `maximumDeltaTime = 1`), `root.Settings.pauseOnFocusLoss = false`, `AutoplayBot.stopAfterEndlessRuns = 0`, 리셋과 봇 켜기는 같은 호출에서. 완주 한 번 약 10분 실제 시간. `run_log.csv`를 Bash until 루프로 감시(열 16 = max_tier).
- 에디터 정적 필드는 플레이 모드 도메인 리로드로 지워진다(밸런스 하네스가 두 번 조용히 실패한 원인).
- ScriptableObject를 `execute_code`로 바꾼 값은 메모리에만 남는다 → 플레이 종료 전 복구. 코드 기본값 → 에셋은 `CopyParamsFromCode` / `CopyAbilitiesFromCode(경로…)`. `SyncAbilitiesFromCode()`는 개발자 튜닝을 덮으니 쓰지 않는다.
- 스크린샷: `manage_camera screenshot`은 프로젝트 상대 폴더(`Captures`)만. `ScreenCapture.CaptureScreenshot(절대경로)`는 어디든. 1초 미만 간격의 연속 캡처는 궤적이 다각형처럼 보인다.
- 패키지 제거: `manifest.json` 편집 → `Client.Resolve()` → 약 20초 대기. 제거 전 `Library/PackageCache/<pkg>@*`의 `.meta` guid를 `Assets` + `ProjectSettings`에서 검색해 참조 확인.
- 프로젝트는 Linear 색공간: 가산 글로우는 0.01~0.04 수준, 쿼드 가장자리에서 정확히 0으로 끝나게.

### 5.3 빌드와 성능

- 윈도우 빌드를 `-perfbench`로 켜면 장면을 스스로 만들어 재고 표를 남긴 뒤 종료(`PerfBench`). `-perfvortex` / `-perftrails` / `-perfworld`로 값 변경. 1920×1080 독점 전체화면, vSync 0, 예열 2초 + 표본 6초.
- 7주차 기준선(`Docs/Reports/Week7_Data/perf_final.md`): 목표 장면 "런, 먼지 8,000 + 궤적 300"에서 **평균 117.5fps / 99퍼센타일 89.9fps** (목표 60 / 50의 1.8배). 결과 화면 천체 129개 평균 415fps.
- 최초 측정이 전 장면 정확히 60.0fps였던 것은 `com.unity.ai.assistant` 오염. 제거 후 상한 소멸, 먼지 틱 4.5배 빨라짐.
- 마지막 빌드는 7주차. `Builds/Win64/`에 있다. 그 뒤 변경(패키지 제거, TMP 런타임 폰트, 능력, 화면 밖 먼지, 과노출 억제)은 **빌드 미검증** → 다음 작업.

### 5.4 세이브와 로그

`%USERPROFILE%\AppData\LocalLow\DefaultCompany\Incremental\`: `save.json`, `save.bak`, `settings.json`, `run_log.csv`(런 통계, 밸런스 분석 자료), `screenshot_*.png`(F12), `snapshots/`(비어 있음), 과거 테스트 백업 폴더들(`human_playtest_*`, `abilities_proto_tests`, `ui_proto_tests`, `week*_tests` …). 제품명·회사명이 `DefaultCompany/Incremental`이라 **이름(D7)을 바꾸면 경로가 바뀐다** → 외부 테스터 섭외 직전에 한 번에.

### 5.5 pptx

python-pptx + PowerPoint COM(`$pres.Export(dir,"PNG",1600,900)`). 한글 줄바꿈은 `run.font.language_id = MSO_LANGUAGE_ID.KOREAN`. 기존 덱 `Docs/Planning_Summary.pptx`(9/17). 메모리에 `Docs/Results_Week1-2.pptx`(10/1)도 적혀 있으나 지금 `Docs/`에는 없다. "N주차"는 달력이 아니라 **작업 묶음**.

---

## 6. 핵심 수치와 사실

| 항목 | 값 | 출처 |
|---|---|---|
| 분량 | 봇 완주 76런 112분 → **사람 기준 약 2.2시간** (목표 2~4시간 충족) | 23 §1 |
| 정체 | 배율 1.0 미만 런 20런당 2.7회 (기준 3회 이하) | 16 §3 |
| 성능 | 빌드 117.5 / 99퍼센타일 89.9fps | 7주차 |
| 월드 확장 배율 | `worldScalePerRing` **0.0375** (스펙 0.06 아님). 막는 것은 밸런스. 링 11에서 중력 고리 지름이 화면 높이의 약 70%(링 1은 33%) → 밸런스 패스에서 55~60% 목표 | 23 §3-1, W8-4 |
| 먼지 상한 | `dustCapCeiling`에 묶임. 링 11 약 7,000. 화면 둘레 200px 여백에 상한의 15%까지 별도 생성 | W8-1 |
| 필요 질량 감소 상한 | `GameParams.maxReduction` 0.9 (0.8 비교는 밸런스 패스) | 23 §3-6 |
| 스킬트리 | 방사형 7축(먼지 30°, 질량 60°, 파편 140°, 행성 150°, 능력 240°, 추종 320°, 중력 330°), 링 1~11 + 엔들리스, 관문 노드가 링 경계. 노드 86개(`NodeTable.asset`) | 12 |
| 능력 | 수축(링 4, 강화 링 5: 범위 화면 40% → 전역), 위성(링 6, 강화 링 7: 1~4기, 타원 궤도), 유성(링 8, 강화 링 9). 전부 자동 발동, 홀드 중 충전 | 24 |
| UI | 방향 A(관측 기기), 스태미나 (a) 위쪽 막대, Pretendard(OFL) 동봉, TMP 동적 SDF | 26, W8-3 |
| 테스트 | EditMode 약 227개, 마지막 실행 통과(10/6) | UI_Proto |
| 세이브 | 버전 3, 마이그레이션·깨진 파일 복구 동작 | 16 §3 |
| 유니티 | 6000.0.66f2, 패키지: 2D feature, Input System 1.17, URP 17.0.4, TMP(ugui 2.0), test-framework 1.6, unity-mcp v10.2.0 | manifest |

---

## 7. 열린 결정 (개발자 몫)

| # | 결정 | 시점 |
|---|---|---|
| D7 | **게임 이름과 회사명** (세이브 경로가 묶임) | 외부 테스터 섭외 직전. 기획 세션이 후보안 + Steam 중복 확인 |
| D13 | 배경음 조달 (효과음은 코드 합성 확정) | 출시 기능 착수 전 |
| D18 | 도전과제 20~30개 방향 | 출시 기능 착수 전. 조사·초안은 기획 세션 |
| D11 | 크레딧 문구 (`End` 키로 확인) | 출시 기능 중 |
| D10 | "판매" 표시 문구 | 로컬라이제이션 때 |
| D5 / D8 | 가격($3.99 추천) / 데모 | 출시 판단 후 |
| C3-12, C3-6 | **어떤 능력을 추가할지**(기획 후보: 블랙홀 링 10, 충격파 링 2, 강화 진화), **강화 레벨 수**(3은 임시) | 능력 마무리 단계. 개발자가 레퍼런스 수집 중 |
| - | 능력 "위성"과 티어 4 "위성" 이름 겹침 | 능력 마무리 |
| 26 §10.1 | **트리 배치 나선 팔 안 채택 여부** (비틀기 각도는 키로 비교해 고른다) | 채택 대기 |
| - | 30분 측정 플레이 (완성 기준 "손맛" 칸) | 능력 뒤 |
| - | `origin` 푸시 여부 (§8) | - |

---

## 8. 리스크와 주의

- **9/18 이후 커밋이 전부 로컬에만 있다** (`origin/main` = `f1844ef`). 계정 이전 중 저장소가 날아가면 복구 불가. 개발자가 `git push origin main abilities`를 하는 것을 권한다. Claude가 임의로 푸시하지 않는다.
- **빌드 미검증 변경이 셋 쌓였다** (R25). 다음 작업 B~D가 그것이다.
- **기한 없는 출시** (R24): 완성 기준 표의 빈칸(소리, 메뉴, 옵션, 안내, 언어, 폰트, 이름, 도전과제, 크레딧)을 먼저 채운다. 능력 개수는 완성 기준에 없다. 능력 추가는 3단계 안에서 끝낸다.
- 개발자 확인 왕복이 일정을 정한다. 구현은 병목이 아니다(한 단계 하루 안팎).
- Claude 자동 메모리 폴더: `C:\Users\Smin\.claude\projects\C--Users-Smin-Programming-Incremental\memory\` (파일 13개). 계정을 바꿔도 디스크에 남아 있을 수 있다. 남아 있으면 그대로 쓰고, 없으면 §9가 그 요약이다.

---

## 9. 이전 세션 자동 메모리 요약

원본은 위 메모리 폴더의 파일들이다. 핵심만 옮겼다.

| 파일 | 내용 |
|---|---|
| `user-korean-unity-dev` | 개발자 Smin(GitHub Smin1004), 한국어, Unity 6 Windows, Hub `_Name` → git clone `Name` 습관, 프로젝트당 기획 + 워커 다중 세션 |
| `planning-session-role` | 기획 세션은 코드 없이 md만. 스펙 + 프롬프트 → 개발자가 워커에 붙여 넣기 → 보고서 → 스펙 갱신 |
| `incremental-game-design` | §1~§2, §6의 게임 요약과 능력·UI 구현 구조(AbilityTable 한 행 + Ability 서브클래스 + 노드 둘, 공용 스탯 `ability_power/cooldown/count/amount/range`, `UIStyle`이 시작 시 SDF 폰트 생성, `UIShape` + `UIPanel` 셰이더, 과노출 억제 4종, `ScreenTransition`, `GateExpandFx`) |
| `feedback-korean-commit-messages` | §4.2-1 |
| `feedback-prototype-first` | §4.2-2 |
| `feedback-debug-keys-and-logs` | §4.2-3 |
| `feedback-midtask-spec-updates` | §4.1의 "Docs가 움직이면 한 줄 알리고 계속" |
| `feedback-font-license-and-temporary` | §4.2-7 |
| `feedback-discretion-then-report` | §4.2-8 |
| `unity-mcp-shared-server` | §5.2 앞부분 |
| `unity-mcp-workflow-tips` | §5.2 뒷부분 전체 |
| `pptx-tooling-windows` | §5.5 |

---

## 10. 문서 지도 (`Docs/`)

읽는 순서는 역할에 따라 다르다. **기획 세션**: 16 → 90 → 최신 스펙(24, 26) → 최신 보고서. **구현 세션**: CLAUDE.md → 최신 프롬프트 → 그 스펙 절 → 해당 보고서의 "확인 방법"·"값 표" → 17.

| 번호 | 문서 | 한 줄 |
|---|---|---|
| 00 | Overview | 정의, 확정 사항, 설계 원칙, 옛 9주 마일스톤(기록용) |
| 01, 02, 03 | 테마 후보 / 진행 골격 / Steam 체크리스트 | 초기 기획 |
| **10** | Core_Loop | 핵심 루프, 티어표, 런 구조. §8 "처음부터 지킬 것"(연출 강도, 문자열 테이블, 데이터 주도) |
| 11 | Prototype_Spec | 1주차 |
| **12** | SkillTree_Spec | 트리 규칙, 데이터 모델, 축 배치(§5), 밸런스 절차(§7) |
| 13, 14, 15 | 결과 태양계 / 행성 비주얼 / 엔딩 | 3~5주차, 구현 완료 |
| **16** | Release_Roadmap | **완성 기준 표(§3), 남은 단계(§3b), 결정 목록(§6)** |
| **17** | Playtest_Guide | 디버그 키 전체 표, 점검 방법, 지금 확인이 밀린 것 |
| 18, 19 | 1차 점검 피드백 / 하위 티어 대책 | 결정됨 |
| 20, 21 | 6주차 스펙 / 검토 | 흡인 보정, 먼지 종류, 파편, 가격 곡선 |
| 22, 23 | 7주차 스펙 / 검토 | 소용돌이·궤적, 화면 확장, 빌드 성능, 밸런스. **23 §3이 그 뒤 방향을 정했다** |
| **24** | Ability_Spec | 능력 1~3차 + §18 개발자 확인과 4차(견인) |
| 25 | Reference_FishLab | 능력 연출의 기준 장면("장치 작동 시 군체가 한 번에 반응") |
| **26** | UI_Pass_Spec | UI 방향 A, 기반, 화면별, 월드 연출, §9 2·3단계 범위, §10 개발자 확인과 트리 배치 |
| **90** | Backlog | **결정 기록(D·E·W·C·S·R 번호), 보류, 제안, 리스크, 용어** |
| Prompts/ | 작업 프롬프트. 최신 **Proto5_Build.md** (Build_Check1.md → Satellite_Build.md → Proto5_Build.md로 합쳐짐) |
| Reports/ | 구현 보고 11개 + 캡처 폴더. 최신 **Abilities_Proto.md**(§1~8), **UI_Proto.md**(§1~12). 성능·밸런스 원본은 `Week7_Data/` |

번호 접두사 규칙: 00대 개요, 10대 루프·스펙, 20대 주차 스펙/검토와 그 뒤 스펙, 90 백로그. **현재 인계 문서는 항상 가장 큰 번호**다(지금 24, 26).

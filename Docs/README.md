# 기획 문서 색인

이 폴더는 게임 기획 문서를 담는다. `Assets` 바깥에 두어 Unity가 임포트하지 않는다.

| 문서 | 내용 | 상태 |
|---|---|---|
| [HANDOVER.md](HANDOVER.md) | **인수인계 요약 (새 세션이 먼저 읽는다).** 현재 상태, 세션 역할과 규칙, 도구·환경, 핵심 수치, 열린 결정, 문서 지도 | 2026-10-07 |
| [00_Overview.md](00_Overview.md) | 프로젝트 정의, 확정 사항, 보류 사항, 목표, 마일스톤(9주) | 확정 |
| [01_Concept_Candidates.md](01_Concept_Candidates.md) | 테마 후보 비교. 우주 채택 기록 | 결정됨 |
| [02_Progression_Skeleton.md](02_Progression_Skeleton.md) | 계층, 수식, 런 단위 시간축, 공통 시스템 | 제안 |
| [03_Steam_Release.md](03_Steam_Release.md) | Steam 출시 체크리스트 | 검토중 |
| [10_Core_Loop.md](10_Core_Loop.md) | **핵심 루프.** 먼지, 홀드 중력, 행성, 런 구조, 엔딩. §8 처음부터 지킬 것 | 골격 확정 + 세부 제안 |
| [11_Prototype_Spec.md](11_Prototype_Spec.md) | 1주차 프로토타입 스펙 | 완료 |
| [12_SkillTree_Spec.md](12_SkillTree_Spec.md) | **스킬트리 스펙.** 규칙, 데이터 모델, 기존 코드 대응, 링 1~3 노드 초안, 링 4~11 틀, 밸런스 절차. 3주차에 §8(모델) → §9(화면). §10은 2주차 보고에 대한 답 | 구조 확정, 수치 초안 |
| [13_Result_SolarSystem_Spec.md](13_Result_SolarSystem_Spec.md) | **결과 화면 태양계 스펙.** 런 중 퇴장 연출, 궤도, 소행성대, 중심 항성 단계, 명암, 등장 연출. 3주차 | 방향 확정, 수치 제안 |
| [14_Planet_Visual_Spec.md](14_Planet_Visual_Spec.md) | 행성 비주얼 스펙 (4주차). 행성 셰이더와 티어별 외형, 형성 중인 천체, 먼지, 항성 점화와 배경, 성장 강조. §8은 3주차 보고들에 대한 답 | 구현 완료 |
| [15_Ending_Spec.md](15_Ending_Spec.md) | 엔딩과 완주 스펙 (5주차). 4주차 검토 반영, 초신성 엔딩 시퀀스, 엔들리스 상태, 봇 완주와 링 스냅샷 | 구현 완료 |
| [16_Release_Roadmap.md](16_Release_Roadmap.md) | **출시 로드맵.** 출시 방식(판단 기준), **완성 기준 표**, 남은 개발 단계, 판단 후 절차 역산, 결정 목록 | 확정 |
| [26_UI_Pass_Spec.md](26_UI_Pass_Spec.md) | **UI·연출 보강 (현재 인계 문서).** 방향 A(관측 기기) 확정, 기반(Pretendard, TMP, UITheme, UI 셰이더), 화면별 보강, 월드 연출 3건, §9 2단계 범위 | 확정 |
| [25_Reference_FishLab.md](25_Reference_FishLab.md) | **레퍼런스: Fish Lab.** 개발자가 꼽은 장면(장치 작동 시 군체가 한 번에 반응), 옮길 원칙 4개, 강화 진화 후보, 가져오지 않을 것 | 참고 |
| [24_Ability_Spec.md](24_Ability_Spec.md) | **특수 능력 (현재 인계 문서).** 1~7차와 개발자 회신. 능력 5개(증폭 결정, 수축, 위성, 유성, 블랙홀), 진화 2개, 묶음 완성. **§22가 최신: 7차 회신과 능력 단계 마감 질문** | 확정 |
| [23_Week7_Review.md](23_Week7_Review.md) | **7주차 검토 (최신).** 판정, 스크린샷 검토, 보고서 §9 답 8건, 완성 기준 갱신(분량 2.2시간), 남은 어긋남, 다음 단계 묶음 | 검토 완료 |
| [22_Week7_Spec.md](22_Week7_Spec.md) | **7주차 스펙 (현재 인계 문서).** 추종 속도 기본 적용, 빌드 성능 기준선, 소용돌이와 궤적, 화면 확장, 최적화와 상한, 밸런스 재조정 | 확정 |
| [21_Week6_Review.md](21_Week6_Review.md) | **6주차 검토.** 판정, 사람 플레이 기록 분석(홀드 비율 92%), 정지 홀드 기준 폐기, 포획 처리량 문제와 재조정 목표, 보고서 질문 답 | 검토 완료 |
| [20_Week6_Spec.md](20_Week6_Spec.md) | 6주차 스펙. 먼지 흡인 보정, 먼지 종류, 파편, 5주차 답 3건, 추종 속도 실험, 가격 곡선 완화와 링별 밸런스 | 확정 |
| [19_LowTier_Options.md](19_LowTier_Options.md) | **하위 티어가 안 나오는 문제.** 원인 계산, 방안 7개 비교, 결정(가격 곡선 완화 + 파편)과 파편 트리 설계 | 결정됨 |
| [18_Feedback_Round1.md](18_Feedback_Round1.md) | **1차 점검 피드백 정리.** 즉시 수정 3건, 먼지 흡인 보정, 먼지 종류, 다양성 보너스, 특수 능력, 이동 제한 실험, 일정 영향 | 처리안 |
| [17_Playtest_Guide.md](17_Playtest_Guide.md) | **점검 안내 (개발자용).** 디버그 키와 에디터 메뉴, 자유 점검과 측정 플레이의 구분, 불편한 점 넘기는 형식, 바로 고칠 수 있는 값 목록 | 안내 |
| [Prompts/](Prompts/) | 구현 세션에 붙여 넣는 작업 프롬프트. 최신은 [Proto13_TreeV2.md](Prompts/Proto13_TreeV2.md) (스킬트리 v2) | - |
| [29_SkillTree_v2.md](29_SkillTree_v2.md) | **스킬트리 v2 (초안).** 카테고리 5개(천체·먼지·중력·시간·능력), 곁가지, 노드 약 141~145개, 규칙 노드 후보 21개(개발자 고르기 대기) | 초안 |
| [28_Balance_Spec.md](28_Balance_Spec.md) | **마지막 밸런스 패스 (초안, 현재 인계 문서).** 시간제 판, 목표 곡선(1시간 40분 ~ 2시간), 개발자 측정 플레이 시트, 트리 재구성(의견 대기) | 초안 |
| [27_PreBalance_Review.md](27_PreBalance_Review.md) | **밸런스 패스 전 점검 (10/14).** 먼저 할 일(봇이 결정 쫓기, 사람/봇 비율), 이후 일정(판단 12월 첫 주), 개발자 확인 목록, 냉정한 평가, 취약점, 시각 만족도 방안 | 검토 |
| [90_Backlog.md](90_Backlog.md) | 결정 기록, 보류, 제안, 리스크, 용어 | - |
| [Reports/Prototype_Week1.md](Reports/Prototype_Week1.md) | 구현 세션의 1주차 보고. 개발자 기입 항목이 남아 있다 | 구현 세션 작성 |
| [Reports/Week2.md](Reports/Week2.md) | 구현 세션의 2주차 보고. §3 질문 9개의 답은 12 §10 | 구현 세션 작성 |
| [Reports/SkillTree_Step1.md](Reports/SkillTree_Step1.md) | 스킬트리 데이터 모델 전환 보고. 링별 실측 수입. §5 질문의 답은 14 §8 | 구현 세션 작성 |
| [Reports/Week3_Tree_SolarSystem.md](Reports/Week3_Tree_SolarSystem.md) | 트리 화면과 결과 태양계 보고. §3 질문의 답은 14 §8 | 구현 세션 작성 |
| [Reports/Week4_Visuals.md](Reports/Week4_Visuals.md) | 행성 비주얼 보고와 스크린샷(`Week4_Screens/`). §7 질문의 답은 15 §3 | 구현 세션 작성 |
| [Reports/Week5_Ending.md](Reports/Week5_Ending.md) | 엔딩과 봇 완주 보고, 스크린샷(`Week5_Screens/`). §7 질문의 답은 16 §8 | 구현 세션 작성 |
| Reports/QuickFix1.md | 즉시 수정 1차 보고. `quickfix1` 브랜치에 있고 6주차 프롬프트 첫 단계에서 병합된다 | 구현 세션 작성 |
| [Reports/Week6.md](Reports/Week6.md) | 6주차 보고. 흡인 보정, 먼지 종류, 파편, 가격 곡선. 검토는 [21](21_Week6_Review.md) | 구현 세션 작성 |
| [Reports/Week7.md](Reports/Week7.md) | 7주차 보고. 소용돌이와 궤적, 화면 확장, 빌드 성능, 완주 런 10회. 검토는 [23](23_Week7_Review.md) | 구현 세션 작성 |

## 상태 표기

- **확정**: 개발자가 결정함. 구현 세션이 그대로 따른다.
- **검토중**: 내용은 있으나 개발자 확인 전.
- **제안**: 기획 세션이 낸 안. 채택 여부는 개발자가 정한다.

## 규칙

- 기획 세션은 코드를 쓰지 않는다. 문서만 관리한다.
- 제안은 본문과 분리해 "제안" 또는 "추천"으로 표기한다.
- 결정이 나면 `90_Backlog.md`의 결정 기록에 적고 해당 문서를 "확정"으로 올린다.
- **지금 (10/14): 능력 단계 확정.** 12차 완료(궤도석 궤도, 시각 보강 26 §12.5). **다음 프롬프트 `Prompts/Proto13_TreeV2.md`**(스킬트리 v2, 1단계 시안 뒤 멈춤, 규칙 노드는 그때 고름) → 밸런스 패스. 그 뒤 밸런스 패스(`28_Balance_Spec.md`, 트리 의견 대기). 일정은 `27_PreBalance_Review.md` §7.1. 일정 전체는 `16_Release_Roadmap.md`, 인계는 `HANDOVER.md`.
- 구현 세션의 보고는 `Reports/`에 둔다. 기획 세션은 보고를 받아 수치와 스펙을 갱신한다.
- **커밋 메시지는 한국어로 쓴다.** 상세와 예시는 저장소 루트의 [CLAUDE.md](../CLAUDE.md).

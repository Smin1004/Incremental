# 기획 문서 색인

이 폴더는 게임 기획 문서를 담는다. `Assets` 바깥에 두어 Unity가 임포트하지 않는다.

| 문서 | 내용 | 상태 |
|---|---|---|
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
| [16_Release_Roadmap.md](16_Release_Roadmap.md) | **출시 로드맵.** 출시일 두 안, 날짜 일정, 남은 개발 작업, 개발자가 직접 할 일, 결정 목록, 새로 발견한 공백, 5주차 보고 답 | 제안 |
| [18_Feedback_Round1.md](18_Feedback_Round1.md) | **1차 점검 피드백 정리.** 즉시 수정 3건, 먼지 흡인 보정, 먼지 종류, 다양성 보너스, 특수 능력, 이동 제한 실험, 일정 영향 | 처리안 |
| [17_Playtest_Guide.md](17_Playtest_Guide.md) | **점검 안내 (개발자용).** 디버그 키와 에디터 메뉴, 자유 점검과 측정 플레이의 구분, 불편한 점 넘기는 형식, 바로 고칠 수 있는 값 목록 | 안내 |
| [Prompts/](Prompts/) | 구현 세션에 붙여 넣는 주차별 작업 프롬프트. 최신은 [Week5_Prompt.md](Prompts/Week5_Prompt.md) | - |
| [90_Backlog.md](90_Backlog.md) | 결정 기록, 보류, 제안, 리스크, 용어 | - |
| [Reports/Prototype_Week1.md](Reports/Prototype_Week1.md) | 구현 세션의 1주차 보고. 개발자 기입 항목이 남아 있다 | 구현 세션 작성 |
| [Reports/Week2.md](Reports/Week2.md) | 구현 세션의 2주차 보고. §3 질문 9개의 답은 12 §10 | 구현 세션 작성 |
| [Reports/SkillTree_Step1.md](Reports/SkillTree_Step1.md) | 스킬트리 데이터 모델 전환 보고. 링별 실측 수입. §5 질문의 답은 14 §8 | 구현 세션 작성 |
| [Reports/Week3_Tree_SolarSystem.md](Reports/Week3_Tree_SolarSystem.md) | 트리 화면과 결과 태양계 보고. §3 질문의 답은 14 §8 | 구현 세션 작성 |
| [Reports/Week4_Visuals.md](Reports/Week4_Visuals.md) | 행성 비주얼 보고와 스크린샷(`Week4_Screens/`). §7 질문의 답은 15 §3 | 구현 세션 작성 |
| Reports/Week5_Ending.md | 엔딩과 봇 완주 보고, 스크린샷(`Week5_Screens/`). `ending` 브랜치에 있고 다음 작업 첫 단계에서 병합된다. §7 질문의 답은 16 §8 | 구현 세션 작성 |

## 상태 표기

- **확정**: 개발자가 결정함. 구현 세션이 그대로 따른다.
- **검토중**: 내용은 있으나 개발자 확인 전.
- **제안**: 기획 세션이 낸 안. 채택 여부는 개발자가 정한다.

## 규칙

- 기획 세션은 코드를 쓰지 않는다. 문서만 관리한다.
- 제안은 본문과 분리해 "제안" 또는 "추천"으로 표기한다.
- 결정이 나면 `90_Backlog.md`의 결정 기록에 적고 해당 문서를 "확정"으로 올린다.
- **지금 기준 문서는 `16_Release_Roadmap.md`와 `18_Feedback_Round1.md`다.** 11~15는 구현이 끝났다. 다음 작업은 `Prompts/QuickFix_1.md`(즉시 수정)이고, 6주차 본 스펙은 C1·C2·C3·D 결정 뒤에 쓴다. 배경이 필요할 때만 10을 본다.
- 구현 세션의 보고는 `Reports/`에 둔다. 기획 세션은 보고를 받아 수치와 스펙을 갱신한다.
- **커밋 메시지는 한국어로 쓴다.** 상세와 예시는 저장소 루트의 [CLAUDE.md](../CLAUDE.md).

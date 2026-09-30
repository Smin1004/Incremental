# 7주차 보고서: 궤적, 화면 확장, 성능, 밸런스

| 항목 | 값 |
|---|---|
| 스펙 | [22_Week7_Spec.md](../22_Week7_Spec.md) |
| 브랜치 | `scale` (main 병합 안 함) |
| 커밋 | A `3d55d82` · B·C·D `cfcc439` · E `e9a8e60` · 캡처 `b20ae85` · F (아래) |
| EditMode 테스트 | 전부 통과 (7주차 시작 시 197개 → 종료 시 (아래)) |
| Validate Data | 오류 0, 경고 0 |
| 캡처 | `Docs/Reports/Week7_Screens/` (11장) |
| 측정 데이터 | `Docs/Reports/Week7_Data/` (성능 표 6개, 완주 런 로그 5개) |

## 0. 시작 전 처리

- `week6`을 main에 fast-forward 병합했다.
- persistentDataPath에 개발자가 플레이한 `run_log.csv`와 `save.json` / `save.bak`이 **있었다.** 로그 마지막 3런(48~50번, `end_reason=quit`, 28~62초)이 6주차 결과 확인 플레이다. 세 파일을 `human_playtest_20260929_150000\`으로 옮겨 보존했고, 이전 세션이 남긴 `human_playtest_20260922_113319\`와 `quickfix1_debug_20260922_121314\`도 그대로 두었다.
- 커밋되지 않은 기획 문서를 "기획 문서 갱신"으로 커밋한 뒤 main에서 `scale`을 땄다.
- ⚠ 작업 중 기획 세션이 `16_Release_Roadmap.md`, `90_Backlog.md`, `00_Overview.md`, `README.md`를 고쳤고, 그 변경이 B·C·D 커밋에 같이 실려 들어갔다. 내용은 건드리지 않았다.

<!--F-->

## 8. 스펙과 다르게 했거나 스스로 정한 것

<!--DECISIONS-->

## 9. 기획이 볼 것

<!--PLANNING-->

## 10. 알려진 문제

<!--ISSUES-->

## 11. 파일

| 종류 | 경로 |
|---|---|
| 캡처 | `Docs/Reports/Week7_Screens/` |
| 성능 측정 표 | `Docs/Reports/Week7_Data/perf_*.md` |
| 완주 런 로그와 분석 | `Docs/Reports/Week7_Data/balance_round*.csv`, `balance_round*.md` |
| 링 스냅샷 | `<persistentDataPath>/snapshots/ring_2..11.json` (마지막 완주 런) |
| 보존한 개발자 플레이 기록 | `<persistentDataPath>/human_playtest_20260929_150000/` 외 2개 |

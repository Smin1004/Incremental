# 성능 측정 (11차, 먼지 8,000만, 보충 가속 0)

| 장면 | 프레임 | 평균 fps | 99퍼센타일 fps | 최대 프레임 ms | 먼지 틱 ms | 메시 재구성 ms | 궤적 메시 ms | 나머지 ms | 궤적 |
|---|---|---|---|---|---|---|---|---|---|
| 런 화면, 먼지 8,000개, 홀드 중 | 694 | 115.7 | 56.0 | 22.5 | 1.38 | 3.58 | 1.48 | 2.22 | 300 |
| 런 화면, 먼지 8,000개 + 능력 (블랙홀 제외, 트리대로) + 화면 밖 먼지, 홀드 중 | 85 | 13.8 | 2.1 | 482.0 | 59.56 | 7.73 | 3.13 | 1.78 | 300 |
| 런 화면, 먼지 8,000개 + 능력 전부 최대 레벨 (블랙홀 열림, 먼지 즉시 보충) + 화면 밖 먼지, 홀드 중 | 79 | 13.1 | 4.3 | 230.9 | 57.56 | 8.51 | 2.69 | 7.78 | 300 |

| 장면 | 능력 틱 | 궤적 이력 | Update 스크립트 | LateUpdate 스크립트 | FixedUpdate 스크립트 | 렌더 (메인) | GPU·렌더 스레드 대기 | UI 캔버스 |
|---|---|---|---|---|---|---|---|---|
| 런 화면, 먼지 8,000개, 홀드 중 | 0.02 | 0.01 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 |
| 런 화면, 먼지 8,000개 + 능력 (블랙홀 제외, 트리대로) + 화면 밖 먼지, 홀드 중 | 1.38 | 0.26 | 0.00 | 0.00 | 0.00 | 0.00 | 0.01 | 0.00 |
| 런 화면, 먼지 8,000개 + 능력 전부 최대 레벨 (블랙홀 열림, 먼지 즉시 보충) + 화면 밖 먼지, 홀드 중 | 0.37 | 0.16 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 | 0.00 |
마커: Update.ScriptRunBehaviourUpdate (기록 안 됨), PreLateUpdate.ScriptRunBehaviourLateUpdate (기록 안 됨), FixedUpdate.ScriptRunBehaviourFixedUpdate (기록 안 됨), PostLateUpdate.FinishFrameRendering (기록 안 됨), Gfx.WaitForPresentOnGfxThread, PostLateUpdate.PlayerUpdateCanvases (기록 안 됨)
dustRefillBoost 0, gravityRadius 150, gravityGrowthScale 0.7

1920×1080, 빌드, vSync 0, FullScreenWindow, vortexShare 0.25, trailBudget 300, worldScalePerRing 0.0375, worldScale 1.38, dustCap 8000, dustMarginPx 200 (마지막 장면만), 예열 2초 / 표본 6초

---
id: EVENT.JUNGLE_MAZE_ADVENTURE
name: 정글 미로 탐험
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/JungleMazeAdventure.cs sha256:06000356af1201b69c1ce9e4ef1d3506ff96b98665924c64df6f3820f0dbc944
- MegaCrit.Sts2.Core.Commands/PlayerCmd.cs sha256:c359ad806bbd92144ffbc80c85ecf86ef13a560a17bc5075548081aaa7c2db7c
- MegaCrit.Sts2.Core.Random/Rng.cs sha256:99a404a807dae7b3c786821b3c287e6518b573f58265f98feaa63132748b7f40
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 정글 미로 탐험 (JUNGLE_MAZE_ADVENTURE)

**요약**: 체력 18을 내고 골드 약 135~164를 얻거나, 대가 없이 골드 약 35~64를 얻는다.

## 조건
- 1막(`Overgrowth`) 이벤트. 별도 등장 조건 없음.
- 공유 이벤트(멀티플레이에서 투표로 고른 선택지가 모든 플레이어에게 적용).

## 선택지
| 선택지 | 효과 |
|---|---|
| 홀로 탐색한다 (`SOLO_QUEST`) | 체력 18 잃음(방어 무시, 보정 없음) → 골드 150±15 획득. 이벤트 종료 |
| 협력한다 (`JOIN_FORCES`) | 골드 50±15 획득. 이벤트 종료 |

## 결과 상세
- 두 골드 양은 이벤트가 열릴 때 각각 기본값에 -15 이상 15 미만의 실수를 더해 정해진다. 실제 획득 시 소수점은 버린다(홀로: 135~164, 협력: 35~64).
- 코드상 불명확: 화면에 표시되는 골드 값이 소수를 어떻게 표시하는지(반올림 여부)는 확인하지 못했다. 표시값과 실제 획득량이 1 차이 날 수 있다.
- 체력 손실이 먼저 적용되고 그다음 골드를 얻는다.

## 플레이 메모
- 홀로 탐색에는 체력 잠금 조건이 없다. 체력이 18 이하이면 고르지 않는다.
- 차이는 체력 18 대 골드 약 100이다. 1막 초반에 상점 방문이 예정되어 있고 체력 여유가 있으면 홀로 탐색이 낫다.

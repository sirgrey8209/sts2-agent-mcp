---
id: MONSTER.SEAPUNK
name: 불량 해초
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/Seapunk.cs sha256:5502426b447c4f7f3d40cdc046d945eae91037d7f90db72816aff5de0ed5d594
- MegaCrit.Sts2.Core.Models.Encounters/SeapunkWeak.cs sha256:9ddbeb8262f02d729998124f684bfcb6f090848d8ab17ba41f64dae36a9773b9
- MegaCrit.Sts2.Core.Models.Encounters/SeapunkNormal.cs sha256:c16009043b6802db13d35de88667dff611bb3f6cf3e6cd06b69801b0d514cd14
- MegaCrit.Sts2.Core.Models.Acts/Underdocks.cs sha256:b5f91ba221afa5ca83ca92290f481c8bef88c28f1ff8272f780f2540700964fb
---
# 불량 해초 (SEAPUNK)

**요약**: 1막의 일반 몬스터. 바다 차기 → 돌려차기(2 × 4회) → 거품 뿜기(방어 + 힘) 3행동 고정 순환. 시작 파워 없음.

## 기본
- HP: 44~46 (A8+ 47~49)
- 등장:
  - 1막(`Underdocks`) 약한 일반 전투 `ENCOUNTER.SEAPUNK_WEAK`, 단독
  - 1막(`Underdocks`) 일반 전투 `ENCOUNTER.SEAPUNK_NORMAL`, 석회화된 광신자(`CALCIFIED_CULTIST`)와 함께
- 전투 시작 시 특별한 파워 없음.

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 바다 차기 (`SEA_KICK_MOVE`) | 공격 | 피해 11 (A9+ 13) |
| 돌려차기 (`SPINNING_KICK_MOVE`) | 공격 ×4 | 피해 2 × 4회 (승천 무관) |
| 거품 뿜기 (`BUBBLE_BURP_MOVE`) | 버프 + 방어 | 자신 방어도 7 (A8+ 8), 이후 자신 힘 +1 (A9+ +2) |

## 패턴
- 시작: 바다 차기.
- 고정 순환: 바다 차기 → 돌려차기 → 거품 뿜기 → 바다 차기 → …
- 무작위 요소 없음.

## 특수 규칙
- 거품 뿜기의 힘은 누적된다. 돌려차기는 4타라 힘 1당 피해가 4 늘어난다.

## 플레이 메모
- 거품 뿜기 턴은 공격이 없다. 공격 카드를 몰아 쓰기 좋은 턴이다.
- 전투가 길어질수록 돌려차기가 크게 강해진다(힘 +1마다 총 피해 +4). 짧게 끝낸다.

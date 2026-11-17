---
id: MONSTER.SNAPPING_JAXFRUIT
name: 포식성 잭스프루트
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/SnappingJaxfruit.cs sha256:d8f124d4fb907f790cb6ff24c558fc836cd45900ec334c0da288dde7bf092957
- MegaCrit.Sts2.Core.Models.Encounters/SnappingJaxfruitNormal.cs sha256:98b2c0081c75abd089feb7819f097aa96870e82e0fbc323cfa830252a758e125
- MegaCrit.Sts2.Core.Models.Encounters/SlitheringStranglerNormal.cs sha256:23ed70bec8d661aafeaf1d5daa8f75fa85daa31df660e6f70ad34fbe29594554
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
- MegaCrit.Sts2.Core.Models/ActModel.cs sha256:55f3a43c7e055aff1e35985c0cd229f22a271698a82b0d0a24a3f01320dcab0b
---
# 포식성 잭스프루트 (SNAPPING_JAXFRUIT)

**요약**: 1막의 작은 식물 몬스터. 매 턴 에너지 구체(작은 공격 + 힘 +2) 하나만 쓴다. 공격력은 약하지만 힘이 매 턴 2씩 쌓여 방치하면 점점 강해진다.

## 기본
- HP: 31~33 (A8+ 34~36)
- 등장:
  - 1막(`Overgrowth`) 일반 전투 `ENCOUNTER.SNAPPING_JAXFRUIT_NORMAL`, 날개버섯(`FLYCONID`)과 함께
  - 1막(`Overgrowth`) 일반 전투 `ENCOUNTER.SLITHERING_STRANGLER_NORMAL`의 동반 적 후보(1/3 확률), 미끈거리는 교살마(`SLITHERING_STRANGLER`)와 함께
  - 두 전투 모두 `Jaxfruit` 태그를 가지므로(앞 전투는 `Mushroom`, 뒤 전투는 `Slimes` 태그도 가짐) 막의 일반 전투 순서를 만들 때 서로 연달아 배정되지 않는다.
- 전투 시작 시 특별한 파워 없음.

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 에너지 구체 (`ENERGY_ORB_MOVE`) | 공격 + 버프 | 피해 3 (A9+ 4), 이후 자신 힘 +2 |

## 패턴
- 에너지 구체만 매 턴 반복한다. 피해는 3 → 5 → 7 → … (A9+ 4 → 6 → 8 → …).
- 무작위 요소 없음.

## 플레이 메모
- 초반 피해는 작지만 매 턴 2씩 강해진다. HP가 낮으므로 함께 나온 적보다 먼저 잡아 성장을 끊는 편이 좋다.

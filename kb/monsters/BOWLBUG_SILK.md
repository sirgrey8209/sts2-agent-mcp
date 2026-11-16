---
id: MONSTER.BOWLBUG_SILK
name: 그릇벌레 (실크)
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/BowlbugSilk.cs sha256:b18fa6abf66e9df9567cb781a589b90ccaf359ec1ba00d4883d3628713c69175
- MegaCrit.Sts2.Core.Models.Powers/WeakPower.cs sha256:b33ac1a44ff5edb75d6e5776627259b2bac2b4e6ab1905cab3d60b0cfa32a426
- MegaCrit.Sts2.Core.Models.Encounters/BowlbugsNormal.cs sha256:9825e8ed31f14a2ca08e5b0e2cc790962362bf59f682b9bf62d9354ad831f0e4
- MegaCrit.Sts2.Core.Models.Encounters/SlumberingBeetleNormal.cs sha256:0ae40808e8c225cbcc724dc14b4439c44a9dbdbd0c849d64d58e10cf90cc0932
- MegaCrit.Sts2.Core.Models.Acts/Hive.cs sha256:085d95a7ea0418ada9e8544efdc97afc01c4bada90eca76fd65d3755c62c784e
---
# 그릇벌레 (실크) (BOWLBUG_SILK)

**요약**: 2막 그릇벌레 전투에 섞여 나오는 적. 유독성 침(약화 1)과 난동부리기(4×2)를 번갈아 쓴다. 첫 턴은 유독성 침.

## 기본
- HP: 40~43 (A8+ 41~44)
- 등장: 2막(`Hive`)
  - 일반 전투 `ENCOUNTER.BOWLBUGS_NORMAL`: 그릇벌레(바위) + 알·실크·꿀 중 서로 다른 무작위 2마리
  - 일반 전투 `ENCOUNTER.SLUMBERING_BEETLE_NORMAL`: 그릇벌레(바위) + 실크 + 잠자는 딱정벌레(`SLUMBERING_BEETLE`)

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 유독성 침 (`TOXIC_SPIT_MOVE`) | 디버프 | 플레이어에게 약화 1 |
| 난동부리기 (`THRASH_MOVE`) | 공격 ×2 | 피해 4 (A9+ 5) × 2회 |

## 패턴
- 시작: 유독성 침.
- 고정 순환: 유독성 침 → 난동부리기 → 유독성 침 → …
- 무작위 요소 없음.

## 특수 규칙
- 약화: 공격 피해 25% 감소.

## 플레이 메모
- 홀수 턴(1, 3, 5 …)에는 이 적의 공격이 없다.

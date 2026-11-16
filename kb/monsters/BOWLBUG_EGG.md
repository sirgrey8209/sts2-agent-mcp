---
id: MONSTER.BOWLBUG_EGG
name: 그릇벌레 (알)
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/BowlbugEgg.cs sha256:c7bdba96f3ae387cbe6189e5a844d6f05d3fcf0a5aa9e5e06d276ba723c8d1ec
- MegaCrit.Sts2.Core.Models.Encounters/BowlbugsWeak.cs sha256:7507c76268d014e7cb13ec1e4342aaf1efb5831317324a612f7fd62c1777ebfc
- MegaCrit.Sts2.Core.Models.Encounters/BowlbugsNormal.cs sha256:9825e8ed31f14a2ca08e5b0e2cc790962362bf59f682b9bf62d9354ad831f0e4
- MegaCrit.Sts2.Core.Models.Acts/Hive.cs sha256:085d95a7ea0418ada9e8544efdc97afc01c4bada90eca76fd65d3755c62c784e
---
# 그릇벌레 (알) (BOWLBUG_EGG)

**요약**: 2막 그릇벌레 무리에 섞여 나오는 적. 매 턴 물기(공격 + 방어) 한 가지만 쓴다.

## 기본
- HP: 21~22 (A8+ 23~24)
- 등장: 2막(`Hive`)
  - 약한 전투 `ENCOUNTER.BOWLBUGS_WEAK`: 그릇벌레(바위) + 그릇벌레(알)/그릇벌레(꿀) 중 무작위 1마리
  - 일반 전투 `ENCOUNTER.BOWLBUGS_NORMAL`: 그릇벌레(바위) + 알·실크·꿀 중 서로 다른 무작위 2마리

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 물기 (`BITE_MOVE`) | 공격 + 방어 | 피해 7 (A9+ 8), 이후 자신 방어도 7 (A9+ 8) |

## 패턴
- 매 턴 물기. 무작위 요소 없음.

## 특수 규칙
- 방어도 수치는 A8이 아니라 A9(치명적인 적)부터 8로 오른다.

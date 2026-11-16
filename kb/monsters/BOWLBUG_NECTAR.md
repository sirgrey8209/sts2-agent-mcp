---
id: MONSTER.BOWLBUG_NECTAR
name: 그릇벌레 (꿀)
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/BowlbugNectar.cs sha256:961b69c0d4dcb4426ce4f4240a8235c95009d89f5852e8686b812b24ec6054ee
- MegaCrit.Sts2.Core.Models.Encounters/BowlbugsWeak.cs sha256:7507c76268d014e7cb13ec1e4342aaf1efb5831317324a612f7fd62c1777ebfc
- MegaCrit.Sts2.Core.Models.Encounters/BowlbugsNormal.cs sha256:9825e8ed31f14a2ca08e5b0e2cc790962362bf59f682b9bf62d9354ad831f0e4
- MegaCrit.Sts2.Core.Models.Acts/Hive.cs sha256:085d95a7ea0418ada9e8544efdc97afc01c4bada90eca76fd65d3755c62c784e
---
# 그릇벌레 (꿀) (BOWLBUG_NECTAR)

**요약**: 2막 그릇벌레 무리에 섞여 나오는 적. 첫 턴 약한 공격 후 2턴째에 힘 +15를 얻고, 그 뒤로는 매 턴 강해진 난동부리기(3 + 힘)를 쓴다.

## 기본
- HP: 35~38 (A8+ 36~39)
- 등장: 2막(`Hive`)
  - 약한 전투 `ENCOUNTER.BOWLBUGS_WEAK`: 그릇벌레(바위) + 그릇벌레(알)/그릇벌레(꿀) 중 무작위 1마리
  - 일반 전투 `ENCOUNTER.BOWLBUGS_NORMAL`: 그릇벌레(바위) + 알·실크·꿀 중 서로 다른 무작위 2마리

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 난동부리기 (`THRASH_MOVE`) | 공격 | 피해 3 |
| 이로운 효과 (`BUFF_MOVE`) | 버프 | 자신 힘 +15 (A9+ +16) |
| 난동부리기 2 (`THRASH2_MOVE`) | 공격 | 피해 3 (힘 적용) |

## 패턴
- 1턴: 난동부리기 → 2턴: 이로운 효과 → 3턴부터: 난동부리기 2를 매 턴 반복.
- 무작위 요소 없음. 이로운 효과는 전투 중 한 번만 쓴다.

## 플레이 메모
- 3턴째부터 매 턴 18 (A9+ 19) 피해가 온다. 2턴 안에 잡거나, 힘을 깎는 수단을 쓰는 것이 좋다.
- 2턴째(이로운 효과)는 공격이 없으므로 이 적에 대해서는 방어가 필요 없다.

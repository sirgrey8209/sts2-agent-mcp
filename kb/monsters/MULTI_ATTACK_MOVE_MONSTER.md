---
id: MONSTER.MULTI_ATTACK_MOVE_MONSTER
name: 대형 더미 인형
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/MultiAttackMoveMonster.cs sha256:7770c888853562d9dcf20c224026780b0012dd81fe61a28293c8c10274f6ec21
---
# 대형 더미 인형 (MULTI_ATTACK_MOVE_MONSTER)

**요약**: 테스트용 몬스터. 어떤 조우(encounter)에도 들어 있지 않아 정상 플레이에서는 나오지 않는다. 표시 이름은 `BIG_DUMMY`의 이름(대형 더미 인형)을 빌려 쓴다.

## 기본
- HP: 999, 고정
- 등장: 없음 (테스트 전용)

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 찌르기 (`POKE`) | 공격 ×5 | 피해 1 × 5회 |

## 패턴
- 매 턴 `POKE`만 반복한다.

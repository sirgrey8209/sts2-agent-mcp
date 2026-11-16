---
id: MONSTER.SINGLE_ATTACK_MOVE_MONSTER
name: 대형 더미 인형
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/SingleAttackMoveMonster.cs sha256:d9bf0ce715f23e765428d7f1019b7a3e90c87013be46d0eb57b1b8e217ea13ab
---
# 대형 더미 인형 (SINGLE_ATTACK_MOVE_MONSTER)

**요약**: 테스트용 몬스터. 어느 막·인카운터에도 들어 있지 않아 일반 플레이에서는 나오지 않는다. 표시 이름은 대형 더미 인형(`BIG_DUMMY`)의 이름을 빌려 쓴다. HP 999, 매 턴 피해 1짜리 찌르기(`POKE`)만 반복한다.

## 기본
- HP: 999 (승천 무관)
- 등장: 없음(테스트용)

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 찌르기 (`POKE`) | 공격 | 피해 1 |

## 패턴
- 찌르기만 계속 반복한다.

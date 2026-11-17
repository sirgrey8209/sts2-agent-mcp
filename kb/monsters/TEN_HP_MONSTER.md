---
id: MONSTER.TEN_HP_MONSTER
name: 대형 더미 인형
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/TenHpMonster.cs sha256:ef44faf2987f4b0f32d9f31b369897ea50fbc58baaf591b78633b31e0b71ce9c
---
# 대형 더미 인형 (TEN_HP_MONSTER)

**요약**: 테스트·디버그용 몬스터. 실제 게임의 어떤 조우(Encounter)에서도 쓰이지 않는다. 이름은 `BIG_DUMMY.name`(대형 더미 인형)을 빌려 쓴다.

## 기본
- HP: 10 (승천 보정 없음)
- 등장: 없음(조우·막 목록에 포함되지 않음)

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 가만히 있기 (`NOTHING`) | 숨김 | 아무것도 하지 않음 |

## 패턴
- 매 턴 `NOTHING`만 반복한다.

---
id: MONSTER.ONE_HP_MONSTER
name: 대형 더미 인형
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/OneHpMonster.cs sha256:f5d5449dd8f57340346ac13502454a3e0ddedd25aabd3d20f6c05dcae3502907
---
# 대형 더미 인형 (ONE_HP_MONSTER)

**요약**: 테스트용 몬스터. 어떤 조우(encounter)에도 들어 있지 않아 정상 플레이에서는 나오지 않는다. 표시 이름은 `BIG_DUMMY`의 이름(대형 더미 인형)을 빌려 쓴다.

## 기본
- HP: 1, 고정
- 등장: 없음 (테스트 전용)

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 가만히 있기 (`NOTHING`) | 숨김 | 아무것도 하지 않음 |

## 패턴
- 매 턴 `NOTHING`만 반복한다.

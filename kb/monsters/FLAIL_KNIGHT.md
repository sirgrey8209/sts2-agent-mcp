---
id: MONSTER.FLAIL_KNIGHT
name: 철퇴 기사
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/FlailKnight.cs sha256:f5b7c0f3666b0bc1c7033682f96c6d56db0b7b03e249ec82d6c09b265a8a5afb
- MegaCrit.Sts2.Core.Models.Monsters/MysteriousKnight.cs sha256:580f6f13e750e948c2bba0cc540d39e99a2c225e80dc17797a6dec73178c9978
- MegaCrit.Sts2.Core.Models.Encounters/KnightsElite.cs sha256:1eed609a722c25e51764424e03707f9828d4ccd7bda8e7ffbd9256bd63ecb166
- MegaCrit.Sts2.Core.Models.Acts/Glory.cs sha256:d70575f5ac62dda4c1e80d424955c412eb00248e127caaa2751a2775db4b03d0
- MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine/RandomBranchState.cs sha256:a21f4174e001da5a0d34c23d07f52da61735618462e407ea1181ebd89469e9bb
---
# 철퇴 기사 (FLAIL_KNIGHT)

**요약**: 3막 엘리트 '기사 패거리'의 한 명. 들이받기로 시작해 이후 전투의 함성(힘 +3)·마구 휘두르기·들이받기를 무작위로 쓴다.

## 기본
- HP: 101 (A8+ 108)
- 등장: 3막(`GLORY`) 엘리트 `ENCOUNTER.KNIGHTS_ELITE` — 철퇴 기사(슬롯 first), 유령 기사(`SPECTRAL_KNIGHT`, second), 마법 기사(`MAGI_KNIGHT`, third)와 함께
- 전투 시작 시 효과 없음

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 전투의 함성 (`WAR_CHANT`) | 버프 | 자신 힘 +3 |
| 마구 휘두르기 (`FLAIL_MOVE`) | 공격 ×2 | 피해 9 (A9+ 10) × 2회 |
| 들이받기 (`RAM_MOVE`) | 공격 | 피해 15 (A9+ 17) |

## 패턴
- 시작: 들이받기.
- 이후 매 턴 무작위: 전투의 함성 / 마구 휘두르기 / 들이받기 가중치 1:1:1.
  - 전투의 함성: 직전 행동이 전투의 함성이면 선택 불가(연속 불가).
  - 마구 휘두르기, 들이받기: 직전 2번 연속으로 쓴 행동이면 선택 불가(최대 2연속).
  - 첫 들이받기도 연속 횟수에 포함된다(들이받기 → 들이받기 다음에는 들이받기 불가).

## 특수 규칙
- 전투의 함성의 힘 +3은 누적되며 마구 휘두르기의 두 타 모두에 적용된다.
- 같은 행동 구성을 쓰는 변형 몬스터 `MYSTERIOUS_KNIGHT`가 있다(시작 시 힘 6, 판금 6 추가). 별도 이벤트 전투용.

## 플레이 메모
- 전투가 길어지면 함성이 쌓여 마구 휘두르기가 크게 강해진다. 기사 셋 중 누구를 먼저 잡을지 정할 때 철퇴 기사의 누적 힘을 고려한다.

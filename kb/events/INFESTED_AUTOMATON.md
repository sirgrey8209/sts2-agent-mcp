---
id: EVENT.INFESTED_AUTOMATON
name: 감염된 자동인형
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/InfestedAutomaton.cs sha256:e6bebde83ee0efbd36afb0411ec75816efac07095848e3cb75f61bb0a77403b3
- MegaCrit.Sts2.Core.Models.Acts/Hive.cs sha256:085d95a7ea0418ada9e8544efdc97afc01c4bada90eca76fd65d3755c62c784e
---
# 감염된 자동인형 (INFESTED_AUTOMATON)

**요약**: 대가 없이 자기 캐릭터의 무작위 파워 카드 1장 또는 무작위 0코스트 카드 1장을 덱에 바로 받는다(고르는 화면 없음).

## 조건
- 2막(`Hive`) 이벤트. 별도 등장 조건 없음.
- 비공유 이벤트.

## 선택지
| 선택지 | 효과 |
|---|---|
| 살펴본다 (`STUDY`) | 자기 캐릭터 카드 풀의 파워 카드 중 무작위 1장을 덱에 추가. 이벤트 종료 |
| 핵을 만진다 (`TOUCH_CORE`) | 자기 캐릭터 카드 풀에서 기본 비용이 0인 카드(X 비용 제외) 중 무작위 1장을 덱에 추가. 이벤트 종료 |

## 결과 상세
- 두 선택지 모두 카드 보상 생성기(`CardFactory.CreateForReward`)로 1장만 만든다. 희귀도는 비전투 기본 확률(`ForNonCombatWithDefaultOdds`)로 정해진다.
- `TOUCH_CORE`는 카드 풀 수정 효과를 무시하는 플래그(`NoCardPoolModifications`)를 쓴다.
- 해당 조건의 카드가 하나도 없으면 아무것도 얻지 못하고 끝난다.
- 받은 카드는 선택 없이 덱에 바로 들어간다(거절 불가).

## 플레이 메모
- 어느 쪽이든 무작위 카드가 거절 불가로 덱에 들어온다. 덱을 얇게 유지하려는 경우 이 점을 감안한다.

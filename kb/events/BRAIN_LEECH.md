---
id: EVENT.BRAIN_LEECH
name: 뇌 거머리
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/BrainLeech.cs sha256:257da258d153d523bd76b20dd2f4930a413fd115ca6e20fac511bdd4248121f7
- MegaCrit.Sts2.Core.Models/ModelDb.cs sha256:7144c6e265fc4484e40860f4ec7cb288c677a63256675222010f66ab628d6e4f
- MegaCrit.Sts2.Core.Models/ActModel.cs sha256:55f3a43c7e055aff1e35985c0cd229f22a271698a82b0d0a24a3f01320dcab0b
---
# 뇌 거머리 (BRAIN_LEECH)

**요약**: 체력 소모 없이 내 캐릭터 카드 5장 중 1장을 덱에 넣거나, 체력 5를 잃고 무색 카드 보상(3장 중 택1)을 받는다.

## 조건
- 모든 막 공용 이벤트 목록(`ModelDb.AllSharedEvents`)에 속하지만, 현재 막 번호가 1막 또는 2막일 때만 등장한다(`CurrentActIndex < 2`). 3막에는 나오지 않는다.
- 공유 이벤트 아님.

## 선택지
| 선택지 | 효과 |
|---|---|
| 지식을 공유한다 (`SHARE_KNOWLEDGE`) | 내 캐릭터 카드 풀에서 무작위 5장을 만들어 보여주고, 그중 1장을 골라 덱에 추가. 이벤트 종료 |
| 거머리를 떼어낸다 (`RIP`) | 체력 5 잃음(방어 무시, 보정 없음) → 무색 카드 보상 1개(3장 중 1장 선택, 건너뛰기 가능). 이벤트 종료 |

## 결과 상세
- '지식을 공유한다'의 5장은 일반 비전투 카드 보상과 같은 희귀도 확률로 생성된다. 선택 화면은 취소할 수 없어 반드시 1장을 골라야 한다.
- '거머리를 떼어낸다'의 무색 카드 보상은 일반 비전투 희귀도 확률을 쓰되, 희귀도 보정·카드 풀 보정을 받지 않는다(`NoRarityModification`, `NoCardPoolModifications`). 보상 화면이므로 넘길 수 있다.

## 플레이 메모
- 덱에 카드를 넣고 싶지 않다면 '거머리를 떼어낸다'를 고르고 보상을 건너뛰는 것이 유일한 회피 방법이다(대신 체력 5 손실).

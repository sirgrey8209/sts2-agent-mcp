---
id: EVENT.DENSE_VEGETATION
name: 울창한 초목
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/DenseVegetation.cs sha256:30111c5079d078af1d77fab3c0f1a412e9f9dc2bf046ab101eb7ab77af07d9e9
- MegaCrit.Sts2.Core.Models.Encounters/DenseVegetationEventEncounter.cs sha256:275c12c3e1ca00a1cc4cf176518973ea5b967558f4a53d00acc7264116fa6bdf
---
# 울창한 초목 (DENSE_VEGETATION)

**요약**: 체력 8을 내고 골드 61~99를 얻거나, 휴식(휴식처와 같은 회복) 후 꿈틀벌레 4마리와 싸운다.

## 조건
- 공유 이벤트(멀티플레이에서 모두가 같은 이벤트를 본다).
- 멀티플레이에서는 체력이 8 이하인 플레이어가 있으면 등장하지 않는다. 싱글플레이는 제한 없음.

## 선택지
| 선택지 | 효과 |
|---|---|
| 묵묵히 나아간다 (`TRUDGE_ON`) | 체력 8 잃음(방어 무시, 보정 없음) → 골드 61~99 획득. 이벤트 종료 |
| 휴식한다 (`REST`) | 휴식처의 '휴식'과 같은 양을 회복 → 다음 페이지에서 '싸운다!'만 선택 가능 |
| └ 싸운다! (`FIGHT`) | 꿈틀벌레(`WRIGGLER`) 4마리와 전투. 이벤트 고유 추가 보상 없음, 전투 후 이벤트로 돌아오지 않음 |

## 결과 상세
- 골드 양은 이벤트가 열릴 때 61~99 사이에서 정해진다(이벤트 화면에 표시되는 값이 실제 값).
- 회복량은 휴식처 휴식 회복량 계산(`HealRestSiteOption.GetHealAmount`)을 그대로 쓴다. 회복을 늘리는 유물도 같이 적용된다.
- 전투의 꿈틀벌레들은 기절하지 않은 상태로 시작한다(일반 등장 때와 다름).

## 플레이 메모
- 휴식을 고르면 전투가 강제된다. 회복량과 꿈틀벌레 4마리 전투의 피해를 비교해서 고른다.

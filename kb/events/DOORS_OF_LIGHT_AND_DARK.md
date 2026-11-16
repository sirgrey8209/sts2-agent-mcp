---
id: EVENT.DOORS_OF_LIGHT_AND_DARK
name: 빛과 어둠의 문
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/DoorsOfLightAndDark.cs sha256:dd3d4ffd117c133f5ffb9970517bee466fbf64a62047c05a01cf47891c5bcbb1
- MegaCrit.Sts2.Core.Models.Acts/Underdocks.cs sha256:b5f91ba221afa5ca83ca92290f481c8bef88c28f1ff8272f780f2540700964fb
---
# 빛과 어둠의 문 (DOORS_OF_LIGHT_AND_DARK)

**요약**: 덱의 무작위 카드 2장을 강화하거나, 카드 1장을 골라 제거한다. 비용 없음.

## 조건
- 1막 지하 선착장(`Underdocks`) 이벤트.
- 별도 등장 조건 없음. 공유 이벤트 아님.

## 선택지
| 선택지 | 효과 |
|---|---|
| 빛의 문 (`LIGHT`) | 덱의 강화 가능한 카드 중 무작위 2장 강화. 이벤트 종료 |
| 어둠의 문 (`DARK`) | 덱에서 카드 1장을 골라 제거. 이벤트 종료 |

## 결과 상세
- 빛의 문: 강화 가능한 카드가 2장 미만이면 있는 만큼만 강화된다. 대상은 고를 수 없다.
- 어둠의 문: 일반 카드 제거 선택 화면(제거 가능한 카드만 대상).

## 플레이 메모
- 1막 초반에는 기본 타격·수비가 많아 무작위 강화가 기본 카드에 걸리기 쉽다. 기본 카드 1장 제거가 더 확실한 이득인 경우가 많다.

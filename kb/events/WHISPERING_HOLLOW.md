---
id: EVENT.WHISPERING_HOLLOW
name: 속삭이는 골짜기
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Events/WhisperingHollow.cs sha256:d854997fa77fba0ab818afceb0acbe4b4a22fed40e3e9ff92e94a8bb9eb6ffe9
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 속삭이는 골짜기 (WHISPERING_HOLLOW)

**요약**: 골드 26~44를 내고 무작위 포션 2개를 받거나, 체력 9를 내고 카드 1장을 무작위 변화시킨다.

## 조건
- 1막 `Overgrowth` 이벤트.
- 모든 플레이어의 골드가 44 이상이어야 등장한다(따라서 골드 선택지는 항상 지불 가능).
- 개별 이벤트(공유 아님).

## 선택지
| 선택지 | 효과 |
|---|---|
| 골드를 거래한다 (`GOLD`) | 골드 26~44 지불 → 포션 보상 2개. 이벤트 종료 |
| 나무를 끌어안는다 (`HUG`) | 덱에서 카드 1장 선택해 무작위 변화 → 체력 9 잃음(방어 무시, 보정 없음). 이벤트 종료 |

## 결과 상세
- 골드 양은 이벤트가 열릴 때 35 ± 9(26~44) 사이에서 정해진다(화면 표시값이 실제값).
- 포션 보상 2개는 일반 포션 보상과 같은 방식으로 무작위 생성된다.
- 변화는 정확히 1장(변화 가능한 카드가 있을 때).

## 플레이 메모
- 기본 타격·수비처럼 약한 카드를 바꾸고 싶으면 '끌어안는다', 체력을 아끼려면 골드로 포션 2개를 사는 쪽이다.

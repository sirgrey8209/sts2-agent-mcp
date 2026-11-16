---
id: EVENT.AROMA_OF_CHAOS
name: 혼돈의 향기
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/AromaOfChaos.cs sha256:bebbe67ed13d94fb5546c87d92262ace605c52f5c88ab5a6b01f970581fc2dfb
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 혼돈의 향기 (AROMA_OF_CHAOS)

**요약**: 덱의 카드 1장을 골라 변화시키거나, 1장을 골라 강화한다. 비용 없음.

## 조건
- 1막 과성장(`Overgrowth`) 이벤트.
- 별도 등장 조건 없음. 공유 이벤트 아님.

## 선택지
| 선택지 | 효과 |
|---|---|
| 향기에 몸을 맡긴다 (`LET_GO`) | 덱에서 카드 1장을 골라 무작위 카드로 변화. 이벤트 종료 |
| 정신을 붙잡는다 (`MAINTAIN_CONTROL`) | 덱에서 카드 1장을 골라 강화. 이벤트 종료 |

## 결과 상세
- 두 선택지 모두 체력·골드 비용이 없다.
- 선택 화면에서 카드를 고르지 않으면(대상이 없거나 선택 결과가 비면) 아무 일도 일어나지 않고 이벤트가 끝난다.
- 변화는 일반적인 이벤트 변화 규칙(`CardCmd.TransformToRandom`)을 따른다.

## 플레이 메모
- 기본 타격·수비를 변화시키거나, 핵심 카드를 확정 강화하는 선택. 둘 다 손해가 없으므로 덱 상황에 따라 고른다.

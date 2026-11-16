---
id: EVENT.MORPHIC_GROVE
name: 변성체의 숲
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/MorphicGrove.cs sha256:673f0f585cb8a2c164dde8ec68214b0e156c70d047fbbdd9470d73cb1d74918a
- MegaCrit.Sts2.Core.Models/CardModel.cs sha256:2e0c6eea66dfc1098c82386fa15838c6d376a83953a895c028b4f2ef60816919
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 변성체의 숲 (MORPHIC_GROVE)

**요약**: 가진 골드를 전부 잃고 카드 2장을 무작위로 변화시키거나, 최대 체력 +5를 얻는다.

## 조건
- 1막(`Overgrowth`) 이벤트.
- 모든 플레이어가 골드 100 이상이고, 덱에 변화 가능한 카드가 2장 이상 있어야 등장.
- 공유 이벤트(멀티플레이에서 투표로 고른 선택지가 모든 플레이어에게 적용).

## 선택지
| 선택지 | 효과 |
|---|---|
| 무리 (`GROUP`) | 가진 골드 전부 잃음(강탈 처리) → 덱에서 카드 2장을 골라 무작위 카드로 변화. 이벤트 종료 |
| 외톨이 (`LONER`) | 최대 체력 +5. 이벤트 종료 |

## 결과 상세
- 골드 손실은 '강탈'(`GoldLossType.Stolen`) 유형으로 처리된다.
- 변화는 이벤트 전용 난수로 결정된다(`CardCmd.TransformToRandom`).
- 등장 조건 때문에 이 이벤트를 만나면 최소 100골드를 잃게 된다.

## 플레이 메모
- 골드가 100을 크게 넘을수록 무리의 손실이 커진다. 상점에서 쓸 계획이 있으면 외톨이가 안전하다.
- 기본 카드(타격·수비) 2장을 변화시키는 가치와 현재 골드를 비교해서 고른다.

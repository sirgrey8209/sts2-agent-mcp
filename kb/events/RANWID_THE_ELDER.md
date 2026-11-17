---
id: EVENT.RANWID_THE_ELDER
name: 장로 랜위드
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Events/RanwidTheElder.cs sha256:424bda743112481d340538b3002072edd3084e65eb95b56a72a5092f903adfb8
- MegaCrit.Sts2.Core.Models/RelicModel.cs sha256:7fd67b3aa8c84e325aeb207e3375c98b1ad301ee68d64841523fdb01ee7c9eb6
- MegaCrit.Sts2.Core.Factories/RelicFactory.cs sha256:4fbe5ca84342cef115aabdd016b55be18bfe7b66cf0ff96e2e41c8a87da3f0dd
- MegaCrit.Sts2.Core.Entities.Relics/RelicRarity.cs sha256:e01d9cf4929e90dc484dd3ba26b66a64563e254e002a31ba105b5c200d1bf1d6
- MegaCrit.Sts2.Core.Models/ModelDb.cs sha256:2f7bdb25b700a1725266c720525542b660056bbc86f357852239b39b8fbf14f8
---
# 장로 랜위드 (RANWID_THE_ELDER)

**요약**: 무작위로 지정된 포션 1개, 골드 100, 무작위로 지정된 유물 1개 중 하나를 주고 무작위 유물을 받는다. 유물을 주면 유물 2개를 받는다.

## 조건
- 모든 막 공통 이벤트 풀(`ModelDb.AllSharedEvents`)에 속한다.
- 1막에서는 등장하지 않는다(`CurrentActIndex != 0`).
- 모든 플레이어가 아래를 모두 만족해야 등장:
  - 거래 가능한 유물 1개 이상
  - 골드 100 이상
  - 포션 1개 이상
- 비공유 이벤트.

## 선택지
| 선택지 | 효과 |
|---|---|
| (포션 이름)을(를) 준다 (`POTION`) | 지정된 포션을 버림 → 무작위 유물 1개 획득. 이벤트 종료 |
| 골드를 100 준다 (`GOLD`) | 골드 100 지불 → 무작위 유물 1개 획득. 이벤트 종료 |
| (유물 이름)을(를) 준다 (`RELIC`) | 지정된 유물을 잃음 → 무작위 유물 2개 획득. 이벤트 종료 |
| 잠김 (`POTION_LOCKED` / `RELIC_LOCKED`) | 줄 포션/유물이 없을 때 대신 표시. 선택 불가 |

## 결과 상세
- **주는 대상은 내가 고르지 않는다**: 이벤트가 열릴 때 가진 포션 중 1개, 거래 가능한 유물 중 1개가 무작위로 정해져 선택지 제목에 표시된다.
- **거래 가능한 유물**(`IsTradable`): 다 쓴 유물, 획득 시 효과가 있는 유물, 녹은 유물, 펫을 소환하는 유물, 시작·이벤트·고대 희귀도 유물은 제외.
- **이벤트 진행 중에는 포션을 사용하거나 버릴 수 없다**(`CanUseOrRemovePotions = false`). 이벤트가 끝나면 다시 가능.
- 받는 유물은 즉시 획득된다(보상 화면 아님). 희귀도는 일반 50%, 고급 33%, 희귀 17%이며, 유물 주머니 앞에서 꺼내 이후 다시 나오지 않는다.

## 플레이 메모
- 표시된 유물이 약한 일반 유물이면 `RELIC`이 가장 이득(유물 1개 → 2개)이다. 핵심 유물이 지정되었다면 포션이나 골드를 준다.

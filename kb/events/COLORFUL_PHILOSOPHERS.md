---
id: EVENT.COLORFUL_PHILOSOPHERS
name: 다채로운 철학자들
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Events/ColorfulPhilosophers.cs sha256:275cd45e34eea3221c5335ac5742918518f9c60ae5ec0fa262a27436a892b38f
- MegaCrit.Sts2.Core.Models.Acts/Hive.cs sha256:085d95a7ea0418ada9e8544efdc97afc01c4bada90eca76fd65d3755c62c784e
- MegaCrit.Sts2.Core.Timeline.Epochs/Event3Epoch.cs sha256:98687a86aa4123fc177bc4e8a286dbf3b8da35d1d39ccb964383f248a0bdb824
- MegaCrit.Sts2.Core.Models/ActModel.cs sha256:55f3a43c7e055aff1e35985c0cd229f22a271698a82b0d0a24a3f01320dcab0b
---
# 다채로운 철학자들 (COLORFUL_PHILOSOPHERS)

**요약**: 다른 캐릭터 색 하나를 골라 그 캐릭터의 일반·고급·희귀 카드 보상(각 3장 중 택1)을 하나씩, 총 3개 받는다.

## 조건
- 2막 군락(`Hive`) 이벤트. 타임라인 `Event3Epoch`가 공개되어야 이벤트 목록에 들어간다.
- 모든 플레이어가 자기 캐릭터 외에 다른 캐릭터 카드 풀을 1개 이상 해금했어야 등장한다.
- 공유 이벤트 아님.

## 선택지
선택지는 내 캐릭터를 제외하고 해금된 캐릭터 색으로 만들어진다. 후보가 3개를 넘으면 무작위로 지워 최대 3개만 남긴다(표시 순서는 아래 표 순서).

| 선택지 | 효과 |
|---|---|
| 분홍색 (`NECROBINDER`) | 네크로바인더 카드 풀에서 보상 3개 |
| 빨간색 (`IRONCLAD`) | 아이언클래드 카드 풀에서 보상 3개 |
| 주황색 (`REGENT`) | 리젠트 카드 풀에서 보상 3개 |
| 초록색 (`SILENT`) | 사일런트 카드 풀에서 보상 3개 |
| 파란색 (`DEFECT`) | 디펙트 카드 풀에서 보상 3개 |

## 결과 상세
- 고른 색의 카드 보상 3개가 보상 화면에 제시된다: 일반(Common)만 3장 중 택1, 고급(Uncommon)만 3장 중 택1, 희귀(Rare)만 3장 중 택1. 각 보상은 따로 고르거나 건너뛸 수 있다.
- 희귀도는 고정이며 희귀도 보정·카드 풀 보정을 받지 않는다.
- 보상을 받은 뒤 이벤트 종료(`DONE`). 거절 선택지는 없지만 보상은 모두 건너뛸 수 있다.
- 현지화에 '평등'(`EQUALITY`, 프리즘 조각) 선택지 문구가 있으나 현재 코드에서는 생성되지 않는다.

## 플레이 메모
- 희귀 카드 3장 중 1장이 확정으로 제시되므로 희귀 카드를 노릴 수 있다. 덱에 맞지 않는 보상은 비용 없이 건너뛰면 된다.

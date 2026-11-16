---
id: MONSTER.TWIG_SLIME_S
name: 가지 슬라임 (소)
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/TwigSlimeS.cs sha256:20df69868780ab61d8fd0e33aaeda150928ab98bc35c5276a2271563c4b46c0b
- MegaCrit.Sts2.Core.Models.Encounters/SlimesNormal.cs sha256:0c53efaaf3053551c55be100f69cfe3dfe5f32f6fac03f57db77d85c54db1c74
- MegaCrit.Sts2.Core.Models.Encounters/SlimesWeak.cs sha256:e28277926221eb06799873115b096e9b48a8672446754da75c5ae902beede7ec
- MegaCrit.Sts2.Core.Models.Encounters/SlitheringStranglerNormal.cs sha256:6303054f9e6b1809fe058179f5fe0a5a2b834255e300dd1e1b065c4ad37ccae3
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 가지 슬라임 (소) (TWIG_SLIME_S)

**요약**: 1막 슬라임 무리의 소형 슬라임. 매 턴 태클 4 (A9+ 5)만 한다. 특수 능력·분열 없음.

## 기본
- HP: 7~11 (A8+ 8~12)
- 등장: 1막(`Overgrowth`) 일반 전투
  - `ENCOUNTER.SLIMES_WEAK` (약한 전투): 가지 슬라임(소)와 나뭇잎 슬라임(소)가 각 1마리씩 항상 나오고, 중형 슬라임 1마리가 함께 나온다.
  - `ENCOUNTER.SLIMES_NORMAL`: 중형 2마리 + 가지(소)·나뭇잎(소) 각 1마리
  - `ENCOUNTER.SLITHERING_STRANGLER_NORMAL`: 동반 적이 '소형 슬라임 2'로 정해지면 소형 2마리를 각각 가지/나뭇잎 중 무작위(중복 가능)로 뽑는다.
- 시작 파워 없음

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 태클 (`TACKLE_MOVE`) | 공격 | 피해 4 (A9+ 5) |

## 패턴
- 태클만 반복. 무작위 요소 없음.

## 플레이 메모
- HP가 낮고 매 턴 확정 피해를 주므로, 광역 공격이나 남는 피해로 먼저 정리하기 좋다.

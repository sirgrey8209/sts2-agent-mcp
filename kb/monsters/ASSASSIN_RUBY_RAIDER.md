---
id: MONSTER.ASSASSIN_RUBY_RAIDER
name: 암살 습격자
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/AssassinRubyRaider.cs sha256:1ded70b4d0a355ab0d0b14e5cdafb3576df1c0504b88aaa837c66fa6744b6564
- MegaCrit.Sts2.Core.Models.Encounters/RubyRaidersNormal.cs sha256:ce3789a28fb2c5f2e6645473ef806f6bcce236b47fb476f3c092db1b262a5f19
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 암살 습격자 (ASSASSIN_RUBY_RAIDER)

**요약**: 루비 습격자 3인조에 섞여 나오는 1막 일반 적. 매 턴 결정타 한 가지만 쓴다.

## 기본
- HP: 18~23 (A8+ 19~24)
- 등장: 1막(`Overgrowth`) 일반 전투 `ENCOUNTER.RUBY_RAIDERS_NORMAL`. 도끼·암살·난폭한·크로스보우·추적 습격자 5종 중 서로 다른 3종이 무작위로 나온다.
  - 계정의 첫 런에서는 1막 6번째 일반 전투로 고정 배치된다.

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 결정타 (`KILLSHOT_MOVE`) | 공격 | 피해 10 (A9+ 11) |

## 패턴
- 매 턴 결정타. 무작위 요소 없음.

## 플레이 메모
- HP가 낮고 매 턴 10(11) 피해로 꾸준하므로, 3인조 중 먼저 잡기 좋은 대상이다.

---
id: MONSTER.AXE_RUBY_RAIDER
name: 도끼 습격자
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/AxeRubyRaider.cs sha256:3ba51c335698440e17d819ff2f25b4f0668c83b490e373198934b5814739f38d
- MegaCrit.Sts2.Core.Models.Encounters/RubyRaidersNormal.cs sha256:ce3789a28fb2c5f2e6645473ef806f6bcce236b47fb476f3c092db1b262a5f19
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 도끼 습격자 (AXE_RUBY_RAIDER)

**요약**: 루비 습격자 3인조에 섞여 나오는 1막 일반 적. 휘두르기(공격+방어) 2번 뒤 크게 휘두르기 1번을 반복한다.

## 기본
- HP: 20~22 (A8+ 21~23)
- 등장: 1막(`Overgrowth`) 일반 전투 `ENCOUNTER.RUBY_RAIDERS_NORMAL`. 도끼·암살·난폭한·크로스보우·추적 습격자 5종 중 서로 다른 3종이 무작위로 나온다.
  - 계정의 첫 런에서는 1막 6번째 일반 전투로 고정 배치된다.

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 휘두르기 (`SWING_1`, `SWING_2`) | 공격 + 방어 | 피해 5 (A9+ 6), 이후 자신 방어도 5 (A9+ 6) |
| 크게 휘두르기 (`BIG_SWING`) | 공격 | 피해 12 (A9+ 13) |

## 패턴
- 시작: 휘두르기.
- 고정 순환: 휘두르기 → 휘두르기 → 크게 휘두르기 → 휘두르기 → …
- 무작위 요소 없음.

## 특수 규칙
- 휘두르기의 방어도는 A8이 아니라 A9(치명적인 적)부터 6으로 오른다.

## 플레이 메모
- 3턴째, 6턴째 … 에 12(13) 피해가 온다. 휘두르기 턴에는 방어도 5(6)를 쌓으므로 그 턴의 피해는 일부 막힌다.

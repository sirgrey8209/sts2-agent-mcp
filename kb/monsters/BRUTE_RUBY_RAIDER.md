---
id: MONSTER.BRUTE_RUBY_RAIDER
name: 난폭한 습격자
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/BruteRubyRaider.cs sha256:3b99a34ef4bf1eb2a1acb2d7ce8adaf5c722e594ac6842a3c2d028cf2a74b4f9
- MegaCrit.Sts2.Core.Models.Encounters/RubyRaidersNormal.cs sha256:ce3789a28fb2c5f2e6645473ef806f6bcce236b47fb476f3c092db1b262a5f19
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 난폭한 습격자 (BRUTE_RUBY_RAIDER)

**요약**: 루비 습격자 3인조에 섞여 나오는 1막 일반 적. 때리기와 포효(힘 +3)를 번갈아 써서 오래 살려두면 공격이 계속 강해진다.

## 기본
- HP: 30~33 (A8+ 31~34)
- 등장: 1막(`Overgrowth`) 일반 전투 `ENCOUNTER.RUBY_RAIDERS_NORMAL`. 도끼·암살·난폭한·크로스보우·추적 습격자 5종 중 서로 다른 3종이 무작위로 나온다.
  - 계정의 첫 런에서는 1막 6번째 일반 전투로 고정 배치된다.

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 때리기 (`BEAT_MOVE`) | 공격 | 피해 7 (A9+ 8) |
| 포효 (`ROAR_MOVE`) | 버프 | 자신 힘 +3 |

## 패턴
- 시작: 때리기.
- 고정 순환: 때리기 → 포효 → 때리기 → 포효 → …
- 무작위 요소 없음.

## 플레이 메모
- 포효마다 힘이 3씩 쌓여 때리기가 7 → 10 → 13 …(A9+ 8 → 11 → 14 …)으로 커진다. 길게 끌수록 위험하다.

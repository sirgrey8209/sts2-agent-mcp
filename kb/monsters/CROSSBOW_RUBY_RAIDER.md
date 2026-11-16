---
id: MONSTER.CROSSBOW_RUBY_RAIDER
name: 크로스보우 습격자
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/CrossbowRubyRaider.cs sha256:1e8bc45eea94eb686aa9c29ad3cd7cea6c78ab9358a4da82132664d2df24c116
- MegaCrit.Sts2.Core.Models.Encounters/RubyRaidersNormal.cs sha256:ce3789a28fb2c5f2e6645473ef806f6bcce236b47fb476f3c092db1b262a5f19
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 크로스보우 습격자 (CROSSBOW_RUBY_RAIDER)

**요약**: 1막(`Overgrowth`) 루비 습격자 무리의 일원. 재장전(방어도 3)과 발사!(큰 단일 공격)를 번갈아 쓴다. 첫 턴은 재장전이다.

## 기본
- HP: 18~21 (A8+ 19~22)
- 등장: 1막 `Overgrowth` 일반 전투 `ENCOUNTER.RUBY_RAIDERS_NORMAL`
  - 습격자 5종(`AXE_RUBY_RAIDER`, `ASSASSIN_RUBY_RAIDER`, `BRUTE_RUBY_RAIDER`, `CROSSBOW_RUBY_RAIDER`, `TRACKER_RUBY_RAIDER`) 중 서로 다른 3종이 무작위로 나온다. 같은 종은 최대 1마리.
- 전투 시작 시 특수 효과 없음

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 재장전 (`RELOAD_MOVE`) | 방어 | 자신 방어도 3 |
| 발사! (`FIRE_MOVE`) | 공격 | 피해 14 (A9+ 16) |

## 패턴
- 시작: 재장전.
- 고정 순환: 재장전 → 발사! → 재장전 → …
- 무작위 요소 없음.

## 플레이 메모
- 홀수 턴(1, 3, 5…)은 재장전, 짝수 턴은 발사!다. HP가 낮으므로 발사! 직전 턴(재장전 턴)에 처치하면 14(A9+ 16) 피해를 통째로 피할 수 있다.

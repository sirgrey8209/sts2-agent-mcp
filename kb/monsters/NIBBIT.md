---
id: MONSTER.NIBBIT
name: 깨작이
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/Nibbit.cs sha256:5f027dafe55d5be0d4001188ff010cbb087b3c0c9f5b069cd03713b33ea02912
- MegaCrit.Sts2.Core.Models.Encounters/NibbitsNormal.cs sha256:a5b84e1df56a5e85785ab654b899777db8baee453481de4faf140f7420f2edd9
- MegaCrit.Sts2.Core.Models.Encounters/NibbitsWeak.cs sha256:fa2354ed95db89c0d43cc2307cf2d6570a0a8c8028d9fd861ca432c52a0727b4
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 깨작이 (NIBBIT)

**요약**: 1막 일반 전투 몬스터. 박치기 → 베기(공격+방어) → 쉭쉭대기(힘 증가) 3행동을 고정 순환한다. 혼자 나오면 박치기, 둘이 나오면 앞쪽은 베기·뒤쪽은 쉭쉭대기로 시작한다.

## 기본
- HP: 42~46 (A8+ 44~48)
- 등장: 1막 `Overgrowth`
  - 약한 전투 `ENCOUNTER.NIBBITS_WEAK`: 1마리 (`IsAlone`)
  - 일반 전투 `ENCOUNTER.NIBBITS_NORMAL`: 2마리 (`front` 자리 1마리는 `IsFront`, `back` 자리 1마리)
- 전투 시작 시 효과 없음

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 박치기 (`BUTT_MOVE`) | 공격 | 피해 12 (A9+ 13) |
| 베기 (`SLICE_MOVE`) | 공격 + 방어 | 피해 6 (A9+ 7), 이후 자신 방어도 5 (A8+ 6) |
| 쉭쉭대기 (`HISS_MOVE`) | 버프 | 자신 힘 +2 (A9+ +3) |

`SLICE_MOVE`는 이동 ID와 같은 이름 키가 없고 도감에서도 숨겨진다(로컬라이즈에는 `HESITANT_SLICE` "속전속결"이 있으나 코드상 연결은 불명확).

## 패턴
- 시작 행동:
  - 혼자(`NIBBITS_WEAK`): 박치기.
  - 둘(`NIBBITS_NORMAL`): 앞쪽(`front`)은 베기, 뒤쪽(`back`)은 쉭쉭대기.
- 고정 순환: 박치기 → 베기 → 쉭쉭대기 → 박치기 → …
- 둘이 나올 때 두 마리의 순환이 한 칸 어긋나 있어, 1턴 베기+쉭쉭대기, 2턴 쉭쉭대기+박치기, 3턴 박치기+베기 순으로 반복된다.
- 무작위 요소 없음.

## 특수 규칙
- 쉭쉭대기의 힘은 누적된다. 3턴마다 2(A9+ 3)씩 강해진다.

## 플레이 메모
- 혼자 나오면 첫 턴이 가장 센 박치기(12/13)이므로 첫 턴 방어를 챙긴다.
- 둘이 나오면 첫 턴 공격은 베기 6(7) 하나뿐이라 공격에 집중하기 좋다.

---
id: MONSTER.ARCHITECT
name: 아키텍트
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/Architect.cs sha256:04d50610fd0f35299f122a7b3b5c622dcd4e6d4fc3533687a8f239f2a4864871
- MegaCrit.Sts2.Core.Models.Encounters/TheArchitectEventEncounter.cs sha256:8cf57b6ee54884b1e7e4143d2d0aa09d03865b2d5ad8acc37d0d304f19e94d14
- MegaCrit.Sts2.Core.Models.Events/TheArchitect.cs sha256:bcd6bbbd0041c895b80b4381cc4bfcfe64692e8e26bdf37938df952cb2a131ce
- MegaCrit.Sts2.Core.Runs/RunManager.cs sha256:db8f6c79a7ae1d1932028af1b9f4d2b01092e6294251f4eab40f7e67a03ee8bd
---
# 아키텍트 (ARCHITECT)

**요약**: 실제로 싸우는 적이 아니다. 마지막 막의 보스를 이기고 다음 막으로 넘어가면 들어가는 승리 장면 이벤트(`THE_ARCHITECT`)에 서 있는 연출용 몬스터다. 대화를 넘기고 '진행'을 고르면 런이 승리로 끝난다.

## 기본
- HP: 9999
- 등장: 이벤트 `THE_ARCHITECT`의 전투 배치(`ENCOUNTER.THE_ARCHITECT_EVENT_ENCOUNTER`). 마지막 막 이후 `RunManager`가 이 이벤트 방으로 보낸다.

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 가만히 있기 (`NOTHING`) | 숨김 | 아무것도 하지 않음 |

## 특수 규칙
- 이벤트 화면에서는 캐릭터별 대사가 이어지고, 마지막에 '진행' 선택지로 런이 승리 처리된다.
- 대사 시작·끝에 플레이어가 아키텍트를 때리거나 아키텍트가 플레이어를 공격하는 연출이 나온다. 표시되는 피해 숫자는 런 점수를 여러 타로 나눈 것이며 실제 피해·HP 변화는 없다.

---
id: MONSTER.BIG_DUMMY
name: 대형 더미 인형
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/BigDummy.cs sha256:1917319a8eeed429b7f413acde7feda8b36d62d39e25c4508c8944454e3cac8d
- MegaCrit.Sts2.Core.Models.Encounters.Mocks/MockMonsterEncounter.cs sha256:555ce536dc7965478787c27c833a05751d63678b1388aa08f2a952bd8f8c5e7b
- MegaCrit.Sts2.Core.Models.Monsters.Mocks/MockAttackAndSummonMinionMonster.cs sha256:992ad2536c805265bfce67e17d1c532a55b87733ed06cf0239a0becee777aff7
---
# 대형 더미 인형 (BIG_DUMMY)

**요약**: 테스트용 몬스터. 실제 막의 전투에는 나오지 않고, 테스트용 가짜 전투(`Mock*Encounter`)와 테스트용 몬스터의 소환에서만 쓰인다. 외형은 디펙트 모델을 빌려 쓴다.

## 기본
- HP: 9999
- 등장: 테스트 전용(`MockMonsterEncounter`, `MockEliteEncounter`, `MockBossEncounter`, `MockNoRewardsEncounter`, `MockTwoMonsterEncounter` 등)

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 가만히 있기 (`NOTHING`) | 숨김 | 아무것도 하지 않음 |

---
id: MONSTER.FUZZY_WURM_CRAWLER
name: 복슬지렁이
game_version: v0.111.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/FuzzyWurmCrawler.cs sha256:1b985c2625c86375a377bb8f124e339cba9af66ce4c6b7d0d5f4548662ce8bb8
- MegaCrit.Sts2.Core.Models.Encounters/FuzzyWurmCrawlerWeak.cs sha256:306bae08ec4ffc05936242ab7749bfe24e8facf79cbdbe4e3d4bc84d9a25bd0e
- MegaCrit.Sts2.Core.Models.Encounters/OvergrowthCrawlers.cs sha256:19522315b75732f6b94cc09b63c2916edf626c226ee8efdb82be7af33ca8ff5f
- MegaCrit.Sts2.Core.Models.Acts/Overgrowth.cs sha256:61ff9d71f9b17f2f6a2f7f51f64bbfaa6752ec8cf7e75b4d291f2b0f89dc232b
---
# 복슬지렁이 (FUZZY_WURM_CRAWLER)

**요약**: 1막 일반 적. 산성 끈적이 → 빨아들이기(힘 +7) → 산성 끈적이의 3턴 고정 순환을 돌아, 3턴마다 공격이 7씩 강해진다.

## 기본
- HP: 55~57 (A8+ 58~59)
- 등장: 1막(`OVERGROWTH`) 일반 전투
  - `ENCOUNTER.FUZZY_WURM_CRAWLER_WEAK` (막 초반 약한 전투): 단독
  - `ENCOUNTER.OVERGROWTH_CRAWLERS`: 압축벌레(`SHRINKER_BEETLE`)와 함께
- 전투 시작 시 효과 없음

## 행동
| 행동 | 의도 | 효과 |
|---|---|---|
| 산성 끈적이 (`FIRST_ACID_GOOP`, `ACID_GOOP`) | 공격 | 피해 4 (A9+ 6). 두 id는 효과가 같다 |
| 빨아들이기 (`INHALE`) | 버프 | 자신 힘 +7 |

## 패턴
- 고정 순환: 산성 끈적이(`FIRST_ACID_GOOP`) → 빨아들이기 → 산성 끈적이(`ACID_GOOP`) → 산성 끈적이(`FIRST_ACID_GOOP`) → 빨아들이기 → …
- 즉 공격, 버프, 공격, 공격, 버프, 공격, 공격, … 순서.
- 무작위 요소 없음.

## 특수 규칙
- 빨아들이기의 힘 +7은 누적된다. 공격 피해 순서(다른 힘 변화가 없을 때): 4, (버프), 11, 11, (버프), 18, 18, … (A9+ 6, (버프), 13, 13, (버프), 20, 20, …).

## 플레이 메모
- 빨아들이기 턴은 공격이 없으므로 공격 턴으로 쓴다.
- 오래 끌수록 3턴마다 피해가 7씩 오르므로, 두 번째 빨아들이기 전에 처치하는 것을 목표로 한다.

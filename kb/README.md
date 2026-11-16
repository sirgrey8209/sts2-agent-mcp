# Knowledge base

Game behaviour that is **not visible in game text**, summarized from the decompiled game source of a specific game
version. The mod installs this folder next to its DLL (`STS2AgentMcp-kb/`) and `info` appends the matching document
(e.g. `info 라가불린 대모`, `info e1` during a fight, `info boss`).

Documents describe behaviour in prose and tables. They must not contain decompiled code.

## Layout

```
kb/
  monsters/<ENTRY>.md   one per monster (ENTRY = model id entry, e.g. LAGAVULIN_MATRIARCH)
  events/<ENTRY>.md     one per event (e.g. DENSE_VEGETATION)
```

## Front matter

```
---
id: MONSTER.LAGAVULIN_MATRIARCH
name: 라가불린 대모
game_version: v0.107.1
sources:
- MegaCrit.Sts2.Core.Models.Monsters/LagavulinMatriarch.cs sha256:<hash>
- MegaCrit.Sts2.Core.Models.Powers/AsleepPower.cs sha256:<hash>
---
```

`sources` lists every decompiled file the document relies on (paths relative to the decompiled version folder).
Writers list the paths; `tools/kb-stamp.ps1` fills in the hashes and `game_version`.

## Writing rules

- Korean prose; keep game ids in English (`SLASH_MOVE`, `StrengthPower`) next to the Korean names players see.
  Korean names come from the localization dump (`loc-monsters.json`, `loc-events.json`).
- Numbers exactly as in code. Ascension variants as `19 (A9+ 21)`. Ascension levels: 8 = ToughEnemies
  (HP and block of enemies), 9 = DeadlyEnemies (enemy damage), 10 = DoubleBoss. Others rarely matter for monsters.
- State only what the code does. Inferences go in a final "플레이 메모" section and must follow directly from the code.
- When a mechanic lives in another class (a power, a status card, an encounter), read it, describe it, and add it to `sources`.
- Monster sections: 요약 / 기본 / 행동 / 패턴 / 특수 규칙 / 플레이 메모. Event sections: 요약 / 조건 / 선택지 / 결과 상세 / 플레이 메모.
  Omit a section when it would be empty.

## Updating for a new game version

1. Decompile the new `sts2.dll` into the local decompiled repository (`C:\WorkSpace\sts2-decompiled\<version>`) and commit it.
2. Export the localization tables with the `export_loc` tool and store them next to it.
3. `tools/kb-check.ps1 -Decompiled <new version folder>` lists documents whose sources changed or disappeared, and
   monsters/events that have no document yet.
4. Rewrite those documents from the new source (the old→new `git diff` of their sources shows what changed), then run
   `tools/kb-stamp.ps1` to record the new hashes and version.

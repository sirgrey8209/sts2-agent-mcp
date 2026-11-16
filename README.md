# STS2 Agent MCP

A Slay the Spire 2 mod that runs an MCP server **inside the game**, so an AI agent can play single-player runs.

- **One call per decision.** `act(["play c3 @e1", "play c7", "end"])` plays the cards, ends the turn, waits through the
  enemy turn and returns what happened plus the next hand. No polling.
- **Short, computed state.** Card text and numbers are the game's own (Strength, Vulnerable, Weak, Frail... already
  applied, per target when it matters). Static text (relics, keywords) is on demand via `info`.
- **No bridge process.** The mod speaks MCP over HTTP directly (`http://127.0.0.1:15527/mcp`).
- **Resilient to game updates.** Screens without a dedicated handler stay playable through a generic button handler;
  game-internal calls are isolated so a broken API disables one feature instead of the whole mod.

Tested against Slay the Spire 2 **v0.107.1**. Design notes: [DESIGN.md](DESIGN.md) (Korean).

## Build and install

Requires the .NET 9 SDK and the game.

```powershell
.\build.ps1 -Install          # builds out\STS2AgentMcp.dll and copies it (+ manifest) to <game>\mods\
# custom install path:  .\build.ps1 -GameDir "D:\...\Slay the Spire 2" -Install
```

The DLL is locked while the game runs: quit the game, install, start it again, and enable the mod in the game's mod
settings if asked. On first launch the mod writes `<game>\mods\STS2AgentMcp.conf`:

```json
{ "port": 15527, "token": "<random>", "fast_mode": "instant" }
```

- `token`: bearer token required on every request.
- `fast_mode`: `instant` (default) switches the game's animation speed to Instant after boot; `keep` leaves your setting.
- `capture_dir` (optional): where screenshots and rendered images are written. Default `%TEMP%\sts2-agent-mcp`; the newest 60 are kept.

## Connect an MCP client

Claude Code (`.mcp.json`):

```json
{
  "mcpServers": {
    "sts2": {
      "type": "http",
      "url": "http://127.0.0.1:15527/mcp",
      "headers": { "Authorization": "Bearer <token from STS2AgentMcp.conf>" }
    }
  }
}
```

The server only exists while the game runs: start the game first, or reconnect with `/mcp`.

## Tools

| Tool | Purpose |
|---|---|
| `state(detail?)` | Current screen, run status and the actions the screen accepts. `detail: "full"` adds deck, pile contents, relic and potion text, and run-level actions. |
| `act(actions[])` | Runs actions in order, waiting for the game to settle after each. Returns the events plus the resulting screen. Stops early on an error, a screen change, combat end, or an unexpected choice prompt. |
| `info(ids[])` | Cards, relics, potions, powers, monsters and characters by id (`CARD.BASH`, `BASH`) or displayed name. |
| `screenshot(attach?, max_width?)` | Saves the game window as JPEG and returns the file path. |
| `render_cards(source? \| cards?, columns?, attach?)` | Draws cards with the game's own card visuals into one PNG. `source`: hand, draw, discard, exhaust, deck, choice, screen (card reward / shop / pending choice). `cards`: combat ids (`c3`) or card ids/names. Captions show the id or index `act` uses. |
| `render_map(attach?)` | Draws the whole act map as one tall PNG with the game's map icons: path taken (red), current position, next choices (green, labelled with the `choose` index). |

### Playing through chat

The image tools are for showing the game to a person while an agent plays alongside them. They write files to
`%TEMP%\sts2-agent-mcp` (or `capture_dir` in the config) and return the path, so the agent can forward the file to the
user's chat. With `attach: true` the image is also returned to the agent itself (costs tokens).

### Actions (each screen lists the ones it accepts)

```
combat      play c12 @e1 · end · wait          (card ids are stable for the whole combat; target optional with one enemy)
any in-run  potion p0 [@e1] · discard_potion p0
choices     choose 2 · choose 0 3 · skip · cancel   (cancel: rest-site smith/cook, shop card removal)
rewards     choose <i> · proceed · card reward: choose <i> · skip · alt <i>
map         choose <i>
event       choose <i> · proceed
rest site   choose <i> · proceed
shop        open · buy <i> · close · leave        (buy/leave open or close the inventory as needed)
treasure    open · choose 0 · skip|proceed
crystal     tool big|small · reveal <x> <y> · proceed
bundle      choose <i>
menus       choose <i|key> · new_run IRONCLAD asc=0 [seed=S] [mode=standard] · embark [CHARACTER] [asc=N] [seed=S]
timeline    reveal_epochs (reveals obtained epochs = applies their unlocks to the profile, then back to menu) · back
run         quit_to_menu (save & quit) · abandon_run      (listed with state detail=full)
fallback    click <i>                              (screens without a dedicated handler)
```

Card choices caused by effects (exhaust a card, upgrade from an event or the smith, card removal, ...) appear as a
`choice` screen. If you expect one, chain the answer: `["play c9", "choose 0"]`.

The game autosaves on room entry (and after combat wins / events). Quitting resumes from that point, so anything done
inside a room since then (e.g. shop purchases) is rolled back.

### Example

```
> play c4 @e1
  play 타격 → e1 복슬지렁이
  e1 복슬지렁이 hit 6 → HP 50/56
> end
  me hit 4 by e1 → HP 66/80
  e1 복슬지렁이 acts: 공격 (FIRST_ACID_GOOP)
  draw 타격, 수비, 타격, 수비, 수비

Act 1 · floor 3 · A7 | 아이언클래드 HP 66/80 · gold 112
relics: 불타는 혈액, 약병 홀스터
potions 1/3: p1 에너지 포션
== combat ==
round 2 · your turn
me 아이언클래드 HP 66/80 · energy 3/3
hand:
  c5 타격 [1E] Attack @enemy — 피해를 6 줍니다.
  ...
enemies:
  e1 복슬지렁이 HP 32/56 · intent 강화
actions: play <card> [@<target>] · potion <slot> [@<target>] · discard_potion <slot> · end
```

Game text is shown in the game's language (Korean above); structure words are short English tokens.

## Development

```powershell
.\tools\redeploy.ps1                       # build → quit game → install → relaunch via Steam → wait until ready
.\tools\mcp.ps1 state                      # call the endpoint without an MCP client
.\tools\mcp.ps1 act "play c3 @e1" "end"
.\tools\mcp.ps1 info BASH
.\tools\autofight.ps1                      # finish the current fight with a naive rule (test helper, not an agent)
```

Game log (mod lines start with `[STS2 Agent MCP]`): `%APPDATA%\SlayTheSpire2\logs\godot.log`.

## Security

- Listens on localhost only and requires the bearer token.
- Requests with an `Origin` header (i.e. from a web page) are rejected; no CORS headers are sent.
- No endpoint deletes or switches save profiles.

## Credits

This project started from studying [STS2MCP](https://github.com/Gennadiyev/STS2MCP) by Yikun Ji (Kunologist) and
contributors. It is a separate implementation with a different architecture, but it reuses knowledge and a few small
code fragments from that project. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

Slay the Spire 2 is a game by Mega Crit. This is an unofficial fan mod.

## License

MIT — see [LICENSE](LICENSE).

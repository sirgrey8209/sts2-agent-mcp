# STS2 Agent MCP

Slay the Spire 2 게임 안에서 MCP 서버를 실행하는 모드다. AI 에이전트가 이 서버에 접속해 싱글플레이 런을 진행한다. 사람과 채팅하며 함께 플레이할 수도 있다.

- **판단 한 번을 호출 한 번으로 처리한다.** `act(["play c3 @e1", "play c7", "end"])`를 한 번 호출하면 카드 두 장을 내고 턴을 끝낸다. 서버는 적 턴이 끝날 때까지 기다렸다가, 그동안 일어난 일과 다음 손패를 함께 돌려준다. 상태를 반복해서 조회할 필요가 없다.
- **게임이 계산한 수치를 보여 준다.** 카드 설명과 숫자는 게임의 문구 그대로이고, 힘, 취약, 약화, 손상 같은 보정이 이미 적용돼 있다. 대상에 따라 값이 달라지면 대상별로 보여 준다. 유물 설명이나 키워드처럼 바뀌지 않는 문구는 `info`로 필요할 때만 조회한다.
- **중계 프로세스가 없다.** 모드가 HTTP로 MCP를 직접 제공한다(`http://127.0.0.1:15527/mcp`).
- **채팅으로 함께 플레이할 때 쓰는 이미지 도구가 있다.** 게임 화면 스크린샷, 게임의 카드 그림으로 그린 카드 목록, 한 막 전체의 지도를 이미지 파일로 만든다.
- **몬스터와 이벤트의 내부 규칙을 문서로 제공한다.** 게임 문구에 나오지 않는 정확한 수치, 승천별 변화, 행동 패턴, 이벤트 결과를 `info`로 조회할 수 있다.
- **게임 업데이트에 대비했다.** 전용 처리기가 없는 화면은 범용 버튼 처리기로 진행할 수 있다. 게임 내부 API 호출은 기능별로 격리했다. 따라서 업데이트로 API 하나가 바뀌어도 그 기능만 비활성화되고, 모드 전체는 계속 동작한다.

Slay the Spire 2 버전 **v0.111.1**에서 시험했다. 설계 문서는 [DESIGN.md](DESIGN.md)에 있다.

## 빌드와 설치

.NET 9 SDK와 게임이 필요하다.

```powershell
.\build.ps1 -Install          # out\STS2AgentMcp.dll을 빌드하고 <게임>\mods\에 DLL, 매니페스트, kb 문서를 복사한다
# 게임 경로 지정:  .\build.ps1 -GameDir "D:\...\Slay the Spire 2" -Install
```

게임 경로를 지정하지 않으면 환경 변수 `STS2_GAME_DIR`를 쓰고, 그것도 없으면 `D:\SteamLibrary\steamapps\common\Slay the Spire 2`를 사용한다.

게임이 실행 중이면 DLL 파일을 덮어쓸 수 없다. 게임을 끄고 설치한 뒤 다시 실행한다. 게임이 모드 사용 여부를 물으면 모드 설정에서 켠다. 처음 실행하면 모드가 `<게임>\mods\STS2AgentMcp.conf`를 만든다. 이 파일의 내용은 다음과 같다.

```json
{ "port": 15527, "token": "<무작위 값>", "fast_mode": "instant" }
```

- `token`: 모든 요청에 필요한 bearer 토큰이다.
- `fast_mode`: `instant`(기본값)이면 게임이 시작된 뒤 모드가 애니메이션 속도를 '즉시'로 바꾼다. `keep`이면 사용자 설정을 그대로 둔다.
- `capture_dir`(선택): 스크린샷과 이미지 파일을 저장할 폴더다. 기본값은 `%TEMP%\sts2-agent-mcp`이고, 최근 파일 60개만 보관한다.
- `dev_tools`(선택, 기본값 `false`): 개발과 시험용 도구 `dev_console`을 켠다. 게임의 개발자 콘솔 명령(방 이동, 이벤트 이동, 카드와 유물 추가 등 치트)을 실행하는 도구라서 평소 플레이에서는 켜지 않는다.
- `chat_panel`, `chat_width`, `chat_agent`, `chat_auto_connect`, `claude_path`(선택, 개발 중): 게임 안 채팅창 설정이다. 아래 "게임 안 채팅창"을 참고한다.

## MCP 클라이언트 연결

Claude Code에서는 `.mcp.json`에 다음과 같이 적는다.

```json
{
  "mcpServers": {
    "sts2": {
      "type": "http",
      "url": "http://127.0.0.1:15527/mcp",
      "headers": { "Authorization": "Bearer <STS2AgentMcp.conf의 token>" }
    }
  }
}
```

서버는 게임이 실행 중일 때만 동작한다. 게임을 먼저 켠다. 클라이언트를 먼저 시작했다면, 게임을 켠 뒤 `/mcp`로 다시 연결한다.

## 도구

| 도구 | 하는 일 |
|---|---|
| `state(detail?)` | 현재 화면, 런 상태, 그 화면에서 할 수 있는 행동을 보여 준다. `detail: "full"`이면 덱, 카드 더미, 유물 설명, 포션 설명, 런 전체에서 할 수 있는 행동까지 보여 준다. |
| `act(actions[])` | 행동을 순서대로 실행하고, 행동마다 게임이 다음 입력을 받을 수 있을 때까지 기다린다. 일어난 일과 마지막 화면을 돌려준다. 다음 경우에는 남은 행동을 실행하지 않고 멈춘다. 오류가 났을 때, 화면이 바뀔 때, 전투가 끝날 때, 예상하지 못한 선택 요청이 나올 때다. |
| `info(ids[])` | 카드, 유물, 포션, 파워, 몬스터, 조우, 이벤트, 캐릭터를 id(`CARD.BASH`, `BASH`)나 화면에 표시된 이름으로 조회한다. `e1`은 지금 전투 중인 적, `boss`는 이번 막의 보스다. 카드는 강화한 뒤의 비용과 설명도 함께 보여 준다. 몬스터는 행동 순환표를 보여 준다. 몬스터와 이벤트는 지식 베이스 문서도 함께 보여 준다. |
| `search(query?, kind?, pool?, type?, rarity?, cost?, keyword?, upgraded?, limit?)` | 카드, 유물, 포션을 검색해 한 줄에 하나씩 보여 준다. 각 줄에는 id, 이름, 비용, 종류, 희귀도, 소속, 설명이 나온다. `query`는 이름과 설명, id에서 찾고, 단어를 여러 개 주면 모두 들어 있는 항목만 찾는다. 나머지 인자는 검색 조건이다. `pool`은 캐릭터나 소속(defect, 디펙트, colorless, curse, shared 등), `type`은 카드 종류, `rarity`는 희귀도, `cost`는 카드 비용, `keyword`는 카드 키워드(Exhaust, Ethereal 등 게임 내부 이름)다. `upgraded: true`이면 강화한 카드를 기준으로 찾고 보여 준다. 결과의 카드 id를 `render_cards`에 넣으면 카드 그림으로 볼 수 있다. |
| `screenshot(attach?, max_width?)` | 게임 화면을 JPEG로 저장하고 파일 경로를 돌려준다. |
| `render_cards(source? \| cards?, columns?, attach?)` | 게임의 카드 그림으로 카드 여러 장을 PNG 한 장에 그린다. `source`는 hand, draw, discard, exhaust, deck, choice, screen(카드 보상, 상점, 진행 중인 선택) 중 하나다. `cards`에는 전투 카드 id(`c3`)나 카드 id, 카드 이름을 넣는다. 카드 아래에는 `act`에서 쓰는 id나 번호가 적힌다. |
| `render_map(coords?, paths?, marks?, notes?, attach?, max_width?)` | 한 막의 지도 전체를 게임의 지도 아이콘으로 그려, 세로로 긴 PNG 한 장을 만든다. 지나온 경로는 빨간색, 다음에 고를 수 있는 칸은 초록색이고, 초록색 칸에는 `choose`에 쓸 번호가 적힌다. 현재 위치도 표시한다. 다음 내용을 덧그릴 수 있다. `coords: true`이면 칸마다 좌표(`r7c3`, 층과 열)를 적는다. `paths`는 칸을 순서대로 이은 경로이고, 색과 이름을 줄 수 있다. 이름은 지도 아래 범례에 나오고, 같은 구간을 지나는 경로는 겹치지 않게 나란히 그린다. `marks`는 칸을 원으로 강조하고 옆에 짧은 글을 쓴다. `notes`는 지도 아래에 적는 메모다. 칸은 `state` 지도 화면과 같은 `r7c3` 형식이나 `current`, `boss`로 지정한다. 색은 `blue` 같은 이름이나 `#3366cc` 형식이고, 생략하면 정해진 순서대로 고른다. |
| `export_loc(tables?)` | 지식 베이스를 관리할 때 쓴다. 게임의 현지화 문구 표를 JSON 파일로 저장한다. 플레이에는 필요 없다. |

### 채팅으로 함께 플레이하기

이미지 도구는 에이전트가 사람과 함께 플레이할 때 게임 화면을 보여 주는 용도다. 도구는 이미지를 `%TEMP%\sts2-agent-mcp`(설정의 `capture_dir`)에 저장하고 경로만 돌려준다. 에이전트는 이 파일을 사용자의 채팅으로 보낸다. `attach: true`를 주면 이미지가 에이전트에게도 전달된다. 이때는 모델 토큰이 소모된다.

### 게임 안 채팅창 (개발 중)

설정에 `"chat_panel": true`를 넣으면 게임 화면 왼쪽에 채팅창이 생기고, 게임은 나머지 영역에 전체 높이로 그려진다. 게임은 그 영역의 비율에 맞춰 UI를 스스로 다시 배치한다. 채팅창 오른쪽 경계선을 끌어서 폭을 바꿀 수 있다.

채팅창에 메시지를 보내면 모드가 Claude Code(`claude`)를 직접 실행한다. Claude Code는 이 모드의 MCP 도구(state, act, info, search)로 게임을 보고 답한다. 말풍선은 게임의 대화 말풍선과 글꼴을 쓴다. 로그인은 이 게임 전용 폴더(`%LOCALAPPDATA%\sts2-agent-mcp\claude-config`)에 따로 한다. 로그인이 없으면 채팅창에 로그인 카드가 뜨고, 버튼을 누르면 Claude Code의 로그인(설치되어 있지 않으면 공식 설치 후 로그인)이 별도 창에서 열린다. Claude 구독(Pro 이상)이 우선이고, 없으면 사용자가 직접 설정한 `ANTHROPIC_API_KEY` 환경변수를 쓴다. 실행 파일을 찾지 못하면 `claude_path`에 경로를 적는다. 구현 방식과 작업 계획은 [docs/in-game-chat.md](docs/in-game-chat.md)에 있다.

### 행동 (화면별로 받는 행동)

```
combat      play c12 @e1 · end · wait          (카드 id는 전투 내내 같다. 적이 하나면 대상 생략 가능)
any in-run  potion p0 [@e1] · discard_potion p0
choices     choose 2 · choose 0 3 · skip · cancel   (cancel: 휴식처 재련과 요리, 상점 카드 제거)
rewards     choose <i> · proceed · card reward: choose <i> · skip · alt <i>
map         choose <i>
event       choose <i> · proceed
rest site   choose <i> · proceed
shop        open · buy <i> · close · leave        (buy와 leave는 필요하면 상점 목록을 알아서 열고 닫는다)
treasure    open · choose 0 · skip|proceed
crystal     tool big|small · reveal <x> <y> · proceed
bundle      choose <i>
menus       choose <i|key> · new_run IRONCLAD asc=0 [seed=S] [mode=standard] · embark [CHARACTER] [asc=N] [seed=S]
timeline    reveal_epochs (얻은 에포크를 공개해 해금을 프로필에 반영하고 메인 메뉴로 돌아간다) · back
run         quit_to_menu (저장 후 메인 메뉴) · abandon_run      (state detail=full에서 보인다)
fallback    click <i>                              (전용 처리기가 없는 화면)
```

효과가 카드 선택을 요구하면 `choice` 화면이 나온다. 카드를 소멸시킬 때, 이벤트나 휴식처에서 카드를 강화할 때, 카드를 제거할 때가 그런 경우다. 선택 요청이 나올 것을 미리 알면, 그 선택까지 같은 호출에 이어서 적는다. 예: `["play c9", "choose 0"]`

게임은 방에 들어갈 때(그리고 전투에서 이기거나 이벤트를 마쳤을 때) 자동으로 저장한다. 런을 종료했다가 이어서 하면 마지막 저장 시점부터 다시 시작한다. 따라서 저장 뒤에 방 안에서 한 일은 취소된다. 상점에서 산 물건이 그 예다.

### 예시

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

게임 문구는 게임에 설정된 언어로 나오고(위 예시는 한국어), 화면 구조를 나타내는 단어는 짧은 영어 단어로 나온다.

## 지식 베이스

`kb/`에는 한국어 문서가 있다. 게임 버전 하나를 기준으로 몬스터 121종과 이벤트 68개의 동작을 정리한 문서이고, 게임 문구만으로는 알 수 없는 내용을 다룬다. `info`로 몬스터나 이벤트를 조회하면 해당 문서가 함께 나온다.

문서는 로컬에서 디컴파일한 게임 코드를 보고 썼다. 디컴파일한 코드는 저장소에 넣지 않는다. 문서마다 근거로 삼은 원본 파일의 해시를 기록해 두었다. 덕분에 게임이 업데이트되면 `tools\kb-check.ps1`이 원본이 바뀐 문서를 찾아 준다. 문서를 쓴 게임 버전이 실행 중인 버전과 다르면, `info`가 문서 앞에 "오래된 내용일 수 있다"는 안내를 함께 보여 준다. 문서 형식과 업데이트 절차는 [kb/README.md](kb/README.md)에 있다.

`build.ps1 -Install`과 `tools\redeploy.ps1`은 문서를 DLL 옆 `STS2AgentMcp-kb\` 폴더에 설치한다.

## 개발

```powershell
.\tools\redeploy.ps1                       # 빌드 → 게임 종료 → 설치 → Steam으로 실행 → 준비될 때까지 대기
.\tools\mcp.ps1 state                      # MCP 클라이언트 없이 엔드포인트 호출
.\tools\mcp.ps1 act "play c3 @e1" "end"
.\tools\mcp.ps1 info BASH
.\tools\autofight.ps1                      # 단순한 규칙으로 현재 전투를 끝낸다 (시험용 도구, 플레이 에이전트가 아니다)
```

지식 베이스를 관리하는 스크립트는 다음과 같다.

```powershell
.\tools\kb-check.ps1 -Decompiled <디컴파일 폴더>                 # 원본이 바뀐 문서, 문서가 없는 몬스터와 이벤트
.\tools\kb-stamp.ps1 -Decompiled <디컴파일 폴더> -Version <버전>  # 원본 해시와 게임 버전 기록
.\tools\kb-glossary.ps1 -Loc <현지화 문구 폴더>                   # 문서별 게임 용어 목록 생성, 게임 이름과 다른 이름 보고
```

게임 로그는 `%APPDATA%\SlayTheSpire2\logs\godot.log`에 있고, 이 모드가 남긴 줄은 `[STS2 Agent MCP]`로 시작한다.

설정에서 `dev_tools`를 켜면 `dev_console(command)` 도구가 생긴다. 게임의 개발자 콘솔 명령(`room RestSite`, `event SPIRALING_WHIRLPOOL`, `fight ...`, `die` 등)을 그대로 실행하고, 모드가 더한 명령 두 개를 받는다. `event_any <ID>`는 일반 이벤트 목록에 없는 이벤트(`THE_ARCHITECT` 등)로 들어가고, `profile <n>`은 열려 있는 프로필 화면에서 n번째 프로필을 고른다. 시험은 실제 진행 기록이 아닌 다른 프로필에서 한다.

## 보안

- localhost에서만 요청을 받고, 모든 요청에 bearer 토큰을 요구한다.
- `Origin` 헤더가 있는 요청(웹 페이지에서 보낸 요청)은 거부한다. CORS 헤더는 보내지 않는다.
- 세이브 프로필을 삭제하거나 바꾸는 기능은 없다.

## 크레딧

이 프로젝트는 Yikun Ji(Kunologist)와 기여자들이 만든 [STS2MCP](https://github.com/Gennadiyev/STS2MCP)를 분석하며 시작했다. 구조는 새로 설계해 별도로 구현했다. 그 프로젝트에서는 지식과 작은 코드 조각 몇 개만 가져왔다. 자세한 내용은 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)에 있다.

Slay the Spire 2는 Mega Crit의 게임이다. 이 모드는 비공식 팬 모드다.

## 라이선스

MIT 라이선스로 배포한다. 자세한 내용은 [LICENSE](LICENSE)를 참고한다.

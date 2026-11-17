# 게임 안 채팅창

상태: 1차 구현 완료(화면 배치, 채팅 패널, Claude Code 연결). 다음 작업은 로그인 분리(3.1절). 기준 게임 버전 v0.111.1.

게임 화면 왼쪽에 에이전트와 대화하는 채팅창을 두고, 게임은 나머지 영역에 전체 높이로 그린다. 채팅창 뒤에서는 모드가 Claude Code를 직접 실행하고, Claude Code는 이 모드의 MCP 도구로 게임을 본다.

설정 파일(`STS2AgentMcp.conf`)에서 켠다. 기본값은 꺼짐이다.

```json
{ "chat_panel": true, "chat_width": 480, "claude_path": "C:\\...\\claude.exe" }
```

- `chat_panel`: 채팅창을 켠다. 게임 노드가 준비되는 즉시(메인 메뉴 전부터) 적용된다.
- `chat_width`: 채팅창 폭. 창 높이를 1080으로 볼 때의 단위다. 경계선을 끌면 저장된다.
- `claude_path`(선택): Claude Code 실행 파일. 없으면 PATH와 `~/.local/bin`에서 찾는다.

## 1. 지금 동작하는 것

실제 게임(v0.111.1, 1280×720·1493×720 창)에서 확인했다.

| 항목 | 결과 |
|---|---|
| 배치 | 채팅창은 고정 폭, 게임은 나머지 영역에 전체 높이. 창 폭을 바꾸면 게임 영역만 바뀐다. 창 크기 변경 후에도 유지된다(사용자 확인) |
| 게임 재배치 | 게임 영역의 비율에 맞춰 게임이 스스로 UI를 다시 배치한다(상단 바, 지도, 전투, 휴식처, 메인 메뉴 확인) |
| 입력 | 호버, 툴팁, 클릭, 카드 드래그, 대상 지정 화살표가 커서와 맞는다(사용자 확인). 드래그한 카드는 채팅창으로 넘어가지 않는다 |
| 입력창과 단축키 | 입력창에 타이핑하는 동안 게임 단축키가 눌리지 않는다. 한글 입력 포함(사용자 확인) |
| 경계선 | 끌어서 채팅창 폭을 바꾼다. 놓으면 `chat_width`에 저장한다 |
| 채팅 | 메시지를 보내면 Claude Code가 실행되고, `state`·`info`로 게임을 확인한 뒤 답한다. 도구 사용은 안내 줄로 보인다 |
| 프로세스 정리 | 게임이 강제 종료돼도 claude 프로세스가 남지 않는다 |
| 스크린샷 | 채팅창이 켜져 있으면 `screenshot`은 게임 영역만 저장한다 |

## 2. 구현 방식

### 2.1 화면 배치 (`src/Chat/ChatLayout.cs`)

- 루트 창의 콘텐츠 크기를 게임 영역의 비율 r에 맞춰 바꾸고, 비율 모드를 `Ignore`로 둔다. 콘텐츠 크기는 게임의 "자동" 비율 규칙(`NGlobalUi.OnWindowChange`)을 그대로 따른다.
  - r > 21:9: 2580 × (2580 / r)
  - r < 4:3: (1260 × r) × 1260
  - 그 사이: 높이 1080(r ≥ 14:9) 또는 폭 1680
- 게임은 이 크기를 자기 화면으로 보고 UI를 다시 배치한다. 렌더링은 창 전체에 한다(`Keep`과 달리 가운데 영역으로 잘리지 않는다).
- `Ignore`로 생긴 늘어남은 캔버스 변환으로 되돌리면서 게임을 오른쪽에 붙인다. 변환은 기본 캔버스(`Viewport.CanvasTransform`)와 루트 뷰포트의 모든 게임 CanvasLayer에 같이 건다. 지금 게임 레이어는 `/root/Game/CanvasLayer`(L10) 하나다. 나중에 생기는 레이어는 `NodeAdded`로 잡는다. 다른 뷰포트(지도 그리기, 카드 이미지) 안의 레이어는 건드리지 않는다.
- 게임과 충돌하는 곳:
  - `NGlobalUi.OnWindowChange`, `NMainMenu.OnWindowChange`(자동 비율 처리기): 켜져 있는 동안 Harmony prefix로 건너뛴다. 두면 매 프레임 서로 덮어쓴다.
  - `NGame.ApplyDisplaySettings`(설정 화면, 시작 시): postfix로 다시 배치한다.
  - 그 밖의 변경은 매 프레임 창 크기와 콘텐츠 설정을 검사해서 다시 배치한다.
- 위·아래·오른쪽에 게임이 영역 밖으로 그리는 것(큰 배경, 스크롤되는 지도)은 검은 마스크로 가린다.
- 채팅창 폭: 최소 240단위. 최대는 게임 영역 비율이 1:1이 될 때까지(`MinGameAspect`).

### 2.2 입력 (`src/Game/MouseRemap.cs`)

- GUI 입력(호버, 클릭)은 캔버스 변환을 따라가므로 따로 할 일이 없다.
- `Viewport.GetMousePosition()`은 변환을 반영하지 않는다. 게임은 이 값으로 드래그한 카드 위치(`NMouseCardPlay`)와 대상 지정 화살표(`NTargetingArrow`)를 그린다. Harmony postfix로 루트 뷰포트일 때만 게임 좌표로 바꾸고, 게임 영역 안으로 자른다(원래 게임에서 창 가장자리가 커서를 막는 것과 같다). 인라인 문제 없이 게임 코드에도 적용되는 것을 확인했다.
- `CanvasItem.GetGlobalMousePosition()`은 엔진이 캔버스 변환을 역으로 적용하므로 원래 맞다(지도 그리기, 보물방, 포션 팝업).
- 입력창이 편집 중이면 게임 단축키(`NHotkeyManager`)와 개발 콘솔(`NDevConsole`)이 스스로 키를 무시한다.

### 2.3 시험했다가 버린 방법

| 방법 | 결과 |
|---|---|
| `Viewport.GlobalCanvasTransform` 하나로 축소 | 게임은 맞지만 새로 만든 채팅 패널이 입력을 받지 못했다 |
| 16:9 화면을 72%로 축소 | 동작하지만 위아래에 띠가 남고 게임이 작다 |
| 콘텐츠 4:3 + `Keep` + 오른쪽으로 이동 | 루트 뷰포트가 창 가운데 영역에만 렌더링돼서 오른쪽이 잘린다 |
| `NGame`을 SubViewport로 옮기기 | 노드를 트리에서 빼면 `NGame._ExitTree`가 Steam, ModManager, Sentry를 종료한다 |

### 2.4 채팅 패널 (`src/Chat/ChatPanel.cs`, `ChatTheme.cs`)

고대인 대화 줄(`scenes/events/ancient_dialogue_line.tscn`)의 자원과 값을 가져와 메신저 모양으로 만들었다. 장면 자체는 고대인 이벤트 데이터가 필요한 스크립트가 붙어 있어서 쓰지 않는다.

- 말풍선: `images/ui/dialogue_nine_patch.png`(9분할 27/28, 타일), 그림자 (2, 5) 검정 25%. 꼬리: `images/ui/dialogue_tail.png`(폭 28, 12 겹침).
- 글자: 게임 언어의 글꼴(`FontManager.GetSubstituteFont`, 없으면 Kreon), 크기 24, 크림색, 그림자 (3, 2).
- 에이전트(왼쪽): 니오우 아이콘(`images/ui/run_history/neow.png`)과 니오우 대화 색(#28454F).
- 플레이어(오른쪽): 런 중이면 현재 캐릭터의 아이콘(`IconTexture`, `IconOutlineTexture`)과 `DialogueColor`. 런 밖이면 아이콘 없이 중립 색.
- 말풍선은 짧은 글에 맞춰 줄고, 긴 글은 채팅창 폭에서 줄바꿈한다. `**굵게**`와 `- 목록`만 바꾸고 나머지 마크다운 기호는 지운다.
- 도구 사용은 가운데 금색 안내 줄로 보인다. 게임 글꼴에 이모지가 없으므로 기호만 쓴다.
- 머리글: 아이콘, 이름, 상태(연결 중/생각 중/대기 중, 게임 연결 여부, 모델).
- 입력창: 말풍선 그림을 어둡게 칠해 테두리로 쓴다. Enter 전송, Esc로 게임에 포커스를 돌려준다.
- 새 메시지가 오면 맨 아래로 따라간다. 위로 올려 읽는 중이면 따라가지 않는다.
- 자원마다 대체 수단이 있다. 게임 업데이트로 파일이 바뀌면 모양만 달라진다.

### 2.5 Claude Code 연결 (`src/Chat/AgentSession.cs`, `ChatController.cs`, `JobObject.cs`)

- 첫 메시지를 보낼 때 실행한다:
  `claude -p --input-format stream-json --output-format stream-json --verbose --mcp-config <작업 폴더>/mcp.json --strict-mcp-config --tools "" --allowedTools mcp__sts2__state,mcp__sts2__act,mcp__sts2__info,mcp__sts2__search --permission-mode dontAsk --system-prompt <프롬프트>`
- 이 모드의 MCP 엔드포인트와 토큰만 연결한다. 내장 도구는 모두 끄고 플레이 도구 네 개만 허용한다. 나머지(이미지 도구, `dev_layout` 등)는 묻지 않고 거부한다.
- 작업 폴더: `%LOCALAPPDATA%\sts2-agent-mcp\chat`. Claude Code는 대화를 작업 폴더 기준으로 저장하므로 바꾸지 않는다.
- 환경변수 정리: `ANTHROPIC_API_KEY`, `ANTHROPIC_AUTH_TOKEN`, `ANTHROPIC_BASE_URL`, `CLAUDE_CODE_USE_BEDROCK`, `CLAUDE_CODE_USE_VERTEX`, `CLAUDECODE`, `CLAUDE_CODE_ENTRYPOINT`, `CLAUDE_CODE_SESSION_ID`, `CLAUDE_CODE_SSE_PORT`. 3.1절에서 API 키 처리를 바꾼다.
- 시스템 프롬프트: 조언이 기본이고 `act`는 요청할 때만, 판단 전에 `state` 확인, 짧게(표·코드 블록 없이), 게임에 표시된 이름 사용.
- 프로세스 하나에 대화 하나. 메시지는 큐에 넣고 `result` 이벤트마다 한 턴씩 보낸다.
- 처리하는 이벤트: `system/init`(세션 ID, 모델, MCP 연결 상태), `assistant`(글 → 말풍선, `tool_use` → 안내 줄), `result`(턴 종료, 오류 표시), `system/compact_boundary`(요약 안내).
- claude가 종료되면 다음 메시지에서 같은 세션을 `--resume`으로 이어 다시 시작한다. 게임을 껐다 켜면 새 대화다.
- Windows Job Object(KILL_ON_JOB_CLOSE)로 묶어서, 게임이 강제 종료돼도 claude가 남지 않는다.

## 3. 작업 계획

### 3.1 로그인 분리 (다음 작업)

요구사항:
- 게임 전용 설정 폴더로 Claude Code 로그인을 분리한다(`CLAUDE_CONFIG_DIR` = `%LOCALAPPDATA%\sts2-agent-mcp\claude-config`). 지금은 이 PC의 평소 로그인을 그대로 쓴다.
- 구독과 API 키 모두 동작하게 한다. 둘 다 있으면 구독을 우선한다.
- API 키는 모드가 입력받거나 저장하지 않는다. 사용자가 직접 설정한 `ANTHROPIC_API_KEY` 환경변수만 쓴다.
- 둘 다 없으면 채팅창 가운데에 로그인 카드를 띄운다.

참고 구현: TH-mcp-app의 `standalone-game` 브랜치(`game/agents.js`, `game/commands.js`, `game/app.js`의 로그인 감시, `game/ui/launcher.js`의 카드). 이 구조를 따르되, 그쪽은 구독 전용(API 키 변수를 항상 지움)이라는 점이 다르다.

조사한 것(Claude Code 2.1.78):
- `claude auth status`: 기본 JSON 출력. 로그인 안 됨이면 종료 코드 1.
  - 안 됨: `{"loggedIn": false, "authMethod": "none", "apiProvider": "firstParty"}`
  - 구독: `authMethod: "claude.ai"`, `subscriptionType`(예: `max`), `email`, `orgId`, `orgName`
  - API 키: `authMethod: "api_key"`, `apiKeySource: "ANTHROPIC_API_KEY"`. 환경변수가 있는지만 보고 키가 유효한지는 확인하지 않는다.
  - 저장된 로그인을 읽기만 하므로 만료된 로그인도 `loggedIn: true`로 답한다(TH에서 확인). 그래서 실제 요청으로 한 번 더 확인한다.
- `claude auth login [--email <email>] [--sso]`, `claude auth logout`. TH가 쓰는 `--claudeai` 플래그는 2.1.78에 없다. 플래그 없이 실행하거나 버전을 보고 붙인다.
- 로그인은 창 없이 실행하지 않고 별도 콘솔 창에서 실행한다(TH 방식). 2.1.78에서 콘솔 창 로그인이 어떻게 진행되는지(브라우저 자동 열기, 코드 붙여 넣기 여부)는 구현할 때 확인한다.

설계:
1. 실행 파일 찾기: `claude_path` → PATH → `~/.local/bin`(공식 설치), `%APPDATA%\npm`(npm, `.cmd`), `~/.claude/local`, `%LOCALAPPDATA%\Microsoft\WinGet\Links` 순서. `.cmd`는 cmd.exe를 거쳐 실행하고 인자를 따옴표로 감싼다.
2. 판정: `--version`, `auth status`(API 키 변수 없이), 실제 요청 한 번을 동시에 실행한다. 실제 요청은 `-p ok --model haiku --output-format json --tools "" --no-session-persistence --strict-mcp-config --system-prompt "Reply with OK."`.
   1. 구독: `loggedIn`이고, `apiProvider`가 `firstParty`이고, `subscriptionType`이 있고 `free`가 아니고, 실제 요청이 성공하면 API 키 변수를 지우고 구독으로 실행한다.
   2. 구독이 안 되고 게임 프로세스에 `ANTHROPIC_API_KEY`가 있으면, 키를 넘겨 실제 요청으로 확인한 뒤 키로 실행한다.
   3. 둘 다 안 되면 로그인 카드를 띄운다.
   - `auth status`가 JSON이 아니면 오래된 버전으로 보고 `claude update`를 안내한다.
   - 오류 문구가 401, authenticat, oauth, token has expired, log in again, re-auth, not logged in 등이면 로그인 만료로 본다.
3. 로그인 카드: 설치(버전), 계정, 플랜 세 칸과 상태(사용 가능/사용 불가/로그인 대기), 무엇이 문제인지 한 문장, [로그인] 버튼. 카드를 누르면 다시 확인한다. API 키 안내 한 줄: `ANTHROPIC_API_KEY` 환경변수를 설정한 뒤 Steam과 게임을 다시 실행한다(게임은 Steam의 환경을 물려받는다).
4. [로그인]: 별도 콘솔 창(`cmd /c start`)에서 `claude auth login`을 실행한다. 로그인은 사용자가 브라우저와 그 창에서 직접 끝낸다. 끝나면 자동으로 다시 확인한다. TH는 `.credentials.json`의 수정 시각을 1초마다 보지만, 여기서는 자격증명 파일을 건드리지 않도록 전용 폴더의 변경을 보거나 `auth status`를 주기적으로 실행한다. 감시는 15분 뒤 멈춘다.
5. 대화 중 로그인이 만료되면 안내 줄을 띄우고 로그인 카드로 돌아간다.
6. 머리글 상태에 인증 방식을 보여 준다(예: "구독(Max)", "API 키").
7. 환경변수 정리 목록을 TH 기준으로 넓힌다. 추가: `CLAUDE_CODE_USE_FOUNDRY`, `CLAUDE_CODE_CHILD_SESSION`, `CLAUDE_CODE_SESSION_ATTENDED`, `CLAUDE_CODE_EXECPATH`, `CLAUDE_CODE_MESSAGING_SOCKET`, `CLAUDE_CODE_MESSAGING_TOKEN`, `CLAUDE_PID`, `CLAUDE_AGENT_SDK_VERSION`, `CLAUDE_EFFORT`. `CLAUDE_CONFIG_DIR`은 게임 폴더로 덮어쓴다. `ANTHROPIC_API_KEY`는 구독으로 실행할 때만 지운다.
- 주의: 전용 설정 폴더를 쓰므로 평소 `~/.claude/settings.json`의 `apiKeyHelper`나 `env`는 적용되지 않는다. 실제 환경변수만 통한다.

정할 것:
- 설치 버튼: 설치되어 있지 않으면 공식 설치 스크립트(`irm https://claude.ai/install.ps1 | iex`)와 로그인을 한 콘솔 창에서 이어 실행하는 버튼을 둘지(TH에는 있다).
- 로그아웃 버튼: 머리글에 두고 `claude auth logout` 후 실행 중인 세션을 끝내고 다시 확인할지(TH에는 없다).

배포 정책(2026-11-17 확인. 근거: [Claude Code legal-and-compliance](https://code.claude.com/docs/en/legal-and-compliance), [Agent SDK overview](https://code.claude.com/docs/en/agent-sdk/overview), [Usage Policy](https://www.anthropic.com/aup) 2026-11-12판):
- 제3자 제품이 claude.ai 로그인을 "제공"하거나 사용자 대신 구독 자격증명으로 요청을 보내는 것은 금지다. 자격증명이나 세션 토큰을 수집·저장·중계하는 것도 금지다.
- 예외: 사용자가 수정하지 않은 Claude Code 바이너리에 본인 구독으로 로그인하는 것은 막지 않는다고 명시돼 있다.
- 현재 방식(사용자가 직접 설치하고 로그인한 Claude Code 실행)과 사용자 본인 API 키는 허용 범위다. 공용 키를 넣어 배포하는 것은 금지다.
- 로그인 카드의 [로그인] 버튼은 예외 조항 안에 든다고 보지만, Agent SDK 문서의 "offer claude.ai login" 문구와 충돌할 여지가 있다. 공개 배포 전에 Anthropic(contact sales)에 확인한다.
- 구현 조건:
  - 로그인은 Claude Code의 흐름으로만 끝낸다. 로그인 코드를 모드 UI로 받아 넘기지 않는다. 붙여 넣기가 필요하면 콘솔 창에서 사용자가 직접 입력한다.
  - `.credentials.json`은 읽거나 복사하지 않는다. 상태는 `claude auth status`로만 확인한다. `setup-token`이나 `CLAUDE_CODE_OAUTH_TOKEN`은 쓰지 않는다.
  - 구독과 API 키를 모두 쓸 수 있게 둔다. 어느 인증 방식도 막지 않는다.
  - 기능 이름이나 버튼 이름에 "Claude Code"를 쓰지 않는다. 설명문으로 "설치된 Claude Code를 실행"한다고 쓰는 것은 된다.
- 문의 결과가 불허면 배포판에서 [로그인] 버튼을 빼고 "터미널에서 직접 로그인" 안내와 API 키 안내만 남긴다. 현재 방식은 그대로 둔다.

### 3.2 런 단위 세션 보관과 재개

- 런 하나에 대화 하나(런 시드로 구분). 세션 ID를 모드 설정 폴더에 보관하고, 이어하기에서 `--resume`으로 재개한다.
- 재개에 실패하면 새 대화로 시작하고 현재 상태(`state`)를 첫 메시지로 넣는다.
- 시스템 프롬프트가 바뀌면 `--resume`은 이전 프롬프트를 유지하므로, 바뀐 내용을 메시지로 알린다(프롬프트 버전을 세션과 함께 보관).
- 대화 기록을 채팅창에 다시 그릴지 정한다(Claude Code의 세션 파일에서 읽기 / 모드가 따로 저장).

### 3.3 답 실시간 표시

- `--include-partial-messages`로 글자 단위로 받아 말풍선을 키워 간다.

### 3.4 게임 안이라서 할 수 있는 것

- 카드 그림(`render_cards`)과 지도(`render_map`)를 파일 없이 채팅창에 바로 띄운다.
- 에이전트가 제안한 카드나 경로를 게임 화면에서 강조한다.
- "상황 물어보기" 버튼처럼 게임 쪽에서 대화를 시작한다.

### 3.5 설정과 편의

- 모델 선택(`chat_model`). 기본 모델(Opus)은 한 턴에 40~60초 걸렸다.
- 게임 화면을 클릭하면 입력창 포커스를 놓는다(지금은 Esc).
- 채팅창 접기/펴기.

### 3.6 남은 확인

- 게임 영역이 4:3보다 좁을 때 게임의 좁은 창 배치가 깨지지 않는지. 깨지면 `MinGameAspect`를 4:3으로 올린다.
- 전체 화면, 21:9 창, 설정에서 화면 비율을 바꿀 때.
- 경계선에서 좌우 화살표 커서가 실제로 보이는지(게임이 커서를 따로 관리한다).
- 채팅창 위에서 카드를 놓았을 때. 지금은 게임 왼쪽 가장자리에서 놓은 것으로 처리돼서, 높이가 사용 구역이면 대상 없는 카드가 사용될 수 있다.
- 첫 메시지 때 MCP 연결이 늦는 경우(지금까지는 문제없었다).

## 4. 개발 메모

- 브랜치: 채팅창 작업은 `in-game-chat` 브랜치에서만 한다. `main`은 배포된 MCP 모드 그대로 두고, 따로 정하기 전까지 병합하거나 PR을 만들지 않는다.
- 게임 경로: 게임이 기본 경로(`D:\SteamLibrary\...`)에 없으면 `build.ps1`, `tools\redeploy.ps1`, `tools\mcp.ps1`을 실행하기 전에 `STS2_GAME_DIR`을 설정한다. 채팅창을 시험하려면 설치된 `STS2AgentMcp.conf`에 `"chat_panel": true`를 넣는다.
- 게임 코드: `sts2.dll`을 저장소 밖 폴더에 디컴파일해서 본다(ilspycmd). 디컴파일 결과는 저장소에 넣지 않는다.
- Windows PowerShell 5.1에서 주의할 점: `ProcessStartInfo.ArgumentList`와 `Process.Kill(bool)`이 없다. here-string을 `git commit -F -`로 넘기면 메시지가 인자로 들어가 커밋이 안 된다. 메시지는 임시 파일에 쓰고 `-F <파일>`로 넘긴다.
- 사람이 PC를 쓰는 중일 수 있으므로 시험할 때 실제 마우스를 움직이지 않는다. `dev_layout`의 가짜 입력(move, click, drag, type, say, shot)을 쓴다. 게임이 실제 커서를 읽는 곳(드래그한 카드, 대상 지정 화살표)은 사람이 직접 시험한다.

## 5. 개발 도구 (`dev_layout`)

MCP 도구로 노출된다. 채팅용 Claude Code에는 허용하지 않는다.

| action | 하는 일 |
|---|---|
| `info` | 배치 상태, 창·콘텐츠 크기, 변환, 루트 뷰포트의 CanvasLayer |
| `on` / `off` | 채팅 배치 켜기/끄기 |
| `hover` / `move` / `click` / `drag` | 가짜 마우스 입력(창 픽셀). 실제 커서는 움직이지 않는다. 게임이 실제 커서를 읽는 곳(드래그 카드, 화살표)은 이걸로 시험할 수 없다 |
| `type` | 가짜 키 입력(영문, 숫자, 공백) |
| `say` | 채팅 메시지 보내기 |
| `shot` | 채팅창까지 포함한 창 전체 스크린샷 |
| `probe` | `res://` 장면·자원 구조 보기(`text`에 경로, 디렉터리는 `/`로 끝낸다) |

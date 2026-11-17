# STS2 Agent MCP — 설계

기준 게임 버전: Slay the Spire 2 v0.111.1 (처음 설계와 P0·P1 검증은 v0.107.1에서 했다)
게임 동작은 디컴파일한 `sts2.dll`로 확인했다(디컴파일 결과는 저장소에 포함하지 않는다).
이 문서의 "기존 모드"는 출발점으로 참고한 [STS2MCP](https://github.com/Gennadiyev/STS2MCP)를 가리킨다.

## 1. 목표와 비목표

**목표**
- AI 에이전트가 싱글플레이 런을 적은 왕복과 짧은 응답으로 진행할 수 있는 MCP 서버를 게임 모드 안에 직접 둔다.
- 행동은 "실행 → 게임이 안정될 때까지 대기 → 무슨 일이 일어났는지 + 현재 상태" 한 번의 호출로 끝난다.
- 계산에 필요한 정보(게임이 계산한 카드 수치, 파워, 의도 피해량, 유물 카운터, 캐릭터 자원)를 빠짐없이 제공한다. 시뮬레이터는 두지 않는다.
- 다가올 대형 업데이트(새 캐릭터, 새 게임 모드)에 경직되지 않는다. 모르는 화면·자원·모드가 나와도 진행이 막히지 않는다.

**비목표**
- 멀티플레이 (전부 제외)
- 미리 계산/시뮬레이션 (`act`에 확장 자리만 남김)
- 프로필 삭제 등 세이브를 파괴하는 기능
- 플레이 전략 (P3에서 별도로)

## 2. 전체 구조

```
[에이전트] ── MCP (Streamable HTTP, JSON 응답) ── [게임 모드: STS2AgentMcp.dll]

 Host       HTTP 수신, 인증, MCP JSON-RPC, 메인 스레드 스케줄러
 Tools      state / act / info, 이미지 도구, export_loc  (도구 정의와 인자 파싱만)
 Session    한 번의 act 실행 흐름: 행동 실행 → 안정 대기 → 이벤트 수집 → 화면 렌더
 Screens    화면 처리기 레지스트리 (+ 범용 대체 처리기)
 View       우리 쪽 상태 모델(게임 타입 비의존) + 텍스트 인코더
 Game       게임 내부 접근을 전부 여기에 모음 (Adapter)
            Settle / Choices / Events / ScreenProbe / Catalog / Ui / Features
```

의존 방향은 위에서 아래로만 흐른다. `Game` 밖에서는 `MegaCrit.*` 타입을 직접 쓰지 않는 것을 원칙으로 한다.
예외: `Screens`의 처리기는 해당 화면의 게임 타입을 다뤄야 하므로 사용한다. 대신 처리기 하나가 화면 하나만 책임진다.

### 폴더 구성

```
(repo root)
  DESIGN.md  README.md  build.ps1  STS2AgentMcp.csproj  mod_manifest.json
  tools/mcp.ps1        개발용: MCP 클라이언트 없이 엔드포인트 직접 호출
  tools/redeploy.ps1   개발용: 빌드 → 게임 종료 → 설치 → 실행 → 준비될 때까지 대기
  tools/autofight.ps1  개발용: 단순 규칙으로 전투를 끝내서 다른 화면까지 빨리 이동 (플레이 에이전트 아님)
  tools/kb-*.ps1       지식 베이스 관리: kb-check(원본 변경 검사), kb-stamp(해시 기록), kb-glossary(게임 용어 정리)
  kb/                  몬스터와 이벤트 문서 (14절). 설치 시 DLL 옆 STS2AgentMcp-kb/로 복사
  src/
    Host/      ModEntry  HttpHost  McpEndpoint  MainThread  Config  Features  Log
    Tools/     ToolRegistry  StateTool  ActTool  InfoTool  SearchTool  CaptureTools (screenshot, render_cards, render_map)  ExportLocTool
               DevLayoutTool (dev_layout: 채팅창 개발용)  ScenePeek (장면·자원 구조 보기)
    Session/   ActRunner (행동 실행 → 안정 대기 → 이벤트 수집 → 렌더)
    Screens/   Core (ActionSpec, ExecResult, ScreenView, IScreenHandler, ScreenRegistry)
               CombatHandler  ChoiceHandler  RewardHandlers (rewards, card_reward)
               MapHandler  MenuHandlers (popup, menu, lobby, game_over, generic)
               RoomHandlers (event, rest_site, shop, treasure, crystal_sphere, bundle)
               PotionActions (모든 런 화면 공통)  RunActions (quit_to_menu, abandon_run)
    View/      Fmt (출력 형식 전부 + 캐릭터 자원 제공자 Resources)
    Game/      GameCtx  Observation  Settle  TurnTracker  ChoiceBroker  EventLog  Ids  Ui  Text  Catalog
               Search (카드·유물·포션 검색)  MonsterInfo (행동 상태 기계 요약)  KbDocs (kb 문서 읽기)  Capture  MapRender (이미지 생성)
               MouseRemap (채팅창 배치에서 마우스 좌표 보정)
    Chat/      게임 안 채팅창 (15절): ChatLayout (화면 배치)  ChatPanel  ChatTheme (게임 말풍선 자원)
               AgentSession (Claude Code 실행)  ChatController  JobObject
```
(구현하면서 파일을 합쳤다: 처음 안의 `ScreenProbe`·`IdMap`·`TextEncoder`는 각각 `Observation`·`Ids`·`Fmt`가 맡는다. 게임 없이 돌리는 단위 테스트는 아직 없다.)

모드 id `STS2AgentMcp`, 기본 포트 `15527`을 써서 기존 모드(id `STS2_MCP`, 포트 15526)와 함께 설치돼도 충돌하지 않게 한다.

## 3. 실행 모델

- HTTP 요청은 백그라운드 스레드에서 받는다. 게임 상태 접근은 전부 Godot 메인 스레드에서 한다.
- `MainThread`: `SceneTree.ProcessFrame`마다
  1. 대기 중인 작업을 **모두** 실행한다 (기존 모드의 "10개 초과 시 1개 유실" 버그 제거).
  2. 등록된 프레임 대기자(조건 함수)를 평가하고, 만족하면 완료한다.
- 모든 대기에는 시간 제한이 있다. 시간 초과 시 진단 정보(현재 화면, 실행 중 액션, 전투 단계)를 담아 반환한다. HTTP 스레드가 영원히 막히는 일은 없다.
- 시작 시 `SaveManager.Instance.PrefsSave.FastMode = Instant`로 설정한다 (게임이 부팅 때 Fast로 되돌리므로 매번 설정). 설정으로 끌 수 있게 한다.

## 4. MCP 엔드포인트

- `POST /mcp` 하나. JSON-RPC 2.0 단건 요청 → `Content-Type: application/json` 단건 응답. SSE는 쓰지 않는다. `GET /mcp`는 405.
- 메서드: `initialize`, `notifications/initialized`(202 응답), `ping`, `tools/list`, `tools/call`.
- 프로토콜 버전: 클라이언트가 요청한 버전이 지원 목록에 있으면 그대로, 없으면 우리 최신 버전으로 응답.
- 인증: `Authorization: Bearer <token>`. 토큰은 첫 실행 때 생성해서 DLL 옆 `STS2AgentMcp.conf`에 저장한다.
- 브라우저 차단: `Origin` 헤더가 있으면 거부, CORS 헤더를 내보내지 않음, `Content-Type: application/json`만 허용, 본문 크기 제한, localhost에만 바인딩.
- 오류 응답에 스택 트레이스를 넣지 않는다(게임 로그에만 남김).

클라이언트 설정 예:
```json
{ "mcpServers": { "sts2": { "type": "http", "url": "http://127.0.0.1:15527/mcp",
  "headers": { "Authorization": "Bearer <token>" } } } }
```
참고: 게임이 꺼져 있으면 Claude Code 세션 시작 때 연결 실패로 표시된다. 게임을 켠 뒤 `/mcp`로 재연결한다.

## 5. 도구

| 도구 | 인자 | 역할 |
|---|---|---|
| `state` | `detail?: "brief" \| "full"` | 현재 화면, 가능한 행동, 요약 상태 |
| `act` | `actions: string[]`, (예약) `dry_run` | 행동을 순서대로 실행. 각 행동 사이에 안정 대기. 결과 = 행동별 이벤트 + 최종 화면 |
| `info` | `ids: string[]` | 카드·유물·파워·포션·몬스터·조우·이벤트·키워드 설명 (ModelId 또는 화면에 표시된 이름, 전투 중 적 `e1`, 이번 막 보스 `boss`). 카드는 강화 후 비용·설명을, 몬스터는 행동 상태 기계를, 몬스터와 이벤트는 kb 문서를 덧붙인다 |
| `search` | `query?`, `kind?`, `pool?`, `type?`, `rarity?`, `cost?`, `keyword?`, `upgraded?`, `limit?` | 카드·유물·포션 검색. 이름·설명·id 문자열 검색(단어 AND) + 조건. 한 줄 목록. 강화 기준 검색은 카드 라이브러리와 같은 방식(복제 → 강화 → 미리보기 수치) |
| `screenshot` / `render_cards` / `render_map` | `attach?` 등 | 채팅 플레이용 이미지. 파일로 저장하고 경로를 돌려준다 (`attach: true`면 이미지도 반환) |
| `render_map` 덧그리기 | `coords?`, `paths?`, `marks?`, `notes?` | 이미지에만 그린다(게임 지도의 그리기 기능은 쓰지 않음). 칸은 `r7c3` / `current` / `boss`. 없는 칸·잘못된 색은 오류로 돌려준다 |
| `export_loc` | `tables?: string[]` | kb 관리용. 현지화 문구 표를 JSON으로 저장 |

런 시작·이어하기·포기도 별도 도구 없이 메뉴 화면의 행동으로 제공한다 (9.1절). 플레이에 쓰는 도구는 state, act, info, search로 유지하고, 나머지는 보여 주기와 관리용이다.

### 행동 문법

화면이 "지금 가능한 행동"을 직접 나열한다. 에이전트는 그 형식을 그대로 쓴다.

```
play c12 @e1      카드 사용 (c = 전투 카드 id, e = 적 id)
potion p0 @e1     포션 사용 (p = 슬롯)
end               턴 종료
choose 2          선택지 고르기 (보상, 카드 보상, 맵 노드, 선택 요청 등 화면의 번호)
choose 0 3        여러 장 고르기 (선택 요청에서 min/max 범위)
skip | confirm | proceed
click 4           범용 처리기: 화면에 보이는 버튼 N 누르기
```

- 카드 id는 게임의 `NetCombatCardDb` id를 쓴다. 전투 동안 카드마다 고정이라 한 장을 내도 다른 카드 id가 밀리지 않는다.
- 적 id는 `CombatState`의 크리처 순서가 아니라 `Creature.CombatId` 기반으로 고정한다 (`IdMap`).
- 번호(`choose N`)는 같은 화면 처리기가 렌더와 실행을 모두 담당하므로, 표시한 번호와 실행하는 대상이 항상 일치한다.

### act 실행 규칙

1. 행동 하나를 메인 스레드에서 실행한다.
2. `Settle`로 안정될 때까지 기다린다.
3. 다음 중 하나면 남은 행동을 실행하지 않고 멈춘다:
   - 행동 오류(잘못된 id, 낼 수 없는 카드 등). 오류 사유를 반환한다.
   - 화면 종류가 바뀜 (전투 → 보상 등)
   - 새 선택 요청이 생김 (카드 효과로 "1장 선택" 등)
   - 전투 종료, 사망
4. 반환: 행동별 이벤트 줄 + 마지막 화면의 brief 렌더 + (멈췄다면) 멈춘 이유.

## 6. 안정 판정 (Settle)

**전투 중 — 플레이어 입력 대기 상태**
```
CombatManager.IsInProgress && !IsOverOrEnding
&& combatState.CurrentSide == Player
&& player.PlayerCombatState.Phase == PlayerTurnPhase.Play
&& ActionQueueSynchronizer.CombatState == PlayPhase
&& !CombatManager.PlayerActionsDisabled
&& !ActionExecutor.IsRunning && ActionQueueSet.IsEmpty
```
이 조건이 연속 2프레임 유지되면 안정. 단 선택 요청이 열려 있으면(ChoiceBroker 대기 중 또는 선택 화면 열림) 즉시 "선택 대기"로 반환한다.

**턴 종료**: 위 조건 + 플레이어 쪽 `TurnStarted`가 새로 발생(턴 번호 증가)할 때까지. 또는 전투 종료/사망.

**전투 밖**: `ScreenProbe`의 화면 식별값이 N프레임(기본 3) 동일 && 액션 실행기 유휴 && 화면 전환(페이드) 중 아님.

**시간 제한**: 기본 15초(턴 종료 30초). 초과 시 `timeout` + 진단 정보.

근거: 턴 종료와 적 턴은 액션 큐 밖(`TaskHelper.RunSafely`)에서 진행되므로 `ActionExecutor.IsRunning`만으로는 판정할 수 없다.

## 7. 선택 요청 (Choices)

- **카드 선택**: `CardSelectCmd.PushSelector(ChoiceBroker)`로 우리 선택자를 등록한다.
  게임이 `GetSelectedCards(options, min, max)`를 호출하면 대기 상태가 되고, 화면은 `choice`로 표시된다. `choose`/`skip`으로 응답하면 게임이 이어서 진행한다.
  - 대상: 손패/더미에서 선택, 버리기·소멸, 덱 강화·변화·제거·인챈트, 카드 중 1장 고르기(포션 등).
  - `RunManager.CleanUp()`이 선택자 스택을 비우므로 `RunStarted`마다 다시 등록한다.
  - 다른 선택자(예: WhisperingEarring)가 위에 쌓이면 그쪽이 처리하도록 둔다.
  - 선택자가 처리하는 동안 게임 UI에는 선택 화면이 뜨지 않는다.
- **선택자가 처리하지 않는 것**: 카드 보상, 유물 선택, 묶음 선택, 이벤트·휴식·상점·맵·크리스탈 구체.
  이것들은 화면 처리기가 UI 노드를 직접 눌러서 처리한다 (`NClickableControl.ForceClick`).

## 8. 이벤트 수집 (Events)

- 전투 시작(`CombatSetUp`) 때 `CombatManager.History.Changed`를 구독하고, 읽은 위치를 기록해 둔다. 기록이 비워지면 위치를 0으로 돌린다.
- 새 기록이 들어오면 **그 즉시** 짧은 텍스트로 바꿔 둔다 (기록은 살아 있는 게임 객체를 참조하므로 나중에 읽으면 값이 바뀌어 있을 수 있음).
- 기록에 없는 것은 크리처/플레이어 이벤트로 보충한다: 파워 제거, 회복, 사망, 방어도 소멸, 골드, 유물 획득.
- `act`의 행동 하나가 시작될 때 위치를 표시하고, 끝나면 그 사이의 줄을 반환한다.
- 적 의도 변화는 기록되지 않으므로, 최종 상태 렌더에서 항상 새로 읽는다.
- 전투 밖 이벤트(보상, 구매, 덱 변화)는 P1에서 `ModHelper.SubscribeForRunStateHooks`로 받는다.

출력 예:
```
> play c3 @e1
  타격 → 압축벌레 6 (39→33)
> end
  압축벌레: 공격 7 → 나 (방어 5 소모, HP 64→62)
  뽑기: 수비, 타격, 강타, 타격, 수비
```

## 9. 화면 처리기

```csharp
interface IScreenHandler
{
    string Kind { get; }                          // "combat", "choice", "card_reward", ...
    bool Matches(ScreenContext ctx);              // ScreenProbe 결과로 판단
    ScreenView Render(ScreenContext ctx, Detail detail);   // 상태 + 가능한 행동
    ActionResult Execute(ScreenContext ctx, ActionSpec action);  // 메인 스레드에서 실행 시작
    SettleKind SettleFor(ActionSpec action);      // 어떤 안정 판정을 쓸지
}
```

- `ScreenRegistry`는 등록 순서대로 `Matches`를 검사한다. 선택 요청(`ChoiceHandler`)이 가장 먼저다.
- `ScreenProbe`는 게임의 `ActiveScreenContext.Instance.GetCurrentScreen()`을 기본으로 쓰고, 방 정보(`RunState.CurrentRoom`)와 전투 여부를 함께 담는다.
- **범용 처리기(`GenericHandler`)**: 맞는 처리기가 없으면 현재 화면 노드 아래의 보이고 활성화된 `NClickableControl`을 화면 위치 순으로 나열하고, 각 버튼의 텍스트를 함께 보여준다. `click N`으로 누른다. 새 모드·새 화면에서도 진행이 막히지 않게 하는 안전장치다.
- 상태 조회(`Render`)는 화면을 바꾸지 않는다 (기존 모드처럼 조회 시 상점·보물상자를 여는 일이 없게 한다). 화면을 여는 동작이 필요하면 행동으로 노출한다.

P0 처리기: `popup`, `main_menu`, `run_lobby`, `game_over`, `combat`, `choice`, `card_reward`, `rewards`, `map`, `generic`.
P1 처리기: `event`(ancient 포함), `rest`, `shop`(가짜 상인 포함), `treasure`, `relic_select`, `bundle_select`, `crystal_sphere`, 타임라인 해금 화면.

### 9.1 메뉴와 런 시작

게임의 런 시작 화면은 모드마다 다르지만(일반: `NCharacterSelectScreen`, 일일: `NDailyRunScreen`, 커스텀: `NCustomRunScreen`) 셋 모두 내부적으로 같은 **`StartRunLobby`** 를 만들어 쓴다.
로비에는 화면 클릭 없이 쓸 수 있는 API가 있다: `SetLocalCharacter`, `SyncAscensionChange`, `SetSeed`, `SetModifiers`, `SetReady(true)`.
`SetReady(true)`를 부르면 게임이 자기 출발 절차(막 선택, 전환 연출, 저장 설정)를 그대로 밟는다. 그래서 런 생성 로직을 우리가 다시 구현하지 않는다.

**처리기 구성**
- `popup` (가장 먼저 검사): 모달, 확인 팝업, 튜토리얼(FTUE). 문구와 버튼을 보여주고 `choose N`으로 누른다. 설정으로 튜토리얼 자동 닫기를 켤 수 있게 한다.
- `main_menu`: 메인 메뉴와 하위 메뉴(싱글플레이 → 모드 선택). 세이브된 런이 있으면 `continue`, `abandon`을 보여준다.
  하위 메뉴의 모드 버튼은 하드코딩하지 않고 화면에 있는 버튼 목록에서 읽는다 → 새 모드 버튼도 그대로 나타난다.
- `run_lobby`: 현재 화면이 `StartRunLobby`를 가진 화면이면 모드와 관계없이 이 처리기가 맡는다.
  보여주는 것: 게임 모드(`GameMode` 이름), 고를 수 있는 캐릭터(해금 여부), 승천(현재/최대), 시드, 수정자.
  행동: `character <ID>`, `ascension <N>`, `seed <S>`, `embark`. 모드가 정한 값(일일 런의 캐릭터·수정자 등)은 바꿀 수 없다고 표시한다.
- `game_over`: 결과(승리/패배, 층, 점수 요약)와 `continue`(메인 메뉴로). 이후 타임라인 해금 화면은 P0에서는 범용 처리기로 넘긴다.

**편의 행동**: 메인 메뉴에서 `new_run <캐릭터> [asc=N] [seed=S] [mode=standard]` 한 줄로
"싱글플레이 → 모드 → 로비 설정 → 출발 → 첫 화면 안정"까지 진행한다. 내부적으로는 위 처리기들의 행동을 차례로 실행하는 것뿐이라 별도 경로가 없다.
`mode`에 모르는 값이 오면 실행하지 않고 화면에 있는 모드 버튼 목록을 반환한다 → 에이전트가 직접 `choose`로 들어가면 된다.

**새 게임 모드 대비**: 새 모드가 같은 `StartRunLobby` 방식이면 처리기 수정 없이 동작한다. 별도 설정 화면이 붙어 있으면 그 화면은 범용 처리기로 진행하고, 필요할 때 처리기를 추가한다.

## 10. 상태 모델과 렌더

### 카드 (계산에 필요한 정보)
- 이름, 비용(보정 후, X 비용 표시), 별 비용, 종류, 키워드, 강화 여부, 인챈트·고통
- 게임 문구 (현재 수치가 들어간 것)
- 대상별 수치: 대상 카드는 적마다 `UpdateDynamicVarPreview(Normal, target)`을 돌려 DynamicVar 값을 비교하고, 적마다 값이 다르면 `→ e1 6 | e2 9`처럼 표시한다. 같으면 생략.
  호출 후에는 화면 표시용 미리보기가 원래대로 돌아가도록 정리한다(구현 시 확인 필요).
- 낼 수 있는지 여부와 이유 (`CanPlay`)

### 크리처
- HP/최대 HP, 방어도, 파워(이름·수치·게임 문구), 적은 의도(종류, 최종 피해량 × 타수)
- 펫(Osty 등)은 플레이어 아래에 크리처로 표시한다.

### 캐릭터 자원 (확장 지점)
```csharp
interface IResourceProvider { bool Applies(PlayerCtx p); IEnumerable<ResourceView> Read(PlayerCtx p); }
```
- 기본 제공: 에너지, 별, 오브(슬롯·패시브·발동 값), 펫.
- **대체 경로**: `PlayerCombatState`의 공개 int/bool 속성 중 어떤 제공자도 다루지 않은 것을 반사로 읽어 `extras`에 이름=값으로 싣는다. 새 캐릭터의 새 자원이 의미는 없더라도 값은 바로 보이게 한다.

### 런 정보
- 게임 모드(`GameMode` 이름 문자열), 수정자(ModelId + 이름), 승천, 막, 층, 골드, 포션, 유물(이름 + 카운터만, 설명은 `info`)

### 텍스트 원칙
- 게임 문구를 그대로 쓴다 (서식 태그 제거). 식별자는 언어와 무관한 ModelId(`CARD.STRIKE_IRONCLAD`).
- 열거형은 이름 문자열로. 모르는 값도 그대로 통과시킨다.
- brief: 매 응답에 필요한 것만 (손패 문구와 파워는 항상 포함, 유물·키워드 설명은 제외).
  full: 덱 전체, 더미 내용, 유물 설명까지.
- 응답은 사람이 읽을 수 있는 짧은 텍스트. JSON 들여쓰기·이스케이프 비용을 피한다.

전투 brief 예:
```
[combat r2 · player turn] 아이언클래드 HP 64/80 · 방어 0 · 에너지 3/3 · 골드 112
유물: 불타는 혈액, 약병 홀스터 | 포션: p1 에너지 포션
손패:
 c3 타격 1E 공격 — 피해를 6 줍니다.
 c5 강타 2E 공격 — 피해를 8 줍니다. 취약을 2 부여합니다.  → e1 8 | e2 12
 c9 등반자의 골칫거리 — 사용불가
더미: 뽑기 7 · 버림 2 · 소멸 0
적:
 e1 압축벌레 7/39 방어 0 · 의도 공격 7
 e2 ... [취약 2]
나: [압축] 압축벌레가 살아있는 동안 공격 피해 30% 감소
행동: play c<id> [@e<n>] · potion p<n> [@e<n>] · end
```

## 11. 게임 업데이트 대비 (Features / Probes)

- 게임 내부 호출은 `Game/` 아래 기능 단위 메서드로만 한다. 시작할 때 각 기능을 한 번 시험 호출해서 `MissingMethodException`/`TypeLoadException`/`MissingFieldException`이 나면 그 기능만 꺼진 것으로 표시한다.
  (.NET은 메서드를 처음 실행할 때 참조를 확인하므로, 기능 단위로 메서드를 나눠 두면 깨진 부분만 격리된다.)
- `state`의 머리말에 꺼진 기능이 있으면 표시한다 (예: `disabled: choice-broker`).
- 게임 버전(`release_info.json`)과 기준 버전이 다르면 경고만 하고 계속 동작한다.
- 하드코딩 목록을 쓰지 않는다: 모델은 `ModelDb`에서, 모드는 `GameMode` 이름으로, 화면은 `ActiveScreenContext`로 찾는다.

### 확장 시나리오
| 업데이트 내용 | 코드 수정 없이 되는 것 | 손대야 할 수 있는 것 |
|---|---|---|
| 새 캐릭터 | 카드·유물·파워 (ModelDb), 파워·펫 기반 메커니즘, 새 자원의 원시 값(`extras`) | 새 자원의 의미 있는 표시 → `IResourceProvider` 하나 추가 |
| 새 게임 모드 | 모드 이름, 수정자 표시, 처리기 없는 새 화면은 범용 처리기로 진행 | 새 화면을 깔끔하게 다루려면 `IScreenHandler` 하나 추가. 런 시작 방식이 다르면 `run` 도구에 분기 |
| 내부 API 변경 | 깨진 기능만 꺼지고 나머지는 동작 | `Game/`의 해당 기능 메서드 수정 |

## 12. P0 범위와 완료 기준

**범위**: Host(MCP·인증·메인 스레드), Settle, ChoiceBroker, EventLog(전투), ScreenProbe, Features, 처리기 10종(popup, main_menu, run_lobby, game_over, combat, choice, card_reward, rewards, map, generic), 도구 3종(state, act, info).

**완료 기준** (실제 게임에서 확인)
1. Claude Code에서 `type: http`로 연결되고 토큰 없는 요청·`Origin` 있는 요청은 거부된다.
1-1. 메인 메뉴에서 `new_run IRONCLAD asc=0` 한 번으로 니오우 화면까지 도착한다. 세이브된 런이 있을 때 `continue`/`abandon`이 동작한다.
1-2. 사망 또는 승리 → `game_over` → 메인 메뉴로 돌아와 다음 런을 시작할 수 있다.
2. 전투 한 턴을 `act(["play c3 @e1", "play c5 @e1", "end"])` 한 번으로 진행하고, 응답에 이벤트와 다음 턴 손패가 들어 있다. 반복 조회 없이.
3. 카드 효과의 선택 요청(예: 소멸할 카드 선택)이 `choice` 화면으로 뜨고 `choose`로 이어진다.
4. 전투 승리 → 보상 → 카드 보상 → 맵 이동까지 `act`로 진행된다.
5. 처리기가 없는 화면(이벤트 등)이 범용 처리기의 `click N`으로 진행된다.
6. 같은 전투 상황에서 기존 모드 대비 호출 수와 응답 크기를 측정해 기록한다.

**시험 방법**: 개발자 콘솔 명령(`fight`, `card`, `relic`)으로 원하는 전투와 카드를 바로 만들어 시험한다. 측정용 테스트 프로필 사용.

## 12-1. P0 검증 결과 (2026-11-16, v0.107.1 실제 게임)

| 기준 | 결과 |
|---|---|
| 1. 연결·인증 | 확인. 토큰 없음/틀림 401, `Origin` 있음 403, GET 405, 정상 200 |
| 1-1. 메뉴·런 시작 | 확인. `new_run IRONCLAD asc=0` 한 번으로 니오우 도착(3.9초), 승천 반영. `continue`, `abandon_run` → 확인 팝업 → 메인 메뉴 |
| 1-2. 게임 오버 | **미확인** (사망·승리까지 진행하지 않음). 처리기는 버튼 방식이라 위험은 낮음 |
| 2. 한 턴을 act 한 번으로 | 확인. 카드·포션·턴 종료·적 턴·다음 손패까지 한 응답, Instant 기준 턴당 약 1.2초 |
| 3. 카드 효과의 선택 요청 | 확인. 전투 중(전투장비 FromHandForUpgrade), 전투 밖(이벤트 덱 강화 FromDeckForUpgrade) 모두 `choice`로 표시. `["play c9", "choose 0"]`처럼 이어서 응답 가능 |
| 4. 승리 → 보상 → 카드 보상 → 맵 | 확인. 전투 종료 후 보상 화면이 뜰 때까지 대기 |
| 5. 범용 처리기 | 확인. 니오우, 사파이어 씨앗 이벤트를 `click`으로 진행 |
| 6. 측정 | 같은 날 같은 게임 기준. 기존 모드: 2턴 전투에 도구 호출 19회(상태 조회 1회당 markdown 1.5천 자 / json 5.5천 자). 이 모드: 진입+3턴 전투에 호출 4회, 응답 합계 2,740자 |

구현 중 발견해서 반영한 것:
- 게임은 모드 초기화 **후에** 설정을 읽고 Instant를 Fast로 되돌린다 → 메인 메뉴가 뜬 뒤 적용.
- 전투가 끝나는 중(`IsEnding`)의 피해는 기록되지 않는다 → 마지막 일격 대신 `victory` 줄.
- 전투 종료 직후 보상 화면이 뜨기 전 전투방이 잠시 남는다 → 최대 10초 대기.
- 게임 카드 이름에 강화 표시(`+`)가 이미 들어 있다.
- 적이 하나면 그 적을 대상으로 한 수치를 손패에 표시(취약·힘·약화 반영 확인).

## 12-2. P1 진행 (2026-11-16)

| 항목 | 상태 |
|---|---|
| 게임 오버 화면 | 확인. `abandon_run` → 게임 오버 → 계속 → `main_menu` → 메인 메뉴 |
| 런 중 메인 메뉴로 (`quit_to_menu`) | 확인. 이어하기와 3회 반복, 게임 오류 0건 |
| 이벤트 처리기 (고대인 포함) | 확인. 니오우, 나선형 소용돌이(인챈트 선택 → `choice`) |
| 휴식처 처리기 | 확인. 휴식, 재련 → 선택 → `cancel` |
| 상점 처리기 | 확인. 열기 없이 `buy` → 자동으로 열기, 카드 제거 → 선택, 포션 구매, `leave` (한 번의 호출로 전체 가능) |
| 보물방 처리기 | 확인. 상자 열기 → 유물 → 진행 |
| 크리스탈 구체, 묶음 선택, 가짜 상인 | 구현만. 해당 화면을 만나지 못해 미검증 |
| 전투 밖 포션 사용 | 확인 (맵에서 피 포션) |
| 이벤트 기록 | 적 행동은 의도 문구 + 내부 id, 적이 준 피해에 `by e1`, 전투 밖 HP 변화 |

구현하면서 발견해서 반영한 것:
- **전투 첫 턴 판정**: 게임은 손패를 다 뽑기 전에 플레이어 단계를 `Play`로 바꾼다. `CombatManager.TurnStarted`(턴 준비가 모두 끝난 뒤 발생)를 기록해 판정 조건에 넣었다 (`TurnTracker`).
- **불러오기 중 판정**: 런을 불러오는 동안 현재 화면은 메인 메뉴이거나 없음(null)이다. 둘 다 전환 중으로 본다.
- **방 페이드인**: `NTransition.InTransition`은 페이드인 0.2초에 꺼지지만 연출과 게임의 불러오기 작업은 0.8초까지 이어진다. 연출(tween)이 도는 동안도 전환 중으로 본다. 이걸 놓치면 불러오기 도중 `quit_to_menu`가 게임의 `LoadRun`을 NullReference로 깨뜨리고 "세이브 손상" 팝업이 뜬다(세이브 파일 자체는 멀쩡).
- **연출만 있는 화면**: 휴식처는 선택 후 VFX가 끝나야 진행 버튼이 켜진다. 처리기가 추가 완료 조건(`ExecResult.Until`)을 줄 수 있게 했다.
- **사망 후**: 게임 오버 화면이 떠도 전투는 형식상 진행 중이다. "플레이어 턴까지 대기" 규칙은 전투방 화면에서만 적용.
- **자동 저장**: 방 진입 시점 기준이라, 재시작하면 그 방 안에서 한 일(상점 구매 등)은 되돌아간다.

연대표(에포크) 처리기 — 결정: 해금은 이름 붙은 행동 하나(`reveal_epochs`)로만 수행한다.
- 이유: 해금은 잃는 것이 아니라 정상 진행이지만, 범용 처리기로 연대표를 눌러 다니면 연출 중 입력 차단·상세 화면·해금 화면이 얽혀 막히기 쉽다(기존 모드도 이 화면에서 오류 기록).
- `reveal_epochs`는 한 단계씩 수행하고 자신을 다시 예약한다: 해금 화면 확인 → 상세 화면 닫기 → 연출 대기 → 얻은 에포크 칸 클릭 → (반복) → 뒤로 가기로 메인 메뉴.
- 다른 연대표 버튼은 노출하지 않는다. 공개 대기 에포크가 없으면 `back`만 보인다.
- 확인: 연대표 진입·인식·`back`. **미확인**: 실제 공개 과정(에포크를 얻어야 시험 가능).
- Claude Code의 HTTP MCP 연결로 직접 호출 확인 (게임 재시작 후에도 재연결 없이 동작).

남은 것:
- 크리스탈 구체, 묶음 선택, 가짜 상인, `reveal_epochs` 실검증
- 인챈트·강화처럼 덱 카드가 바뀌는 이벤트 기록 (지금은 추가/제거만 기록)

## 13. 확인이 필요한 것 (구현 중 검증)

- `UpdateDynamicVarPreview`를 대상별로 호출했을 때 화면 표시에 부작용이 없는지, 원상 복구 방법.
- `ChoiceBroker`가 전투 밖 선택(휴식처 강화, 이벤트 변화)에서도 호출되는지.
- `FastMode = Instant`에서 남는 애니메이션이 안정 판정에 주는 영향.
- 범용 처리기의 버튼 텍스트 추출이 대부분의 화면에서 의미 있게 나오는지.
- MCP 클라이언트(Claude Code)가 단건 JSON 응답만으로 문제없이 동작하는지, 긴 대기(턴 종료 최대 30초) 중 시간 초과가 없는지.
- 로비 API로 캐릭터·승천을 바꿨을 때 화면 표시(승천 패널 등)가 따라오는지. 안 따라오면 해당 UI 컨트롤을 통해 바꾼다.
- 튜토리얼 표시를 끄는 게임 설정이 있는지 (있으면 팝업 자동 닫기 대신 사용).
- 일일 런은 시간 서버 응답이 필요하다. 오프라인일 때의 동작.

## 14. 지식 베이스 (kb)

게임 문구에 나오지 않는 몬스터·이벤트 동작(정확한 수치, 승천별 변화, 행동 패턴, 선택지 결과)은 런타임 코드 대신 문서로 관리한다.

- 기준: 로컬에서 디컴파일한 게임 코드(저장소 밖, 공개하지 않음). 문서에는 코드를 넣지 않고 동작만 요약한다.
- 위치: `kb/monsters/<ENTRY>.md`, `kb/events/<ENTRY>.md`. `ENTRY`는 게임 `ModelId.Entry`와 같다. v0.111.1 기준 몬스터 121개, 이벤트 68개(v0.107.1에서 쓰고 v0.111.1 변경을 반영).
- 머리말: `game_version`과 근거 원본 파일 목록(`sources`, 파일별 sha256). `info`는 게임 버전이 다르면 문서 앞에 안내를 붙인다.
- 업데이트: 새 버전을 디컴파일 → `kb-check`가 원본이 바뀐 문서와 문서가 없는 몬스터·이벤트를 나열 → 원본 diff를 보고 고쳐 씀 → `kb-glossary`로 게임 용어 확인 → `kb-stamp`로 해시 기록. 형식과 작성 규칙은 `kb/README.md`.
- 문서 안의 이름은 게임 현지화 문구와 같아야 한다. `kb-glossary`가 문서별 용어 목록을 만들고, 게임 이름과 다른 이름을 보고한다.

## 15. 게임 안 채팅창 (개발 중)

- 화면 왼쪽에 에이전트 채팅창을 두고 게임은 나머지 영역에 전체 높이로 그린다. 게임은 그 영역의 비율에 맞춰 UI를 다시 배치한다. 모드가 Claude Code를 직접 실행하고, Claude Code는 이 모드의 MCP 도구로 게임을 본다.
- 1차 구현 완료(배치, 입력 보정, 메신저 패널, Claude Code 연결). 구현 방식, 버린 방법, 작업 계획: [docs/in-game-chat.md](docs/in-game-chat.md)
- 이 기능은 Harmony 패치를 쓴다(`Viewport.GetMousePosition`, 게임의 화면 비율 처리기 두 곳, `NGame.ApplyDisplaySettings`). 설정 `chat_panel`이 꺼져 있으면 패치하지 않는다.

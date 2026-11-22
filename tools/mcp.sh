#!/usr/bin/env bash
# Calls a mod's MCP endpoint directly on macOS (tools/mcp.ps1 on Windows).
#
#   tools/mcp.sh init | list | state [full]          # STS2AgentMcp, /mcp (outside clients)
#   tools/mcp.sh act "play c3 @e1" "end"
#   tools/mcp.sh info BASH CARD.STRIKE_IRONCLAD
#   tools/mcp.sh call screenshot '{}'
#   tools/mcp.sh --dev call dev_layout '{"action":"auth"}'   # STS2AgentChat, /mcp/dev (development)
#   tools/mcp.sh --chat list                                  # STS2AgentChat, /mcp/chat (the chat's agents)
set -euo pipefail

game_dir="${STS2_GAME_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
mods="$game_dir/SlayTheSpire2.app/Contents/MacOS/mods"
conf="$mods/STS2AgentMcp.conf"; path="/mcp"
case "${1:-}" in
    --dev) conf="$mods/STS2AgentChat.conf"; path="/mcp/dev"; shift ;;
    --chat) conf="$mods/STS2AgentChat.conf"; path="/mcp/chat"; shift ;;
esac
[ $# -ge 1 ] || { echo "usage: $0 [--dev|--chat] init|list|state|act|info|call ..." >&2; exit 2; }

exec python3 - "$conf" "$path" "$@" <<'PY'
import json, sys, urllib.request

conf_path, path, command, rest = sys.argv[1], sys.argv[2], sys.argv[3], sys.argv[4:]
conf = json.load(open(conf_path, encoding="utf-8"))

if command == "init":
    method, params = "initialize", {"protocolVersion": "2025-06-18", "capabilities": {}, "clientInfo": {"name": "mcp.sh", "version": "1"}}
elif command == "list":
    method, params = "tools/list", {}
elif command == "state":
    method, params = "tools/call", {"name": "state", "arguments": {"detail": "full" if rest[:1] == ["full"] else "brief"}}
elif command == "act":
    method, params = "tools/call", {"name": "act", "arguments": {"actions": rest}}
elif command == "info":
    method, params = "tools/call", {"name": "info", "arguments": {"ids": rest}}
elif command == "call":
    method, params = "tools/call", {"name": rest[0], "arguments": json.loads(rest[1]) if len(rest) > 1 else {}}
else:
    sys.exit(f"unknown command {command}")

body = json.dumps({"jsonrpc": "2.0", "id": 1, "method": method, "params": params}).encode("utf-8")
request = urllib.request.Request(f"http://127.0.0.1:{conf['port']}{path}", data=body, method="POST",
                                 headers={"Content-Type": "application/json", "Authorization": f"Bearer {conf['token']}"})
response = json.load(urllib.request.urlopen(request, timeout=120))

if "error" in response:
    sys.exit(f"ERROR {response['error'].get('code')}: {response['error'].get('message')}")
result = response["result"]
if method != "tools/call":
    print(json.dumps(result, indent=2, ensure_ascii=False))
    sys.exit()
if result.get("isError"):
    print("(isError)")
for content in result.get("content", []):
    if content.get("type") == "image":
        print(f"[image {content.get('mimeType')}, {len(content.get('data', ''))} base64 chars]")
    else:
        print(content.get("text", ""))
PY

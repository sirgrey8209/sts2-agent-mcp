#!/usr/bin/env bash
# Development loop on macOS (tools/redeploy.ps1 on Windows): build, quit the game, install the mods,
# relaunch through Steam, wait until the mods are ready.
#
#   tools/redeploy.sh                 # both mods (STS2AgentMcp and STS2AgentChat)
#   tools/redeploy.sh mcp             # external MCP only (the chat mod is taken out of the mods folder)
#   tools/redeploy.sh chat            # in-game chat only
#   STS2_GAME_DIR="/path/to/Slay the Spire 2" tools/redeploy.sh
#   TIMEOUT_SEC=300 tools/redeploy.sh
#
# A mod left out is removed from the mods folder except its .conf (its token and settings stay).
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
game_dir="${STS2_GAME_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/Slay the Spire 2}"
app="$game_dir/SlayTheSpire2.app"
data_dir="$app/Contents/Resources/data_sts2_macos_$( [ "$(uname -m)" = arm64 ] && echo arm64 || echo x86_64 )"
mods="$app/Contents/MacOS/mods"
log="$HOME/Library/Application Support/SlayTheSpire2/logs/godot.log"
timeout="${TIMEOUT_SEC:-180}"
dotnet="$(command -v dotnet || echo "$HOME/.dotnet/dotnet")"

want_mcp=1; want_chat=1
if [ $# -gt 0 ]; then
    want_mcp=0; want_chat=0
    for arg in "$@"; do
        case "$arg" in
            mcp) want_mcp=1 ;;
            chat) want_chat=1 ;;
            *) echo "usage: $0 [mcp] [chat]" >&2; exit 2 ;;
        esac
    done
fi

if [ ! -f "$data_dir/sts2.dll" ]; then
    echo "ERROR: sts2.dll not found under '$data_dir'." >&2
    exit 1
fi

[ $want_mcp = 1 ] && "$dotnet" build "$root/src/External/STS2AgentMcp.csproj" -c Release -o "$root/out" -nologo -v q "-p:STS2GameDataDir=$data_dir"
[ $want_chat = 1 ] && "$dotnet" build "$root/src/Chat/STS2AgentChat.csproj" -c Release -o "$root/out" -nologo -v q "-p:STS2GameDataDir=$data_dir"

# Quit through the app (the game's own quit). Not SIGTERM: the .NET runtime then calls exit() from another thread
# while Godot still runs, which aborts and leaves a crash report ("quit unexpectedly"). SIGKILL only as a fallback.
game_running() { pgrep -f "SlayTheSpire2.app/Contents/MacOS/" >/dev/null; }
if game_running; then
    osascript -e 'tell application "Slay the Spire 2" to quit' >/dev/null 2>&1 || true
    for _ in $(seq 1 20); do game_running || break; sleep 1; done
    if game_running; then
        echo "game did not quit; killing it" >&2
        pkill -KILL -f "SlayTheSpire2.app/Contents/MacOS/" || true
        while game_running; do sleep 1; done
    fi
    sleep 2
fi

mkdir -p "$mods"
# install <id> <project dir>: the DLL, its manifest, and the knowledge base it serves through `info`.
install_mod() {
    cp "$root/out/$1.dll" "$mods/"
    cp "$root/src/$2/mod_manifest.json" "$mods/$1.json"
    if [ -d "$root/kb" ]; then rsync -a --delete "$root/kb/" "$mods/$1-kb/"; fi
}
remove_mod() {
    rm -f "$mods/$1.dll" "$mods/$1.json"
    rm -rf "$mods/$1-kb"
}
if [ $want_mcp = 1 ]; then install_mod STS2AgentMcp External; else remove_mod STS2AgentMcp; fi
if [ $want_chat = 1 ]; then install_mod STS2AgentChat Chat; else remove_mod STS2AgentChat; fi

# The game starts a new godot.log on every launch (the old one is renamed): wait for a new file, not new lines.
old_log=$( [ -f "$log" ] && stat -f %i "$log" || echo none )
launched=$(date +%s)
open "steam://rungameid/2868840"
while [ $(( $(date +%s) - launched )) -lt "$timeout" ]; do
    sleep 3
    if ! game_running && [ $(( $(date +%s) - launched )) -ge 60 ]; then
        echo "ERROR: the game did not start within 60s. Check Steam (logged in? a dialog waiting?)." >&2
        exit 1
    fi
    [ -f "$log" ] || continue
    [ "$(stat -f %i "$log")" != "$old_log" ] || continue
    ready=1
    [ $want_mcp = 1 ] && ! grep -q "\[STS2 Agent MCP\] ready" "$log" && ready=0
    [ $want_chat = 1 ] && ! grep -q "\[STS2 Agent Chat\] ready" "$log" && ready=0
    if [ $ready = 1 ] && grep -q "Time to main menu" "$log"; then break; fi
done
grep -E "STS2 Agent (MCP|Chat)\]" "$log" || true

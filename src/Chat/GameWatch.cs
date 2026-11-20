using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Chat;

/// <summary>Something that happened in the game, for the chat (docs/chat-roadmap.md steps 5 and 6).</summary>
/// <param name="Kind">game_over, combat_start, combat_end, turn, room, deck, log (other event lines outside combat:
/// gold, HP, potions...), view.</param>
internal sealed record GameSignal(string Kind, string Text);

/// <summary>
/// Watches the game for the chat a few times a second (main thread): which run is going on (its seed is the run's
/// key: a play session per run), and the moments the chat reacts to or reports to the agent: a turn starting after
/// the player ended one, a fight starting or ending, a new room, deck and relic changes, the game over screen, and
/// the views the player opens (deck, piles, map, card or relic details).
/// </summary>
internal static class GameWatch
{
    private static string? _runKey;
    private static bool _inCombat;
    private static int _round = -1;
    private static string? _coord;
    private static string? _view;
    private static bool _gameOver;
    private static long _logMark = -1;
    private static DateTime _ignoreViewsUntil;

    /// <summary>The current run's key (its seed), or null outside a run. Main thread.</summary>
    public static string? RunKey => _runKey;

    /// <summary>Raised on the main thread: (previous run key, new run key).</summary>
    public static event Action<string?, string?>? RunChanged;
    /// <summary>Raised on the main thread.</summary>
    public static event Action<GameSignal>? Signal;

    public static void Install() => MainThread.Every(TimeSpan.FromMilliseconds(400), Tick);

    /// <summary>A view the agent opens itself (open_view) is not reported back as the player's.</summary>
    public static void IgnoreViewsFor(TimeSpan time) => _ignoreViewsUntil = DateTime.UtcNow + time;

    private static void Tick()
    {
        var rm = RunManager.Instance;
        RunState? run = rm != null && rm.IsInProgress ? rm.DebugOnlyGetState() : null;
        string? key = run != null ? Safe(() => run.Rng.StringSeed) : null;
        if (key != _runKey)
        {
            string? old = _runKey;
            _runKey = key;
            _inCombat = false;
            _round = -1;
            _coord = null;
            _view = null;
            _gameOver = false;
            _logMark = EventLog.Mark(flush: false);
            RunChanged?.Invoke(old, key);
        }
        if (run == null) return;

        if (!_gameOver && ActiveScreenContext.Instance.GetCurrentScreen() is NGameOverScreen)
        {
            _gameOver = true;
            Raise("game_over", "런 종료");
            return;
        }

        bool inCombat = CombatManager.Instance is { IsInProgress: true };
        if (inCombat && !_inCombat) Raise("combat_start", "전투 시작");
        if (!inCombat && _inCombat) Raise("combat_end", "전투 끝");
        _inCombat = inCombat;

        int round = inCombat ? TurnTracker.PlayerTurnStartedRound : -1;
        if (round > 1 && round != _round) Raise("turn", $"{round}턴 시작");
        _round = round;

        string? coord = Safe(() => run.CurrentMapCoord is { } c ? $"r{c.row}c{c.col}" : null);
        if (coord != null && coord != _coord && _coord != null)
            Raise("room", $"{RoomName(run)} 도착 ({Safe(() => run.TotalFloor.ToString())}층)");
        _coord = coord ?? _coord;

        // Deck and relic changes, from the event log (card rewards, removals, upgrades, relics). Peek only: no flush.
        if (EventLog.Mark(flush: false) > _logMark)
        {
            foreach (var line in EventLog.Since(_logMark, flush: false))
            {
                if (line.StartsWith("deck ") || line.StartsWith("relic ")) Raise("deck", line);
                else if (!inCombat) Raise("log", line);
            }
            _logMark = EventLog.Mark(flush: false);
        }

        string? view = CurrentView();
        if (view != _view && view != null && DateTime.UtcNow >= _ignoreViewsUntil) Raise("view", view);
        _view = view;
    }

    private static void Raise(string kind, string text)
    {
        try { Signal?.Invoke(new GameSignal(kind, text)); }
        catch (Exception ex) { Log.Error($"chat: signal handler failed: {ex}"); }
    }

    private static string RoomName(RunState run) => run.CurrentRoom?.GetType().Name switch
    {
        "CombatRoom" => "전투",
        "EventRoom" => "이벤트",
        "MerchantRoom" => "상점",
        "RestSiteRoom" => "휴식처",
        "TreasureRoom" => "보물",
        { } other => other,
        null => "방",
    };

    /// <summary>The view the player has open on top of the game, in words (null when none).</summary>
    private static string? CurrentView()
    {
        try
        {
            switch (NCapstoneContainer.Instance?.CurrentCapstoneScreen)
            {
                case NDeckViewScreen:
                    return "덱 보기";
                case NCardPileScreen pile:
                    return pile.Pile.Type switch
                    {
                        PileType.Draw => "뽑을 더미 보기",
                        PileType.Discard => "버린 더미 보기",
                        PileType.Exhaust => "소멸 더미 보기",
                        _ => "카드 더미 보기",
                    };
            }
            // The properties, not GetInspect*Screen(): those create the screen when it does not exist yet, and a
            // created but never opened screen shows its scene's sample content over the game (seen 2026-11-19).
            var inspectCard = NGame.Instance?.InspectCardScreen;
            if (inspectCard != null && Ui.IsVisible(inspectCard))
            {
                var cards = Ui.GetField(inspectCard, "_cards") as IList<CardModel>;
                int index = Ui.GetField(inspectCard, "_index") is int i ? i : -1;
                return cards != null && index >= 0 && index < cards.Count
                    ? "카드 살펴보기: " + View.Fmt.CardLabel(cards[index])
                    : "카드 살펴보기";
            }
            var inspectRelic = NGame.Instance?.InspectRelicScreen;
            if (inspectRelic != null && Ui.IsVisible(inspectRelic))
            {
                var relic = Ui.GetField(inspectRelic, "_relic") as RelicModel;
                return relic != null ? "유물 살펴보기: " + Text.Of(() => relic.Title) : "유물 살펴보기";
            }
            if (NMapScreen.Instance is { IsOpen: true }) return "지도 보기";
        }
        catch (Exception ex)
        {
            Log.Warn($"chat: view check failed: {ex.Message}");
        }
        return null;
    }

    private static T? Safe<T>(Func<T?> get)
    {
        try { return get(); } catch { return default; }
    }
}

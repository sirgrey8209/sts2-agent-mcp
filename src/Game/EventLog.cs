using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>
/// Turns the game's combat history into short event lines as entries arrive (entries reference live objects,
/// so they are formatted immediately). Callers take a mark before an action and read the lines added since.
/// </summary>
internal static class EventLog
{
    private const int MaxLines = 2000;

    private static readonly List<string> Lines = new();
    private static long _dropped; // lines trimmed from the front
    private static int _historyCount;
    private static string? _pendingDraws;

    public static void Install()
    {
        CombatManager.Instance.History.Changed += OnHistoryChanged;
        // The killing blow is not recorded in history (the game skips entries while combat is ending).
        CombatManager.Instance.CombatWon += _ => Append("victory");
        RunManager.Instance.RunStarted += _ => AttachPlayer();
        if (RunManager.Instance.IsInProgress) AttachPlayer();
    }

    // ---------- out-of-combat events (gold, relics, potions, deck) ----------

    private static Player? _player;
    private static int _gold;

    private static void AttachPlayer()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        var me = run != null ? LocalContext.GetMe(run) : null;
        if (ReferenceEquals(me, _player)) return;

        if (_player != null)
        {
            _player.GoldChanged -= OnGoldChanged;
            _player.RelicObtained -= OnRelicObtained;
            _player.RelicRemoved -= OnRelicRemoved;
            _player.PotionProcured -= OnPotionProcured;
            _player.PotionDiscarded -= OnPotionDiscarded;
            _player.Deck.CardAdded -= OnDeckCardAdded;
            _player.Deck.CardRemoved -= OnDeckCardRemoved;
            _player.Creature.CurrentHpChanged -= OnHpChanged;
            _player.Creature.MaxHpChanged -= OnMaxHpChanged;
            foreach (var card in _watched.Keys.ToList()) Unwatch(card);
        }

        _player = me;
        if (me == null) return;
        _gold = me.Gold;
        me.GoldChanged += OnGoldChanged;
        me.RelicObtained += OnRelicObtained;
        me.RelicRemoved += OnRelicRemoved;
        me.PotionProcured += OnPotionProcured;
        me.PotionDiscarded += OnPotionDiscarded;
        me.Deck.CardAdded += OnDeckCardAdded;
        me.Deck.CardRemoved += OnDeckCardRemoved;
        me.Creature.CurrentHpChanged += OnHpChanged;
        me.Creature.MaxHpChanged += OnMaxHpChanged;
        foreach (var card in me.Deck.Cards) Watch(card);
    }

    // ---------- deck card changes (upgrade, downgrade, enchantment, affliction) ----------

    private sealed class Watched
    {
        public int Level;
        public string Name = "";
        public string? Enchantment;
        public string? Affliction;
        public Action OnUpgraded = null!;
        public Action OnEnchantment = null!;
        public Action OnAffliction = null!;
    }

    private static readonly Dictionary<CardModel, Watched> _watched = new(ReferenceEqualityComparer.Instance);

    private static void Watch(CardModel card)
    {
        if (_watched.ContainsKey(card)) return;
        var w = new Watched
        {
            Level = SafeLevel(card),
            Name = View.Fmt.CardName(card),
            Enchantment = View.Fmt.EnchantmentLabel(card),
            Affliction = View.Fmt.AfflictionLabel(card),
        };
        w.OnUpgraded = () => CardUpgraded(card, w);
        w.OnEnchantment = () => CardEnchantmentChanged(card, w);
        w.OnAffliction = () => CardAfflictionChanged(card, w);
        card.Upgraded += w.OnUpgraded;
        card.EnchantmentChanged += w.OnEnchantment;
        card.AfflictionChanged += w.OnAffliction;
        _watched[card] = w;
    }

    private static void Unwatch(CardModel card)
    {
        if (!_watched.Remove(card, out var w)) return;
        card.Upgraded -= w.OnUpgraded;
        card.EnchantmentChanged -= w.OnEnchantment;
        card.AfflictionChanged -= w.OnAffliction;
    }

    private static int SafeLevel(CardModel card)
    {
        try { return card.CurrentUpgradeLevel; } catch { return 0; }
    }

    /// <summary>The game raises Upgraded for both upgrades and downgrades; the level tells which.</summary>
    private static void CardUpgraded(CardModel card, Watched w)
    {
        int level = SafeLevel(card);
        string name = View.Fmt.CardName(card);
        if (level != w.Level)
            Append($"deck {(level > w.Level ? "upgraded" : "downgraded")} {w.Name} → {name}");
        w.Level = level;
        w.Name = name;
    }

    private static void CardEnchantmentChanged(CardModel card, Watched w)
    {
        string? now = View.Fmt.EnchantmentLabel(card);
        if (now != w.Enchantment)
            Append(now != null ? $"deck enchanted {View.Fmt.CardName(card)}: {now}" : $"deck enchantment removed {View.Fmt.CardName(card)}");
        w.Enchantment = now;
    }

    private static void CardAfflictionChanged(CardModel card, Watched w)
    {
        string? now = View.Fmt.AfflictionLabel(card);
        if (now != w.Affliction)
            Append(now != null ? $"deck afflicted {View.Fmt.CardName(card)}: {now}" : $"deck affliction removed {View.Fmt.CardName(card)}");
        w.Affliction = now;
    }

    /// <summary>Out-of-combat HP changes (rest, events). In combat, damage entries already cover HP.</summary>
    private static void OnHpChanged(int oldHp, int newHp)
    {
        if (CombatManager.Instance.IsInProgress || _player == null || oldHp == newHp) return;
        Append($"HP {(newHp > oldHp ? "+" : "")}{newHp - oldHp} → {newHp}/{_player.Creature.MaxHp}");
    }

    private static void OnMaxHpChanged(int oldMax, int newMax)
    {
        if (oldMax == newMax) return;
        Append($"max HP {(newMax > oldMax ? "+" : "")}{newMax - oldMax} → {newMax}");
    }

    private static void OnGoldChanged()
    {
        if (_player == null) return;
        int gold = _player.Gold;
        int delta = gold - _gold;
        _gold = gold;
        if (delta != 0) Append($"gold {(delta > 0 ? "+" : "")}{delta} → {gold}");
    }

    private static void OnRelicObtained(RelicModel relic) => Append($"relic + {Text.Of(() => relic.Title)}");
    private static void OnRelicRemoved(RelicModel relic) => Append($"relic - {Text.Of(() => relic.Title)}");
    private static void OnPotionProcured(PotionModel potion) => Append($"potion + {Text.Of(() => potion.Title)}");
    private static void OnPotionDiscarded(PotionModel potion) => Append($"potion - {Text.Of(() => potion.Title)}");
    private static void OnDeckCardAdded(CardModel card)
    {
        Append($"deck + {View.Fmt.CardLabel(card)}");
        Watch(card);
    }

    private static void OnDeckCardRemoved(CardModel card)
    {
        Append($"deck - {View.Fmt.CardLabel(card)}");
        Unwatch(card);
    }

    /// <summary>Main thread only.</summary>
    public static long Mark()
    {
        FlushDraws();
        return _dropped + Lines.Count;
    }

    /// <summary>Main thread only.</summary>
    public static List<string> Since(long mark)
    {
        FlushDraws();
        long start = Math.Max(0, mark - _dropped);
        return start >= Lines.Count ? new List<string>() : Collapse(Lines.Skip((int)start));
    }

    /// <summary>Consecutive identical lines (multi-hit attacks) become one line with "×n".</summary>
    private static List<string> Collapse(IEnumerable<string> lines)
    {
        var result = new List<string>();
        string? previous = null;
        int count = 0;
        foreach (var line in lines)
        {
            if (line == previous)
            {
                count++;
                continue;
            }
            if (previous != null) result.Add(count > 1 ? $"{previous} ×{count}" : previous);
            previous = line;
            count = 1;
        }
        if (previous != null) result.Add(count > 1 ? $"{previous} ×{count}" : previous);
        return result;
    }

    private static void OnHistoryChanged()
    {
        try
        {
            var entries = CombatManager.Instance.History.Entries as IReadOnlyCollection<CombatHistoryEntry>
                          ?? CombatManager.Instance.History.Entries.ToList();
            int count = entries.Count;
            if (count < _historyCount) _historyCount = 0; // cleared at combat end

            foreach (var entry in entries.Skip(_historyCount))
            {
                try { Add(entry); }
                catch (Exception ex) { Log.Warn($"event format failed for {entry.GetType().Name}: {ex.Message}"); }
            }
            _historyCount = count;
        }
        catch (Exception ex)
        {
            Log.Warn($"history read failed: {ex.Message}");
        }
    }

    private static void Add(CombatHistoryEntry entry)
    {
        if (entry is CardDrawnEntry drawn)
        {
            string card = Text.Of(() => drawn.Card.Title) ?? drawn.Card.Id.Entry;
            _pendingDraws = _pendingDraws == null ? "draw " + card : _pendingDraws + ", " + card;
            return;
        }
        FlushDraws();

        string? line = entry switch
        {
            CardPlayStartedEntry play => $"play {Card(play.CardPlay.Card)}"
                                         + (play.CardPlay.Target != null ? " → " + Name(play.CardPlay.Target) : "")
                                         + (play.CardPlay.IsAutoPlay ? " (auto)" : ""),
            DamageReceivedEntry dmg => Damage(dmg),
            BlockGainedEntry block => $"{Name(block.Actor)} +{block.Amount} block",
            PowerReceivedEntry power => Power(power),
            CardDiscardedEntry discard => $"discard {Card(discard.Card)}",
            CardExhaustedEntry exhaust => $"exhaust {Card(exhaust.Card)}",
            CardGeneratedEntry generated => $"add {Card(generated.Card)}",
            MonsterPerformedMoveEntry move => $"{Name(move.Monster.Creature)} acts: {MoveText(move)}",
            PotionUsedEntry potion => $"potion {Text.Of(() => potion.Potion.Title)}" + (potion.Target != null ? " → " + Name(potion.Target) : ""),
            CardPlayFinishedEntry or EnergySpentEntry or CreatureAttackedEntry => null,
            _ => Text.Of(() => entry.Description),
        };
        if (!string.IsNullOrEmpty(line)) Append(line);
    }

    private static string Damage(DamageReceivedEntry dmg)
    {
        var r = dmg.Result;
        var target = r.Receiver;
        string text = $"{Name(target)} hit {r.TotalDamage}";
        if (r.BlockedDamage > 0) text += $" (blocked {r.BlockedDamage})";
        // My own attacks follow a "play" line; name the source for everything else (enemy attacks, thorns, ...).
        if (dmg.Dealer != null && dmg.Dealer.Player == null) text += $" by {Ids.Creature(dmg.Dealer)}";
        text += $" → HP {target.CurrentHp}/{target.MaxHp}";
        if (r.WasTargetKilled) text += " DEAD";
        return text;
    }

    /// <summary>Localized intent titles of the performed move, plus its internal id for pattern tracking.</summary>
    private static string MoveText(MonsterPerformedMoveEntry entry)
    {
        var creature = entry.Monster.Creature;
        var targets = creature.CombatState?.PlayerCreatures;
        var parts = new List<string>();
        foreach (var intent in entry.Move.Intents)
        {
            string? title = null;
            if (targets != null)
            {
                try { title = Text.Clean(intent.GetHoverTip(targets, creature).Title ?? ""); } catch { }
            }
            parts.Add(string.IsNullOrEmpty(title) ? intent.IntentType.ToString() : title);
        }
        string intents = parts.Count > 0 ? string.Join("+", parts) : "?";
        return $"{intents} ({entry.Move.Id})";
    }

    private static string Power(PowerReceivedEntry entry)
    {
        string name = Text.Of(() => entry.Power.Title) ?? entry.Power.Id.Entry;
        string sign = entry.Amount >= 0 ? "+" : "";
        string owner = Name(entry.Power.Owner);
        return $"{owner} {name} {sign}{entry.Amount:0.##}";
    }

    private static string Card(CardModel card) => Text.Of(() => card.Title) ?? card.Id.Entry;

    private static string Name(Creature? c)
    {
        if (c == null) return "?";
        if (c.Player != null && !c.IsPet) return "me";
        return $"{Ids.Creature(c)} {Text.Of(() => c.Name)}";
    }

    private static void FlushDraws()
    {
        if (_pendingDraws == null) return;
        Append(_pendingDraws);
        _pendingDraws = null;
    }

    private static void Append(string line)
    {
        if (_pendingDraws != null && !line.StartsWith("draw ", StringComparison.Ordinal))
        {
            Lines.Add(_pendingDraws);
            _pendingDraws = null;
        }
        Lines.Add(line);
        if (Lines.Count > MaxLines)
        {
            int drop = Lines.Count - MaxLines;
            Lines.RemoveRange(0, drop);
            _dropped += drop;
        }
    }
}

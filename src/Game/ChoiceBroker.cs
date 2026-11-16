using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using Sts2AgentMcp.Host;

namespace Sts2AgentMcp.Game;

/// <summary>A card selection the game is waiting on (discard, exhaust, upgrade, choose 1 of N, ...).</summary>
internal sealed class PendingChoice
{
    public PendingChoice(List<CardModel> options, int min, int max, string source, string? trigger)
    {
        Options = options;
        Min = min;
        Max = max;
        Source = source;
        Trigger = trigger;
    }

    public List<CardModel> Options { get; }
    public int Min { get; }
    public int Max { get; }
    /// <summary>Which CardSelectCmd entry point asked (e.g. FromHand, FromDeckForUpgrade).</summary>
    public string Source { get; }
    /// <summary>The action that triggered the choice, if known (e.g. the card being played).</summary>
    public string? Trigger { get; }
    public TaskCompletionSource<IEnumerable<CardModel>> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>
/// Answers the game's card-selection prompts through the official selector hook (CardSelectCmd.PushSelector),
/// so they become agent decisions instead of UI clicks. Card rewards, relic and bundle choices are not routed here
/// by the game and stay UI-driven.
/// </summary>
internal sealed class ChoiceBroker : ICardSelector
{
    public static readonly ChoiceBroker Instance = new();

    private ChoiceBroker() { }

    /// <summary>Main thread only.</summary>
    public PendingChoice? Pending { get; private set; }

    public static void Install()
    {
        RunManager.Instance.RunStarted += _ => Instance.EnsureRegistered();
        if (RunManager.Instance.IsInProgress) Instance.EnsureRegistered();
    }

    private void EnsureRegistered()
    {
        Pending = null; // anything pending belonged to a previous run
        if (ReferenceEquals(CardSelectCmd.Selector, this)) return;
        CardSelectCmd.PushSelector(this);
        Log.Info("card selector registered");
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        var list = options.ToList();
        if (list.Count == 0 || maxSelect <= 0) return Task.FromResult(Enumerable.Empty<CardModel>());

        var choice = new PendingChoice(list, Math.Max(0, minSelect), Math.Min(maxSelect, list.Count), DetectSource(), DetectTrigger());
        Pending = choice;
        Log.Info($"choice requested: {choice.Source} {choice.Min}-{choice.Max} of {list.Count}");
        return choice.Completion.Task;
    }

    /// <summary>Only consulted by the game in test mode; fall back to "no selection".</summary>
    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives) => default;

    /// <summary>Resolve the pending choice with option indices. Returns an error message or null.</summary>
    public string? Resolve(IReadOnlyList<int> indices)
    {
        var choice = Pending;
        if (choice == null) return "no choice is pending";
        var distinct = indices.Distinct().ToList();
        if (distinct.Any(i => i < 0 || i >= choice.Options.Count))
            return $"option index out of range (0-{choice.Options.Count - 1})";
        if (distinct.Count < choice.Min || distinct.Count > choice.Max)
            return choice.Min == choice.Max
                ? $"select exactly {choice.Min}"
                : $"select between {choice.Min} and {choice.Max}";

        Pending = null;
        choice.Completion.TrySetResult(distinct.Select(i => choice.Options[i]).ToList());
        return null;
    }

    /// <summary>Answer with no cards (cancel for cancelable prompts such as rest-site smith or shop card removal).</summary>
    public string? Cancel()
    {
        var choice = Pending;
        if (choice == null) return "no choice is pending";
        Pending = null;
        choice.Completion.TrySetResult(Array.Empty<CardModel>());
        return null;
    }

    private static string DetectSource()
    {
        try
        {
            foreach (var frame in new StackTrace(1, false).GetFrames())
            {
                var type = frame.GetMethod()?.DeclaringType;
                for (var t = type; t != null; t = t.DeclaringType)
                {
                    if (t != typeof(CardSelectCmd)) continue;
                    // async methods run in a nested state machine type named "<FromHand>d__12"
                    string name = type != t ? type!.Name : frame.GetMethod()!.Name;
                    int open = name.IndexOf('<');
                    int close = name.IndexOf('>');
                    return open >= 0 && close > open ? name[(open + 1)..close] : name;
                }
            }
        }
        catch { /* best effort */ }
        return "select";
    }

    private static string? DetectTrigger()
    {
        try
        {
            var action = RunManager.Instance.ActionExecutor?.CurrentlyRunningAction;
            if (action == null) return null;
            // Most triggers are card plays or potion uses; name the model instead of the action's debug string.
            foreach (var name in new[] { "Card", "Potion", "Relic" })
            {
                var model = action.GetType().GetProperty(name)?.GetValue(action) as AbstractModel;
                if (model != null) return Catalog.TitleOf(model) ?? model.Id.Entry;
            }
            return action.GetType().Name;
        }
        catch { return null; }
    }
}

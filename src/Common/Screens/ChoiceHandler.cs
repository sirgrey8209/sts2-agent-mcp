using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Screens;

/// <summary>A card-selection prompt intercepted by <see cref="ChoiceBroker"/>.</summary>
internal sealed class ChoiceHandler : IScreenHandler
{
    public string Kind => "choice";

    public bool Matches(GameCtx ctx) => ChoiceBroker.Instance.Pending != null;

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var choice = ChoiceBroker.Instance.Pending!;
        string count = choice.Min == choice.Max ? $"{choice.Min}" : $"{choice.Min}-{choice.Max}";
        view.Add($"select {count} card(s) · {choice.Source}" + (choice.Trigger != null ? $" · from {choice.Trigger}" : ""));
        bool upgrade = choice.Source.Contains("Upgrade", System.StringComparison.Ordinal);
        for (int i = 0; i < choice.Options.Count; i++)
        {
            var card = choice.Options[i];
            var pile = card.Pile?.Type ?? PileType.None;
            string line = $"  {i} {Fmt.PlainCard(card, pile)}";
            if (upgrade && Fmt.UpgradedDescription(card) is { } upgraded) line += $" ⇒ {upgraded}";
            view.Add(line);
        }
        view.Actions = "choose <i> [<j> ...]" + (choice.Min == 0 ? " · skip" : "") + (IsCancelable(ctx) ? " · cancel" : "");
        return view;
    }

    /// <summary>
    /// The selector API does not tell us whether a prompt is cancelable. Rest site (smith, cook) and shop (card removal)
    /// prompts are, and the game treats an empty answer as "cancel" there.
    /// </summary>
    private static bool IsCancelable(GameCtx ctx) =>
        !ctx.InCombat && ctx.Run?.CurrentRoom is MegaCrit.Sts2.Core.Rooms.RestSiteRoom or MegaCrit.Sts2.Core.Rooms.MerchantRoom;

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        string? error;
        switch (action.Verb)
        {
            case "choose":
                var indices = action.IntArgs();
                if (indices == null || indices.Count == 0) return ExecResult.Fail("usage: choose <i> [<j> ...]");
                error = ChoiceBroker.Instance.Resolve(indices);
                break;
            case "skip":
                error = ChoiceBroker.Instance.Resolve(System.Array.Empty<int>());
                break;
            case "cancel":
                if (!IsCancelable(ctx)) return ExecResult.Fail("this choice cannot be cancelled");
                error = ChoiceBroker.Instance.Cancel();
                break;
            default:
                return ExecResult.Fail($"a card choice is pending; use choose/skip (got '{action.Verb}')");
        }
        if (error != null) return ExecResult.Fail(error);
        if (ctx.InCombat) return ExecResult.Ok(SettleMode.Combat);
        // A rest-site choice (smith) returns to the room, which enables proceed only after its VFX.
        if (ctx.Screen is MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom room)
            return ExecResult.Ok(SettleMode.Screen, until: () => RestSiteHandler.Ready(room));
        return ExecResult.Ok(SettleMode.Screen);
    }
}

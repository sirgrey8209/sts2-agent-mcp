using System.Linq;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rewards;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Screens;

/// <summary>Post-combat rewards list (gold, potions, relics, card rewards).</summary>
internal sealed class RewardsHandler : IScreenHandler
{
    public string Kind => "rewards";

    public bool Matches(GameCtx ctx) => ctx.Screen is NRewardsScreen;

    private static System.Collections.Generic.List<NRewardButton> Buttons(NRewardsScreen screen) =>
        Ui.FindAllByPosition<NRewardButton>(screen).Where(b => b.Reward != null && Ui.IsActionable(b)).ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var screen = (NRewardsScreen)ctx.Screen!;
        var buttons = Buttons(screen);
        for (int i = 0; i < buttons.Count; i++)
            view.Add($"  {i} {Describe(buttons[i].Reward!)}");
        if (buttons.Count == 0) view.Add("  (nothing left to claim)");

        bool canProceed = Ui.FindFirst<NProceedButton>(screen) is { } proceed && Ui.IsActionable(proceed);
        view.Actions = "choose <i>" + (canProceed ? " · proceed" : "");
        return view;
    }

    private static string Describe(Reward reward)
    {
        string desc = Text.Of(() => reward.Description) ?? reward.GetType().Name;
        return reward switch
        {
            GoldReward g => $"gold {g.Amount}",
            PotionReward p when p.Potion != null => $"potion {Text.Of(() => p.Potion.Title)} — {Text.Of(() => p.Potion.DynamicDescription)}",
            CardReward => "card reward (opens selection)",
            _ => desc,
        };
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var screen = (NRewardsScreen)ctx.Screen!;
        switch (action.Verb)
        {
            case "choose":
            {
                var buttons = Buttons(screen);
                int? i = action.IntArg(0);
                if (i == null || i < 0 || i >= buttons.Count) return ExecResult.Fail($"choose 0-{buttons.Count - 1}");
                buttons[i.Value].ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            }
            case "proceed":
            {
                var proceed = Ui.FindFirst<NProceedButton>(screen);
                if (!Ui.IsActionable(proceed)) return ExecResult.Fail("proceed is not available");
                proceed!.ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange);
            }
            default:
                return ExecResult.Fail($"unknown rewards action '{action.Verb}'");
        }
    }
}

/// <summary>Choose one card from a card reward (or take an alternative such as skip).</summary>
internal sealed class CardRewardHandler : IScreenHandler
{
    public string Kind => "card_reward";

    public bool Matches(GameCtx ctx) => ctx.Screen is NCardRewardSelectionScreen;

    private static System.Collections.Generic.List<NCardHolder> Cards(NCardRewardSelectionScreen screen) =>
        Ui.FindAllByPosition<NCardHolder>(screen).Where(h => h.CardModel != null && Ui.IsVisible(h)).ToList();

    private static System.Collections.Generic.List<NCardRewardAlternativeButton> Alternatives(NCardRewardSelectionScreen screen) =>
        Ui.FindAllByPosition<NCardRewardAlternativeButton>(screen).Where(Ui.IsActionable).ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var screen = (NCardRewardSelectionScreen)ctx.Screen!;
        var cards = Cards(screen);
        for (int i = 0; i < cards.Count; i++)
            view.Add($"  {i} {Fmt.PlainCard(cards[i].CardModel!)}");

        var alternatives = Alternatives(screen);
        for (int i = 0; i < alternatives.Count; i++)
            view.Add($"  alt {i}: {Ui.LabelOf(alternatives[i]) ?? "alternative"}");

        view.Actions = "choose <i>" + (alternatives.Count > 0 ? " · skip (= alt 0) · alt <i>" : "");
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var screen = (NCardRewardSelectionScreen)ctx.Screen!;
        switch (action.Verb)
        {
            case "choose":
            {
                var cards = Cards(screen);
                int? i = action.IntArg(0);
                if (i == null || i < 0 || i >= cards.Count) return ExecResult.Fail($"choose 0-{cards.Count - 1}");
                var holder = cards[i.Value];
                holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
                return ExecResult.Ok(SettleMode.ScreenChange);
            }
            case "skip":
            case "alt":
            {
                var alternatives = Alternatives(screen);
                int i = action.Verb == "skip" ? 0 : action.IntArg(0) ?? -1;
                if (i < 0 || i >= alternatives.Count) return ExecResult.Fail("no such alternative");
                alternatives[i].ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange);
            }
            default:
                return ExecResult.Fail($"unknown card reward action '{action.Verb}'");
        }
    }
}

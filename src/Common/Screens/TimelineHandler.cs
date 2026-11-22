using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline.UnlockScreens;
using MegaCrit.Sts2.Core.Saves;
using Sts2AgentMcp.Game;

namespace Sts2AgentMcp.Screens;

/// <summary>
/// Timeline (epochs). After a run that obtained epochs the game goes here and keeps Back disabled until every obtained
/// epoch is revealed. Revealing changes the profile's progression (unlocks cards/relics/characters), so it is exposed as
/// one named action, `reveal_epochs`, that walks the whole sequence: reveal slot → close inspect screen → confirm each
/// unlock screen → repeat → back to the main menu. No other timeline buttons are offered.
/// </summary>
internal sealed class TimelineHandler : IScreenHandler
{
    public string Kind => "timeline";

    public bool Matches(GameCtx ctx) => ctx.Screen is NTimelineScreen or NUnlockScreen;

    private static int PendingEpochs()
    {
        try { return SaveManager.Instance.GetDiscoveredEpochCount(); }
        catch { return -1; }
    }

    private static NTimelineScreen? Timeline => NTimelineScreen.Instance;

    private static NClickableControl? Button(object? owner, string field) =>
        owner == null ? null : Ui.GetField(owner, field) as NClickableControl;

    private static bool InspectOpen(NTimelineScreen timeline) =>
        Ui.GetField(timeline, "_inspectScreen") is CanvasItem inspect && Ui.IsVisible(inspect);

    private static bool InputBlocked(NTimelineScreen timeline) =>
        Ui.GetField(timeline, "_inputBlocker") is CanvasItem blocker && Ui.IsVisible(blocker);

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        int pending = PendingEpochs();
        view.Add($"epochs waiting to be revealed: {pending}");
        if (ctx.Screen is NUnlockScreen unlock)
        {
            view.Add("  unlock screen: " + unlock.GetType().Name);
            foreach (var text in Ui.Texts(unlock, 6)) view.Add("    " + text);
        }
        else if (Timeline is { } timeline && InspectOpen(timeline))
        {
            view.Add("  epoch inspect screen is open");
        }
        else if (Timeline is { } tl && Ui.GetField(tl, "_tutorial") is CanvasItem tutorial && Ui.IsVisible(tutorial))
        {
            foreach (var text in Ui.Texts(tutorial, 3)) view.Add("  tutorial: " + text);
        }

        bool canBack = Timeline is { } t && ctx.Screen is NTimelineScreen && !InspectOpen(t) && Ui.IsActionable(Button(t, "_backButton"));
        view.Actions = pending > 0 || ctx.Screen is NUnlockScreen
            ? "reveal_epochs (reveals obtained epochs and applies their unlocks to this profile, then returns to the main menu)"
            : canBack ? "back" : "wait";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var timeline = Timeline;
        if (timeline == null) return ExecResult.Fail("timeline is not available");
        switch (action.Verb)
        {
            case "reveal_epochs":
                return RevealStep(ctx, timeline);
            case "back":
            {
                var back = Button(timeline, "_backButton");
                if (!Ui.IsActionable(back)) return ExecResult.Fail("back is disabled (reveal the obtained epochs first: reveal_epochs)");
                back!.ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange);
            }
            default:
                return ExecResult.Fail($"unknown timeline action '{action.Verb}'");
        }
    }

    /// <summary>Does the next step of the reveal sequence and schedules itself again until back at the main menu.</summary>
    private static ExecResult RevealStep(GameCtx ctx, NTimelineScreen timeline)
    {
        var again = new[] { "reveal_epochs" };

        // 1. An unlock screen (new card/relic/character...) is showing: confirm it once its button is enabled.
        if (ctx.Screen is NUnlockScreen unlock)
        {
            var confirm = Button(unlock, "_unlockConfirmButton");
            if (!Ui.IsActionable(confirm))
                return ExecResult.Ok(SettleMode.Screen, followups: again, until: () => Ui.IsActionable(confirm));
            confirm!.ForceClick();
            return ExecResult.Ok(SettleMode.Screen, followups: again);
        }

        // 2. The epoch inspect screen is open: close it once its close button is enabled (queued unlock screens follow).
        if (InspectOpen(timeline))
        {
            var close = Button(Ui.GetField(timeline, "_inspectScreen"), "_closeButton");
            if (!Ui.IsActionable(close))
                return ExecResult.Ok(SettleMode.Screen, followups: again, until: () => Ui.IsActionable(close));
            close!.ForceClick();
            return ExecResult.Ok(SettleMode.Screen, followups: again,
                until: () => !InspectOpen(timeline) || ActiveUnlockScreen(timeline));
        }

        // 3a. First visit: a tutorial overlay ("the timeline reveals history...") blocks input until acknowledged.
        if (Ui.GetField(timeline, "_tutorial") is CanvasItem tutorial && Ui.IsVisible(tutorial))
        {
            var acknowledge = Button(tutorial, "_acknowledgeButton");
            if (!Ui.IsActionable(acknowledge))
                return ExecResult.Ok(SettleMode.Screen, followups: again, until: () => Ui.IsActionable(acknowledge) || !Ui.IsLive(tutorial));
            acknowledge!.ForceClick();
            return ExecResult.Ok(SettleMode.Screen, followups: again,
                until: () => !Ui.IsLive(tutorial) || !Ui.IsVisible(tutorial) || !InputBlocked(timeline));
        }

        // 3. Timeline is animating (input blocked): wait.
        if (InputBlocked(timeline))
            return ExecResult.Ok(SettleMode.Screen, followups: again,
                until: () => !InputBlocked(timeline) || InspectOpen(timeline) || ActiveUnlockScreen(timeline));

        // 4. Reveal the next obtained epoch.
        var slot = Ui.FindAll<NEpochSlot>(timeline).FirstOrDefault(s => s.State == EpochSlotState.Obtained);
        if (slot != null)
        {
            slot.ForceClick();
            return ExecResult.Ok(SettleMode.Screen, followups: again, timeout: TimeSpan.FromSeconds(30),
                until: () => InspectOpen(timeline) || ActiveUnlockScreen(timeline) || !InputBlocked(timeline));
        }

        // 5. Nothing left: leave the timeline.
        var back = Button(timeline, "_backButton");
        if (!Ui.IsActionable(back))
        {
            if (PendingEpochs() > 0)
                return ExecResult.Fail("epochs are pending but no revealable slot was found; use state detail=full and report this");
            return ExecResult.Ok(SettleMode.Screen, followups: again, until: () => Ui.IsActionable(back));
        }
        back!.ForceClick();
        return ExecResult.Ok(SettleMode.ScreenChange, note: "all obtained epochs revealed");
    }

    private static bool ActiveUnlockScreen(NTimelineScreen timeline) => timeline.CurrentUnlockScreen != null;
}

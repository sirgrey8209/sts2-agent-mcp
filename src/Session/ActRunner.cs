using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.Host;
using Sts2AgentMcp.Screens;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Session;

/// <summary>
/// Runs `act` requests: execute an action on the main thread, wait for the game to settle, collect events,
/// stop early when the situation changes, and finish with a render of the resulting screen.
/// </summary>
internal sealed class ActRunner
{
    // Macros such as reveal_epochs expand into many steps; each step is bounded by its own settle timeout.
    private const int MaxSteps = 100;

    private sealed record StepStart(ExecResult Result, Observation Before, long Mark, string Kind);

    public Task<string> RenderCurrent(bool full) => MainThread.Run(() => Render(full));

    public async Task<string> Run(IReadOnlyList<string> actions)
    {
        var output = new StringBuilder();
        // Followup = step produced by a macro (e.g. new_run); those continue through screen changes.
        var queue = new LinkedList<(string Raw, bool Followup)>(actions.Select(a => (a, false)));
        int steps = 0;
        string? stopReason = null;

        while (queue.Count > 0)
        {
            if (++steps > MaxSteps)
            {
                stopReason = "too many steps";
                break;
            }

            string raw = queue.First!.Value.Raw;
            queue.RemoveFirst();

            var spec = ActionSpec.Parse(raw);
            output.Append("> ").AppendLine(raw);

            StepStart start;
            try
            {
                start = await MainThread.Run(() => Begin(spec));
            }
            catch (Exception ex)
            {
                Log.Error($"action '{raw}' failed: {ex}");
                output.Append("  ! ").AppendLine(Features.IsBindingFailure(ex) ? "game API mismatch: " + ex.Message : ex.Message);
                stopReason = "error";
                break;
            }

            if (start.Result.Error != null)
            {
                output.Append("  ! ").AppendLine(start.Result.Error);
                stopReason = "error";
                break;
            }

            var settle = await Settle.Wait(start.Result.Settle, start.Before, start.Result.Timeout, start.Result.Until);
            var events = await MainThread.Run(() => EventLog.Since(start.Mark));
            foreach (var line in events) output.Append("  ").AppendLine(line);
            if (start.Result.Note != null) output.Append("  ").AppendLine(start.Result.Note);

            if (settle.TimedOut)
            {
                output.Append("  ! timed out waiting for the game: ").AppendLine(settle.Diagnostic);
                stopReason = "timeout";
                break;
            }

            if (start.Result.Followups.Count > 0)
            {
                // Macro expansion: run these next, before the remaining user actions.
                foreach (var followup in Enumerable.Reverse(start.Result.Followups)) queue.AddFirst((followup, true));
                continue;
            }

            bool nextIsUserAction = queue.Count > 0 && !queue.First!.Value.Followup;
            // A choice the agent anticipated ("play c9", "choose 0") continues; anything else stops.
            bool answersChoice = settle.Outcome == "choice" && nextIsUserAction
                                 && ActionSpec.Parse(queue.First!.Value.Raw).Verb is "choose" or "skip" or "cancel";
            if (nextIsUserAction && !answersChoice && settle.Outcome is "choice" or "screen_changed" or "combat_ended")
            {
                stopReason = settle.Outcome.Replace('_', ' ');
                break;
            }
        }

        if (queue.Count > 0)
            output.AppendLine($"(stopped: {stopReason}; not run: {string.Join(" | ", queue.Select(q => q.Raw))})");

        output.AppendLine();
        output.Append(await RenderCurrent(false));
        return output.ToString();
    }

    /// <summary>Main thread: capture state, find the handler, mark the event log, start the action.</summary>
    private static StepStart Begin(ActionSpec spec)
    {
        var before = Observation.Capture();
        long mark = EventLog.Mark();

        if (spec.Verb == "wait")
        {
            var mode = before.InCombat ? SettleMode.Combat : SettleMode.Screen;
            return new StepStart(ExecResult.Ok(mode), before, mark, "wait");
        }

        var ctx = GameCtx.Capture();
        var handler = ScreenRegistry.Find(ctx);

        // Potions and run-level actions work on any in-run screen; a pending card choice or popup must be answered first.
        if (ctx.Run != null && handler.Kind is not ("choice" or "popup"))
        {
            if (PotionActions.Handles(spec.Verb)) return new StepStart(PotionActions.Execute(ctx, spec), before, mark, "potion");
            if (RunActions.Handles(spec.Verb)) return new StepStart(RunActions.Execute(ctx, spec), before, mark, "run");
        }

        return new StepStart(handler.Execute(ctx, spec), before, mark, handler.Kind);
    }

    /// <summary>Main thread: header + current screen + accepted actions.</summary>
    private static string Render(bool full)
    {
        var ctx = GameCtx.Capture();
        var sb = new StringBuilder();

        string disabled = Features.Summary();
        if (disabled.Length > 0) sb.AppendLine("[" + disabled + "]");

        if (ctx.Run != null)
        {
            sb.AppendLine(Fmt.RunHeader(ctx.Run, ctx.Me));
            if (ctx.Me != null)
            {
                sb.AppendLine(Fmt.Relics(ctx.Me));
                sb.AppendLine(Fmt.Potions(ctx.Me, withText: full));
                if (full)
                {
                    sb.AppendLine(Fmt.Deck(ctx.Me));
                    foreach (var line in Fmt.RelicDetails(ctx.Me)) sb.Append("  ").AppendLine(line);
                }
                else if (!ctx.InCombat)
                {
                    // In combat the piles line carries the same hint.
                    sb.AppendLine($"deck {ctx.Me.Deck.Cards.Count} cards (card list and relic text: state detail=full)");
                }
            }
        }

        var handler = ScreenRegistry.Find(ctx);
        ScreenView view;
        try
        {
            view = handler.Render(ctx, full);
        }
        catch (Exception ex)
        {
            Log.Error($"{handler.Kind} render failed: {ex}");
            if (Features.IsBindingFailure(ex)) Features.Disable("screen:" + handler.Kind, ex);
            view = new ScreenView(handler.Kind).Add("! render failed: " + ex.Message);
        }

        sb.Append("== ").Append(view.Kind).AppendLine(" ==");
        foreach (var line in view.Lines) sb.AppendLine(line);
        string actions = view.Actions;
        if (view.Kind is not ("combat" or "choice" or "popup" or "game_over") && PotionActions.HasPotions(ctx))
            actions += (actions.Length > 0 ? " · " : "") + "potion <slot> · discard_potion <slot>";
        if (full && ctx.Run != null && view.Kind is not ("choice" or "popup"))
            actions += (actions.Length > 0 ? " · " : "") + RunActions.Help;
        if (actions.Length > 0) sb.Append("actions: ").AppendLine(actions);
        return sb.ToString().TrimEnd();
    }
}

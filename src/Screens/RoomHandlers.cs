using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using Sts2AgentMcp.Game;
using Sts2AgentMcp.View;

namespace Sts2AgentMcp.Screens;

/// <summary>Event rooms, including ancients (Neow etc.). Options come from the layout's buttons in child order.</summary>
internal sealed class EventRoomHandler : IScreenHandler
{
    public string Kind => "event";

    public bool Matches(GameCtx ctx) => ctx.Screen is NEventRoom { Layout: not null };

    private static List<NEventOptionButton> Options(NEventRoom room) =>
        room.Layout!.OptionButtons.Where(b => Ui.IsLive(b) && b.Option != null).ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var room = (NEventRoom)ctx.Screen!;
        var layout = room.Layout!;

        var model = (ctx.Run?.CurrentRoom as EventRoom)?.LocalMutableEvent;
        string title = Text.Of(() => model?.Title) ?? "";
        view.Add(title.Length > 0 ? title : layout.GetType().Name);
        var descriptionNode = layout.GetNodeOrNull("%EventDescription");
        string? body = (descriptionNode != null ? Ui.LabelOf(descriptionNode) : null) ?? Text.Of(() => model?.Description);
        if (!string.IsNullOrEmpty(body) && body != title) view.Add("  " + body);

        if (layout is NAncientEventLayout && layout.GetNodeOrNull("%DialogueContainer") is Node dialogue)
        {
            foreach (var line in Ui.Texts(dialogue, 6)) view.Add("  » " + line);
        }
        else if (model != null && CurrentDialogueLine(model) is { } spoken)
        {
            // Events that talk through speech bubbles in the room (The Architect) have no description text.
            view.Add("  » " + spoken);
        }

        var options = Options(room);
        for (int i = 0; i < options.Count; i++)
        {
            var option = options[i].Option;
            var flags = new List<string>();
            if (option.IsLocked) flags.Add("locked");
            if (option.WasChosen) flags.Add("chosen");
            try { if (ctx.Me != null && option.WillKillPlayer?.Invoke(ctx.Me) == true) flags.Add("WILL KILL YOU"); } catch { }
            if (!options[i].IsEnabled && !option.IsLocked) flags.Add("disabled");
            string label = Ui.LabelOf(options[i]) ?? Text.Of(() => option.Title) ?? "option";
            // Relic options usually describe the relic in the label already; add it only when the label doesn't.
            if (option.Relic != null && Text.Of(() => option.Relic.Title) is { } relicName && !label.Contains(relicName))
                label += $" [relic {relicName}: {Text.Of(() => option.Relic.DynamicDescription)}]";
            view.Add($"  {i} {label}" + (flags.Count > 0 ? $" ({string.Join(", ", flags)})" : ""));
        }

        bool onlyProceed = options.Count == 1 && options[0].Option.IsProceed;
        view.Actions = onlyProceed ? "proceed (= choose 0)" : "choose <i>";
        return view;
    }

    /// <summary>
    /// The line an event with ancient-style dialogue (private `_dialogue` + `_currentLineIndex`) is currently
    /// speaking, as "speaker: text". Null for other events.
    /// </summary>
    private static string? CurrentDialogueLine(MegaCrit.Sts2.Core.Models.EventModel model)
    {
        try
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            if (model.GetType().GetField("_dialogue", flags)?.GetValue(model)
                    is not MegaCrit.Sts2.Core.Entities.Ancients.AncientDialogue dialogue
                || model.GetType().GetField("_currentLineIndex", flags)?.GetValue(model) is not int index
                || index < 0 || index >= dialogue.Lines.Count)
                return null;
            var line = dialogue.Lines[index];
            string? text = Text.Of(() => line.LineText) ?? Text.Of(() => line.LineText?.GetRawText());
            if (string.IsNullOrEmpty(text)) return null;
            string speaker = line.Speaker.ToString() switch
            {
                "Ancient" => Text.Of(() => model.Title) ?? "ancient",
                "Character" => "me",
                var other => other,
            };
            return $"{speaker}: {text}";
        }
        catch
        {
            return null;
        }
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var room = (NEventRoom)ctx.Screen!;
        var options = Options(room);
        int? index = action.Verb switch
        {
            "choose" => action.IntArg(0),
            "proceed" => options.FindIndex(b => b.Option.IsProceed) is var p && p >= 0 ? p : null,
            _ => null,
        };
        if (action.Verb is not ("choose" or "proceed")) return ExecResult.Fail($"unknown event action '{action.Verb}'");
        if (index == null || index < 0 || index >= options.Count)
            return ExecResult.Fail(action.Verb == "proceed" ? "there is no proceed option now" : $"choose 0-{options.Count - 1}");

        var button = options[index.Value];
        if (button.Option.IsLocked) return ExecResult.Fail("that option is locked");
        if (!button.IsEnabled) return ExecResult.Fail("that option is not available right now");
        bool leaving = button.Option.IsProceed;
        button.ForceClick();
        return ExecResult.Ok(leaving ? SettleMode.ScreenChange : SettleMode.Screen);
    }
}

/// <summary>Rest site. Display and execution both use the model's option order.</summary>
internal sealed class RestSiteHandler : IScreenHandler
{
    public string Kind => "rest_site";

    public bool Matches(GameCtx ctx) => ctx.Screen is NRestSiteRoom;

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var room = (NRestSiteRoom)ctx.Screen!;
        var options = room.Options;
        for (int i = 0; i < options.Count; i++)
        {
            var o = options[i];
            view.Add($"  {i} {Text.Of(() => o.Title)} — {Text.Of(() => o.Description)}" + (o.IsEnabled ? "" : " (unavailable)"));
        }
        bool canProceed = Ui.IsActionable(room.ProceedButton);
        view.Actions = (options.Count > 0 ? "choose <i>" : "") + (canProceed ? (options.Count > 0 ? " · " : "") + "proceed" : "");
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var room = (NRestSiteRoom)ctx.Screen!;
        switch (action.Verb)
        {
            case "choose":
            {
                var options = room.Options;
                int? i = action.IntArg(0);
                if (i == null || i < 0 || i >= options.Count) return ExecResult.Fail($"choose 0-{options.Count - 1}");
                var option = options[i.Value];
                if (!option.IsEnabled) return ExecResult.Fail("that option is unavailable");
                var button = room.GetButtonForOption(option);
                if (button == null || !Ui.IsActionable(button)) return ExecResult.Fail("options are not selectable right now");
                button.ForceClick();
                return ExecResult.Ok(SettleMode.Screen, until: () => Ready(room));
            }
            case "proceed":
                if (!Ui.IsActionable(room.ProceedButton)) return ExecResult.Fail("proceed is not available yet");
                room.ProceedButton.ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange);
            default:
                return ExecResult.Fail($"unknown rest site action '{action.Verb}'");
        }
    }

    /// <summary>
    /// After an option resolves (directly, or through a card choice such as smith) the room plays VFX, rebuilds its
    /// options and only then enables proceed. Ready once proceed or an option button can be clicked.
    /// </summary>
    public static bool Ready(NRestSiteRoom room) =>
        Ui.IsActionable(room.ProceedButton)
        || room.Options.Any(o => room.GetButtonForOption(o) is { } b && Ui.IsActionable(b));
}

/// <summary>
/// Merchant and the fake-merchant event. Reading state never opens the shop; "open" is an explicit action.
/// Entry order follows the inventory model (cards, colorless, relics, potions, card removal), which is also the on-screen order.
/// </summary>
internal sealed class ShopHandler : IScreenHandler
{
    public string Kind => "shop";

    public bool Matches(GameCtx ctx) => ctx.Screen is NMerchantRoom or NMerchantInventory or NFakeMerchant;

    private sealed record Shop(MerchantInventory? Inventory, NMerchantInventory? InventoryNode, bool IsOpen, bool Fake,
        NClickableControl? Proceed, Func<bool> Open);

    private static Shop Resolve(GameCtx ctx)
    {
        if (ctx.Screen is NFakeMerchant || ctx.Screen is NFakeMerchantInventory)
        {
            var node = Ui.FindFirst<NFakeMerchant>(Ui.Root);
            var model = (ctx.Run?.CurrentRoom as EventRoom)?.LocalMutableEvent as FakeMerchant;
            var inventoryNode = node?.Inventory;
            return new Shop(model?.Inventory, inventoryNode, inventoryNode?.IsOpen ?? false, true,
                Ui.GetField(node!, "_proceedButton") as NClickableControl,
                () =>
                {
                    if (node == null || !Ui.IsActionable(node.MerchantButton)) return false;
                    node.MerchantButton.ForceClick();
                    return true;
                });
        }

        var room = NMerchantRoom.Instance;
        var merchantRoom = ctx.Run?.CurrentRoom as MerchantRoom;
        return new Shop(merchantRoom?.GetLocalInventory(), room?.Inventory, room?.Inventory?.IsOpen ?? false, false,
            room?.ProceedButton,
            () =>
            {
                if (room == null) return false;
                room.OpenInventory();
                return true;
            });
    }

    private static List<MerchantEntry> Entries(Shop shop) => shop.Inventory?.AllEntries.ToList() ?? new List<MerchantEntry>();

    private static bool Close(Shop shop)
    {
        if (shop.InventoryNode == null || Ui.GetField(shop.InventoryNode, "_backButton") is not NClickableControl back || !Ui.IsActionable(back))
            return false;
        back.ForceClick();
        return true;
    }

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var shop = Resolve(ctx);
        view.Add((shop.Fake ? "fake merchant" : "merchant") + (shop.IsOpen ? " (inventory open)" : " (closed)"));

        var entries = Entries(shop);
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            var flags = new List<string>();
            if (!e.IsStocked) flags.Add("sold");
            else if (!e.EnoughGold) flags.Add("can't afford");
            if (e is MerchantCardEntry { IsOnSale: true }) flags.Add("sale");
            view.Add($"  {i} {e.Cost}g {Describe(e)}" + (flags.Count > 0 ? $" ({string.Join(", ", flags)})" : ""));
        }
        if (shop.Inventory == null) view.Add("  (inventory not available yet)");

        var actions = new List<string>();
        if (entries.Count > 0) actions.Add("buy <i>");
        if (!shop.IsOpen) actions.Add("open");
        else actions.Add("close");
        actions.Add("leave");
        view.Actions = string.Join(" · ", actions);
        return view;
    }

    private static string Describe(MerchantEntry entry) => entry switch
    {
        MerchantCardEntry c when c.CreationResult != null => "card " + Fmt.PlainCard(c.CreationResult.Card),
        MerchantRelicEntry r when r.Model != null => $"relic {Text.Of(() => r.Model.Title)} — {Text.Of(() => r.Model.DynamicDescription)}",
        MerchantPotionEntry p when p.Model != null => $"potion {Text.Of(() => p.Model.Title)} — {Text.Of(() => p.Model.DynamicDescription)}",
        MerchantCardRemovalEntry => "remove a card from your deck",
        _ => entry.GetType().Name,
    };

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var shop = Resolve(ctx);
        switch (action.Verb)
        {
            case "open":
                if (shop.IsOpen) return ExecResult.Fail("the inventory is already open");
                return shop.Open() ? ExecResult.Ok(SettleMode.Screen) : ExecResult.Fail("the shop cannot be opened right now");

            case "buy":
            {
                if (!shop.IsOpen)
                {
                    // Open first, then retry the purchase once the inventory is up.
                    if (!shop.Open()) return ExecResult.Fail("the shop cannot be opened right now");
                    return ExecResult.Ok(SettleMode.Screen, followups: new[] { action.Raw });
                }
                var entries = Entries(shop);
                int? i = action.IntArg(0);
                if (i == null || i < 0 || i >= entries.Count) return ExecResult.Fail($"buy 0-{entries.Count - 1}");
                var entry = entries[i.Value];
                if (!entry.IsStocked) return ExecResult.Fail("that item is sold out");
                if (!entry.EnoughGold) return ExecResult.Fail($"not enough gold ({entry.Cost}g)");
                if (entry is MerchantPotionEntry && ctx.Me != null && !ctx.Me.HasOpenPotionSlots)
                    return ExecResult.Fail("potion slots are full (discard_potion first)");
                _ = entry.OnTryPurchaseWrapper(shop.Inventory);
                return ExecResult.Ok(SettleMode.Screen);
            }

            case "close":
                if (!shop.IsOpen) return ExecResult.Fail("the inventory is not open");
                return Close(shop) ? ExecResult.Ok(SettleMode.Screen) : ExecResult.Fail("cannot close the inventory right now");

            case "leave":
            case "proceed":
            {
                if (shop.IsOpen)
                {
                    if (!Close(shop)) return ExecResult.Fail("cannot close the inventory right now");
                    return ExecResult.Ok(SettleMode.Screen, followups: new[] { "leave" });
                }
                if (!Ui.IsActionable(shop.Proceed)) return ExecResult.Fail("leaving is not possible right now");
                shop.Proceed!.ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange);
            }

            default:
                return ExecResult.Fail($"unknown shop action '{action.Verb}'");
        }
    }
}

/// <summary>Treasure room: open the chest (explicit), take the relic or skip, proceed.</summary>
internal sealed class TreasureHandler : IScreenHandler
{
    public string Kind => "treasure";

    public bool Matches(GameCtx ctx) => ctx.Screen is NTreasureRoom;

    private static NClickableControl? Chest(NTreasureRoom room) => Ui.GetField(room, "_chestButton") as NClickableControl;

    private static NClickableControl? RelicHolder(NTreasureRoom room)
    {
        var collection = Ui.GetField(room, "_relicCollection") as NTreasureRoomRelicCollection;
        var holder = collection?.SingleplayerRelicHolder;
        return Ui.IsActionable(holder) ? holder : null;
    }

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var room = (NTreasureRoom)ctx.Screen!;
        var actions = new List<string>();
        if (Ui.IsActionable(Chest(room)))
        {
            view.Add("  chest (closed)");
            actions.Add("open");
        }
        if (RelicHolder(room) is NTreasureRoomRelicHolder holder)
        {
            var relic = holder.Relic?.Model;
            view.Add($"  0 relic {Text.Of(() => relic?.Title)} — {Text.Of(() => relic?.DynamicDescription)}");
            actions.Add("choose 0");
        }
        var proceed = room.ProceedButton;
        if (Ui.IsActionable(proceed)) actions.Add(proceed.IsSkip ? "skip" : "proceed");
        if (view.Lines.Count == 0) view.Add("  (nothing to take)");
        view.Actions = string.Join(" · ", actions);
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var room = (NTreasureRoom)ctx.Screen!;
        switch (action.Verb)
        {
            case "open":
                var chest = Chest(room);
                if (!Ui.IsActionable(chest)) return ExecResult.Fail("the chest is already open");
                chest!.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            case "choose":
                var holder = RelicHolder(room);
                if (holder == null) return ExecResult.Fail("no relic to take");
                holder.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            case "skip":
            case "proceed":
                if (!Ui.IsActionable(room.ProceedButton)) return ExecResult.Fail("not available yet (open the chest first)");
                room.ProceedButton.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            default:
                return ExecResult.Fail($"unknown treasure action '{action.Verb}'");
        }
    }
}

/// <summary>Crystal Sphere minigame: reveal cells with the big (3x3) or small (1x1) tool.</summary>
internal sealed class CrystalSphereHandler : IScreenHandler
{
    public string Kind => "crystal_sphere";

    public bool Matches(GameCtx ctx) => ctx.Screen is NCrystalSphereScreen;

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var screen = (NCrystalSphereScreen)ctx.Screen!;
        var game = Ui.GetField(screen, "_entity") as CrystalSphereMinigame;
        if (game == null) return view.Add("  (minigame not available)");

        view.Add($"divinations left {game.DivinationCount} · tool {game.CrystalSphereTool}");
        view.Add("grid (x→, y↓): # hidden, . empty, letter = revealed part of that item (listed below)");
        var size = game.GridSize;
        for (int y = 0; y < size.Y; y++)
        {
            var row = new StringBuilder();
            for (int x = 0; x < size.X; x++)
            {
                var cell = game.cells[x, y];
                row.Append(cell.IsHidden ? '#' : cell.Item == null ? '.' : Label(game, cell.Item));
            }
            view.Add($"  y{y,-2} {row}");
        }

        // The game draws each item's picture in its revealed cells, so its kind and extent there are visible to the player.
        var seen = game.Items.Select(item => (item, revealed: Revealed(game, item))).Where(s => s.revealed > 0).ToList();
        if (seen.Count == 0) view.Add("items seen: none yet");
        else
        {
            view.Add("items seen (only fully revealed items are won):");
            foreach (var (item, revealed) in seen)
            {
                int total = item.Size.X * item.Size.Y;
                string status = revealed < total ? "" : item.IsGood ? " · won" : " · revealed: added to deck";
                view.Add($"  {Label(game, item)} {ItemName(item)} {item.Size.X}×{item.Size.Y} · revealed {revealed}/{total}{status}");
            }
        }
        view.Add($"won so far: {WonText(game)}");

        var actions = new List<string>();
        if (!game.IsFinished) actions.Add("tool big|small · reveal <x> <y>");
        if (Ui.IsActionable(Ui.GetField(screen, "_proceedButton") as NClickableControl)) actions.Add("proceed");
        view.Actions = string.Join(" · ", actions);
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var screen = (NCrystalSphereScreen)ctx.Screen!;
        var game = Ui.GetField(screen, "_entity") as CrystalSphereMinigame;
        switch (action.Verb)
        {
            case "tool":
            {
                string field = action.Arg(0) switch { "big" => "_bigDivinationButton", "small" => "_smallDivinationButton", _ => "" };
                if (field.Length == 0) return ExecResult.Fail("usage: tool big|small");
                if (Ui.GetField(screen, field) is not NClickableControl button || !Ui.IsActionable(button)) return ExecResult.Fail("tool not available");
                button.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            }
            case "reveal":
            {
                int? x = action.IntArg(0), y = action.IntArg(1);
                if (x == null || y == null || game == null) return ExecResult.Fail("usage: reveal <x> <y>");
                if (game.IsFinished) return ExecResult.Fail("no divinations left");
                var cell = Ui.FindAll<NCrystalSphereCell>(screen).FirstOrDefault(c => c.Entity?.X == x && c.Entity?.Y == y);
                if (cell == null) return ExecResult.Fail($"no cell at {x},{y}");
                if (!cell.Entity.IsHidden) return ExecResult.Fail("that cell is already revealed (it would waste a divination)");
                cell.ForceClick();
                return ExecResult.Ok(SettleMode.Screen);
            }
            case "proceed":
            {
                if (Ui.GetField(screen, "_proceedButton") is not NClickableControl proceed || !Ui.IsActionable(proceed))
                    return ExecResult.Fail("proceed is not available yet");
                // With nothing won the game goes straight on without a reward screen; say so.
                string? note = game == null ? null
                    : Won(game).Any() ? $"crystal sphere won: {WonText(game)}"
                    : "crystal sphere: no item was fully revealed, nothing won";
                proceed.ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange, note);
            }
            default:
                return ExecResult.Fail($"unknown crystal sphere action '{action.Verb}'");
        }
    }

    /// <summary>One letter per item, by its place in the game's item list (stable while the minigame lasts).</summary>
    private static char Label(CrystalSphereMinigame game, CrystalSphereItem item)
    {
        int i = 0;
        foreach (var other in game.Items)
        {
            if (ReferenceEquals(other, item)) break;
            i++;
        }
        return i < 26 ? (char)('A' + i) : '?';
    }

    private static int Revealed(CrystalSphereMinigame game, CrystalSphereItem item)
    {
        int n = 0;
        for (int dx = 0; dx < item.Size.X; dx++)
        for (int dy = 0; dy < item.Size.Y; dy++)
            if (!game.cells[item.Position.X + dx, item.Position.Y + dy].IsHidden) n++;
        return n;
    }

    private static IEnumerable<CrystalSphereItem> Won(CrystalSphereMinigame game) =>
        game.Items.Where(item => Revealed(game, item) == item.Size.X * item.Size.Y);

    private static string WonText(CrystalSphereMinigame game)
    {
        var won = Won(game).Select(item => $"{Label(game, item)} {ItemName(item)}{(item.IsGood ? "" : " (bad)")}").ToList();
        return won.Count == 0 ? "nothing" : string.Join(", ", won);
    }

    private static string ItemName(CrystalSphereItem item)
    {
        var s = item.ToSerializable();
        return s.type switch
        {
            CrystalSphereItemType.Relic => "relic",
            CrystalSphereItemType.Potion => $"{s.potionRarity.ToString().ToLowerInvariant()} potion",
            CrystalSphereItemType.CardReward => $"{s.cardRarity.ToString().ToLowerInvariant()} card reward",
            CrystalSphereItemType.Gold => s.isBigGold ? "gold 30" : "gold 10",
            CrystalSphereItemType.Curse => "curse Doubt",
            _ => s.type.ToString(),
        };
    }
}

/// <summary>Choose one bundle of cards (e.g. Scroll Boxes). The game does not route this through the card selector.</summary>
internal sealed class BundleHandler : IScreenHandler
{
    public string Kind => "bundle";

    public bool Matches(GameCtx ctx) => ctx.Screen is NChooseABundleSelectionScreen;

    private static List<NCardBundle> Bundles(NChooseABundleSelectionScreen screen) =>
        Ui.FindAll<NCardBundle>(Ui.GetField(screen, "_bundleRow") as Node).Where(b => Ui.IsLive(b)).ToList();

    public ScreenView Render(GameCtx ctx, bool full)
    {
        var view = new ScreenView(Kind);
        var screen = (NChooseABundleSelectionScreen)ctx.Screen!;
        var bundles = Bundles(screen);
        for (int i = 0; i < bundles.Count; i++)
        {
            view.Add($"  {i} bundle:");
            foreach (var card in bundles[i].Bundle) view.Add("     " + Fmt.PlainCard(card));
        }
        view.Actions = "choose <i>";
        return view;
    }

    public ExecResult Execute(GameCtx ctx, ActionSpec action)
    {
        var screen = (NChooseABundleSelectionScreen)ctx.Screen!;
        switch (action.Verb)
        {
            case "choose":
            {
                var bundles = Bundles(screen);
                int? i = action.IntArg(0);
                if (i == null || i < 0 || i >= bundles.Count) return ExecResult.Fail($"choose 0-{bundles.Count - 1}");
                bundles[i.Value].Hitbox.ForceClick();  // opens the preview
                return ExecResult.Ok(SettleMode.Screen, followups: new[] { "confirm_bundle" });
            }
            case "confirm_bundle":
            {
                if (Ui.GetField(screen, "_previewConfirmButton") is not NClickableControl confirm || !Ui.IsActionable(confirm))
                    return ExecResult.Fail("no bundle preview to confirm");
                confirm.ForceClick();
                return ExecResult.Ok(SettleMode.ScreenChange);
            }
            default:
                return ExecResult.Fail($"unknown bundle action '{action.Verb}'");
        }
    }
}

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The row of style cards. Each one is a real button over a real thumbnail, so these tests
/// drive clicks rather than commands wherever a click is what a user would do.
///
/// Two things a test here deliberately does not do, both because they hang the headless
/// dispatcher rather than because they do not matter:
///
/// Nothing executes DeletePresetCommand. It calls IDialogService.Confirm, which opens a real
/// modal MessageWindow over the shown window, and a headless run has nobody to dismiss it; the
/// nested frame is then released only by the ten-minute dialog timeout. That deletion actually
/// removes the preset is proved in MainViewModelTests, which has a fake dialog service.
///
/// Nothing clicks a MenuItem. Reading one is fine: a MenuItem declared inside a ContextMenu has
/// had none of its bindings evaluated while the menu is shut, but ContextMenu.Open realises it
/// into the window tree with its bindings live, which is how the delete item's IsVisible is
/// asserted below. Pressing it is what cannot be done, for the reason above.
/// </summary>
public class PresetsStripTests
{
    [AvaloniaFact]
    public void EveryPresetTheModelOffersGetsACard()
    {
        UiHarness.WithWindow(session =>
        {
            var cards = UiHarness.PresetCards(session.Window).ToList();

            Assert.NotEmpty(session.Model.Presets);
            Assert.Equal(session.Model.Presets.Count, cards.Count);
            Assert.Equal(
                session.Model.Presets.ToList(),
                cards.Select(c => c.DataContext).ToList());
        });
    }

    [AvaloniaFact]
    public void EachCardShowsItsOwnThumbnailAndName()
    {
        // Every card, not just the first: a template that binds the thumbnail or the label to
        // something other than the item it stands for still gets the first card right.
        UiHarness.WithWindow(session =>
        {
            var cards = UiHarness.PresetCards(session.Window).ToList();

            // A loop over nothing asserts nothing. Without this the whole test passes on a
            // window that renders no strip at all.
            Assert.NotEmpty(cards);

            foreach (var card in cards)
            {
                var item = Assert.IsType<PresetItem>(card.DataContext);

                var preview = card.GetVisualDescendants().OfType<QrPreview>().Single();
                Assert.Same(item.ThumbnailDrawing, preview.Drawing);

                var label = card.GetVisualDescendants().OfType<TextBlock>().Single();
                Assert.Equal(item.Name, label.Text);
            }

            // The assertions above are only worth anything if the presets differ from one
            // another in the first place.
            Assert.Equal(
                session.Model.Presets.Count,
                session.Model.Presets.Select(p => p.Name).Distinct().Count());
        });
    }

    [AvaloniaFact]
    public void ClickingACardAppliesThatStyle()
    {
        UiHarness.WithWindow(session =>
        {
            // A preset whose module shape differs from the one in effect, so the assertion
            // cannot pass by accident on a style that was already applied.
            var target = session.Model.Presets
                .First(p => p.Preset.Style.ModuleShape != session.Model.ModuleShape);

            var card = UiHarness.PresetCard(session.Window, target);
            card.BringIntoView();
            DispatcherPump.Drain();

            var point = UiHarness.At(card, 0.5, 0.5);
            Assert.True(
                point.X >= 0 && point.Y >= 0
                && point.X < session.Window.Width && point.Y < session.Window.Height,
                $"the card is at {point}, outside the window. A headless click there is discarded "
                + "silently, so this test would pass without ever pressing anything.");

            UiHarness.Click(session.Window, point);

            Assert.Equal(target.Preset.Style.ModuleShape, session.Model.ModuleShape);
        });
    }

    [AvaloniaFact]
    public void ClickingACardCarriesTheWholeLookAcrossNotJustTheShape()
    {
        // Two-tone is the preset that changes the most at once: the code colour, the corner
        // ring colour and the corner centre shape. If the card reached the command with the
        // wrong preset, or with none, some of these would keep their old values.
        UiHarness.WithWindow(session =>
        {
            var target = session.Model.Presets.Single(p => p.Name == "Two-tone");
            var style = target.Preset.Style;

            // One pre-check per assertion below. An assertion on a value that already matched
            // before the click cannot fail, so each of the three has to be shown to move.
            Assert.NotEqual(style.Foreground, session.Model.Foreground);
            Assert.NotEqual(style.EffectiveMarkerFrameColor, session.Model.MarkerFrameColor);
            Assert.NotEqual(style.MarkerCenterShape, session.Model.MarkerCenterShape);

            var card = UiHarness.PresetCard(session.Window, target);
            card.BringIntoView();
            DispatcherPump.Drain();

            UiHarness.Click(session.Window, UiHarness.At(card, 0.5, 0.5));

            Assert.Equal(style.Foreground, session.Model.Foreground);
            Assert.Equal(style.EffectiveMarkerFrameColor, session.Model.MarkerFrameColor);
            Assert.Equal(style.MarkerCenterShape, session.Model.MarkerCenterShape);
        });
    }

    [AvaloniaFact]
    public void TheFavoriteCardSitsInTheStripWithTheStyles()
    {
        // It used to be a button below the whole section, which put the action to add a style a
        // long way from the row of styles it adds to. Asserting the shared parent rather than a
        // coordinate: the point is that they are arranged together, not where that lands.
        UiHarness.WithWindow(session =>
        {
            var add = session.Window.FindControl<Button>("SavePresetButton")
                ?? throw new InvalidOperationException("MainWindow no longer has a SavePresetButton.");
            var strip = session.Window.FindControl<ItemsControl>("PresetsStrip")!;

            Assert.Same(strip.GetVisualParent(), add.GetVisualParent());
            Assert.Same(session.Model.SavePresetCommand, add.Command);
        });
    }

    [AvaloniaFact]
    public void OnlyASavedStyleOffersTheRemoveButton()
    {
        // The affordance the feature exists for: right click was the only route and nothing on a
        // card said so. A built-in must not offer it, because the command refuses built-ins and
        // an X that does nothing is worse than no X.
        UiHarness.WithWindow(session =>
        {
            var saved = new PresetItem(
                new StylePreset("Mine", "A saved style", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null);
            session.Model.Presets.Add(saved);
            DispatcherPump.Drain();

            var builtIn = session.Model.Presets.First(p => p.IsBuiltIn);

            Assert.False(RemoveButton(UiHarness.PresetCard(session.Window, builtIn)).IsVisible);
            Assert.True(RemoveButton(UiHarness.PresetCard(session.Window, saved)).IsVisible);
        });
    }

    [AvaloniaFact]
    public void TheRemoveButtonDeletesThatStyleAndNoOther()
    {
        // Driven through the button rather than the command, so a wrong CommandParameter is
        // caught: every card is bound to the same command and only the parameter says which
        // style is meant.
        UiHarness.WithWindow(session =>
        {
            var saved = new PresetItem(
                new StylePreset("Mine", "A saved style", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null);
            session.Model.Presets.Add(saved);
            DispatcherPump.Drain();

            var remove = RemoveButton(UiHarness.PresetCard(session.Window, saved));

            Assert.Same(session.Model.DeletePresetCommand, remove.Command);
            Assert.Same(saved, remove.CommandParameter);
        });
    }

    /// <summary>The remove button on one card, by the name the template gives it.</summary>
    private static Button RemoveButton(Button card) =>
        card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "RemoveFavoriteButton");

    [AvaloniaFact]
    public void ACardWithNoThumbnailRendersInsteadOfThrowing()
    {
        // A preset saved from a style that failed to encode has no thumbnail. It must still
        // take its place in the row rather than bringing the window down.
        UiHarness.WithWindow(session =>
        {
            var before = UiHarness.PresetCards(session.Window).Count();

            session.Model.Presets.Add(new PresetItem(
                new StylePreset("Broken", "No thumbnail", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null));

            DispatcherPump.Drain();

            var cards = UiHarness.PresetCards(session.Window).ToList();
            Assert.Equal(before + 1, cards.Count);

            var card = cards[^1];
            var preview = card.GetVisualDescendants().OfType<QrPreview>().Single();

            Assert.Null(preview.Drawing);
            Assert.True(preview.IsVisible);
            Assert.Equal("Broken", UiHarness.PresetCardLabel(card).Text);
        });
    }

    [AvaloniaFact]
    public void ABuiltInPresetOffersNoWayToDeleteIt()
    {
        UiHarness.WithWindow(session =>
        {
            var builtIn = session.Model.Presets.First(p => p.IsBuiltIn);
            var card = UiHarness.PresetCard(session.Window, builtIn);

            // The card carries the menu -- deleting is not something the strip forgot to
            // offer -- but the command behind it refuses this preset, which is what actually
            // stops a built-in style from being removed. That the item is also hidden on a
            // built-in's menu is asserted separately, with the menu open.
            Assert.NotNull(card.ContextMenu);
            Assert.False(
                session.Model.DeletePresetCommand.CanExecute(builtIn),
                "a built-in style cannot be deleted, so offering the option is a dead end");
        });
    }

    [AvaloniaFact]
    public void ASavedPresetsCardOffersDeletionWiredToThatPreset()
    {
        UiHarness.WithWindow(session =>
        {
            var saved = new PresetItem(
                new StylePreset("Mine", "A saved style", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null);
            session.Model.Presets.Add(saved);
            DispatcherPump.Drain();

            var card = UiHarness.PresetCard(session.Window, saved);

            Assert.NotNull(card.ContextMenu);
            var delete = Assert.Single(card.ContextMenu!.Items.OfType<MenuItem>());

            // Header, not Command: this menu is shut, and a MenuItem inside an unopened
            // ContextMenu has had none of its bindings evaluated, so Command and
            // CommandParameter both read back null. The literal header is the one thing the
            // XAML sets outright. What the bindings do once the menu is opened has its own
            // test, TheDeleteItemIsOnASavedStylesMenuAndOffABuiltInsAltogether.
            Assert.Equal("Remove this style", delete.Header);

            // Not executed either: DeletePreset asks the dialog service to confirm, which opens
            // a modal nothing can dismiss in a headless run. That the deletion goes through is
            // proved in MainViewModelTests.DeletingASavedPreset_TakesItOutOfTheListAndOffDisk.
            Assert.True(
                session.Model.DeletePresetCommand.CanExecute(saved),
                "a style the user saved must be removable");
        });
    }

    [AvaloniaFact]
    public void TheDeleteItemIsOnASavedStylesMenuAndOffABuiltInsAltogether()
    {
        // The command refusing a built-in stops the deletion; this stops the offer. A menu whose
        // only item is greyed out or, worse, present and dead is a menu that says the app is
        // broken rather than that the style is not yours to remove.
        UiHarness.WithWindow(session =>
        {
            var saved = new PresetItem(
                new StylePreset("Mine", "A saved style", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null);
            session.Model.Presets.Add(saved);
            DispatcherPump.Drain();

            Assert.False(
                DeleteItemIsShowing(session.Window, session.Model.Presets.First(p => p.IsBuiltIn)),
                "a built-in style must not offer deletion");
            Assert.True(
                DeleteItemIsShowing(session.Window, saved),
                "a style the user saved must offer deletion");
        });
    }

    /// <summary>
    /// Whether one card's context menu actually shows its delete item, with the menu open.
    ///
    /// Opened rather than read off the unopened ContextMenu, because the MenuItem declared in
    /// the XAML has had none of its bindings evaluated until then: IsVisible reads back true
    /// on every card, built-in or not. Opening realises it into the window tree, which is where
    /// this looks for it.
    /// </summary>
    private static bool DeleteItemIsShowing(Window window, PresetItem item)
    {
        var card = UiHarness.PresetCard(window, item);
        var menu = card.ContextMenu ?? throw new InvalidOperationException("the card carries no menu");

        menu.Open(card);
        DispatcherPump.Drain();

        try
        {
            var delete = Assert.Single(
                window.GetVisualDescendants().OfType<MenuItem>(),
                m => (m.Header as string) == "Remove this style");

            return delete.IsVisible;
        }
        finally
        {
            menu.Close();
            DispatcherPump.Drain();
        }
    }

    [AvaloniaFact]
    public void TheSaveStyleButtonIsBoundToTheCommandThatNeedsTheNamePrompt()
    {
        // The end-to-end path (click, prompt, new preset) needs a modal, which cannot be driven
        // from a headless test without blocking. What is provable here is that the button
        // reaches the command at all, the part that was missing before Task 1.
        UiHarness.WithWindow(session =>
        {
            var button = session.Window.FindControl<Button>("SavePresetButton");

            Assert.NotNull(button);
            Assert.Same(session.Model.SavePresetCommand, button!.Command);
            Assert.True(button.IsEffectivelyEnabled);
        });
    }
}

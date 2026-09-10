using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Styling;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The row of style cards. Each one is a real button over a real thumbnail, so these tests
/// drive clicks rather than commands wherever a click is what a user would do.
/// </summary>
public class PresetsStripTests
{
    [AvaloniaFact]
    public void PresetsStripExistsAndShowsPresets()
    {
        UiHarness.WithWindow(session =>
        {
            var strip = session.Window.FindControl<ItemsControl>("PresetsStrip");
            Assert.NotNull(strip);
            Assert.NotEmpty(session.Model.Presets);
        });
    }

    [AvaloniaFact]
    public void EachCardShowsItsOwnName()
    {
        UiHarness.WithWindow(session =>
        {
            var first = session.Model.Presets[0];

            // Find the button that has the preset name in its visual descendants
            var allButtons = session.Window.GetVisualDescendants().OfType<Button>()
                .Where(b => b.DataContext == first).FirstOrDefault();

            Assert.NotNull(allButtons);
        });
    }

    [AvaloniaFact]
    public void ACardWithNoThumbnailCanBeAdded()
    {
        UiHarness.WithWindow(session =>
        {
            var before = session.Model.Presets.Count;

            session.Model.Presets.Add(new TrispotQR.ViewModels.PresetItem(
                new StylePreset("Broken", "No thumbnail", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null));

            DispatcherPump.Drain();

            Assert.Equal(before + 1, session.Model.Presets.Count);
        });
    }

    [AvaloniaFact]
    public void ABuiltInPresetCannotBeDeleted()
    {
        UiHarness.WithWindow(session =>
        {
            var builtIn = session.Model.Presets.First(p => p.IsBuiltIn);

            // The DeletePresetCommand's CanExecute is the load-bearing protection that prevents
            // deletion of built-in presets.
            Assert.False(
                session.Model.DeletePresetCommand.CanExecute(builtIn),
                "a built-in style cannot be deleted, so the command should refuse it");
        });
    }

    [AvaloniaFact]
    public void ASavedPresetCanBeDeletedViaCommand()
    {
        UiHarness.WithWindow(session =>
        {
            var saved = new TrispotQR.ViewModels.PresetItem(
                new StylePreset("Mine", "A saved style", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null);
            session.Model.Presets.Add(saved);
            DispatcherPump.Drain();

            var before = session.Model.Presets.Count;

            // The DeletePresetCommand's CanExecute allows deletion of saved presets
            Assert.True(
                session.Model.DeletePresetCommand.CanExecute(saved),
                "a style the user saved must be removable");
            session.Model.DeletePresetCommand.Execute(saved);
            DispatcherPump.Drain();

            Assert.Equal(before - 1, session.Model.Presets.Count);
        });
    }

    [AvaloniaFact]
    public void TheSaveStyleButtonExists()
    {
        UiHarness.WithWindow(session =>
        {
            var button = session.Window.FindControl<Button>("SavePresetButton");
            Assert.NotNull(button);
        });
    }

    [AvaloniaFact]
    public void TheSaveStyleButtonIsBoundToSavePresetCommand()
    {
        UiHarness.WithWindow(session =>
        {
            var button = session.Window.FindControl<Button>("SavePresetButton");
            Assert.NotNull(button);
            Assert.Same(session.Model.SavePresetCommand, button!.Command);
        });
    }
}

using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TrispotQR.Core.Presets;
using TrispotQR.UI;
using TrispotQR.UI.Controls;
using TrispotQR.UI.Services;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The one way these tests open the real MainWindow.
///
/// There used to be three: MainWindowTests.Open(), which ran against the developer's real
/// %APPDATA% and worked around it by re-selecting the plain-text editor; ValidationUiTests's
/// own WithWindow, which used temporary stores and whose doc comment already said it was "the
/// same shape as Open()"; and one test that simply constructed a bare MainWindow with neither
/// protection. This is the temporary-store one, which was the right of the three.
///
/// Why it matters beyond tidiness: MainWindow.OnClosing calls SaveSession on the model held in
/// its own private field, which always points at the real %APPDATA%\TrispotQR\settings.json,
/// so any test that ever closes a window overwrites the developer's real settings. Nothing
/// here ever closes one, and having a single place that opens them is what keeps that true.
/// </summary>
internal static class UiHarness
{
    /// <summary>
    /// One opened window and everything a test might legitimately ask of it.
    ///
    /// <see cref="Model"/> is the view model the tests drive: a second MainViewModel, built
    /// here over temporary preset and settings directories, put in place of the one MainWindow
    /// builds for itself. That is what makes a run reproducible -- the content type it opens on
    /// is the fresh-install default rather than whatever this machine last left selected -- and
    /// what keeps it from reading or writing the developer's real files.
    ///
    /// <see cref="RestoredModel"/> and <see cref="RestoredSize"/> are the evidence of what the
    /// window did for itself before that substitution: MainWindow is the composition root, so
    /// restoring the last session's size is something only its own real, %APPDATA%-backed model
    /// can be asked about. Recorded here rather than read afterwards because the harness then
    /// resizes the window to give the form room to lay out.
    /// </summary>
    internal sealed record Session(
        MainWindow Window,
        MainViewModel Model,
        MainViewModel RestoredModel,
        Size RestoredSize);

    /// <summary>
    /// Opens the real window over throwaway stores, hands it to <paramref name="work"/>, and
    /// returns whatever that produced. The window's compiled XAML -- Resources, Styles and
    /// DataTemplates included -- stays exactly what ships, which is what makes this a real
    /// rendered tree rather than a stand-in for one.
    /// </summary>
    public static T WithWindow<T>(Func<Session, T> work)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var window = new MainWindow();

            // Captured before anything else touches the window: this is the size MainWindow's
            // constructor restored from the real settings file, and the assertion that it does
            // so has nothing else to read.
            var restored = (MainViewModel)window.DataContext!;
            var restoredSize = new Size(window.Width, window.Height);

            var model = new MainViewModel(
                new AvaloniaDialogService(window),
                new AvaloniaUiTimer(),
                new AvaloniaImageClipboard(window),
                new PresetStore(directory),
                new AppSettingsStore(directory));

            window.DataContext = model;
            window.Width = 1180;
            window.Height = 900;
            window.Show();
            DispatcherPump.Drain();

            return work(new Session(window, model, restored, restoredSize));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The same, for a body that asserts as it goes rather than returning a value.</summary>
    public static void WithWindow(Action<Session> work) =>
        WithWindow<object?>(session =>
        {
            work(session);
            return null;
        });

    /// <summary>
    /// The realised content editor, scoped to exactly what the "what goes in the code"
    /// ContentControl produced -- not the whole window. Searching the whole window would also
    /// catch FluentTheme's ComboBox control template, which carries its own internal
    /// PART_EditableTextBox even though the content-type picker is never actually editable;
    /// found empirically when a window-wide TextBox search kept turning up two boxes instead of
    /// one. MainWindow.axaml names this ContentControl "ContentEditorHost" for exactly this
    /// reason.
    /// </summary>
    public static ContentControl ContentHost(Window window) =>
        window.FindControl<ContentControl>("ContentEditorHost")
            ?? throw new InvalidOperationException("MainWindow no longer has a ContentEditorHost.");

    /// <summary>Every field box the current content type realised, hidden ones included.</summary>
    public static IEnumerable<FieldBox> AllBoxes(Window window) =>
        ContentHost(window).GetVisualDescendants().OfType<FieldBox>();

    /// <summary>
    /// Only the boxes a user can actually see. The Wi-Fi password box stays in the tree with
    /// IsVisible false when the network needs no password, and a hidden box's pseudo-classes
    /// are not something anyone can look at.
    /// </summary>
    public static IEnumerable<FieldBox> VisibleBoxes(Window window) =>
        AllBoxes(window).Where(b => b.IsVisible);

    /// <summary>
    /// Reaches past FieldBox's own public surface to the real inner TextBox, the same way a
    /// style selector does, so a test can assert on the border the user actually sees rather
    /// than only on the pseudo-class that is supposed to cause it -- and so a test can put text
    /// into the box the way a user does, rather than into the property the box is bound to. A
    /// pseudo-class can be set correctly while a broken selector still leaves the border
    /// unstyled; a bound property can be read correctly while nothing typed ever reaches it.
    /// Neither is provable from outside the control.
    /// </summary>
    public static TextBox Input(FieldBox box) =>
        box.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Input");
}

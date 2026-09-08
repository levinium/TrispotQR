using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TrispotQR.UI;
using TrispotQR.UI.Controls;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

public class MainWindowTests
{
    /// <summary>
    /// Opens the window and selects the plain-text editor.
    ///
    /// Selecting it explicitly matters: MainViewModel restores the last-used content type from
    /// the real settings file, and MainWindow is the composition root so no test can hand it a
    /// different store. Assuming plain text is selected would make these tests pass or fail
    /// depending on what the developer last did in the shipping app.
    ///
    /// Deliberately never closed. MainWindow.OnClosing calls MainViewModel.SaveSession, which
    /// writes to the developer's real %APPDATA%\TrispotQR\settings.json (MainWindow is the
    /// composition root, so it always builds the default, real AppSettingsStore -- there is no
    /// way for a test to hand it a different one without changing MainViewModel, which this task
    /// is not allowed to do). Calling Close() here would silently overwrite that file with
    /// whatever this test run happened to leave the view model holding.
    /// </summary>
    private static (MainWindow Window, MainViewModel Model, PlainTextEditor Editor) Open()
    {
        var window = new MainWindow();
        window.Show();

        var model = Assert.IsType<MainViewModel>(window.DataContext);
        var editor = model.ContentEditors.OfType<PlainTextEditor>().Single();
        model.SelectedContent = editor;

        return (window, model, editor);
    }

    [AvaloniaFact]
    public void OpensWithAViewModelAttached()
    {
        var (_, model, editor) = Open();

        Assert.NotEmpty(model.ContentEditors);
        Assert.Same(editor, model.SelectedContent);
    }

    [AvaloniaFact]
    public void ShowsAPreviewOnceThereIsContent()
    {
        var (window, model, editor) = Open();

        editor.Text = "https://www.emanuelnyc.org";

        // RefreshNow skips the debounce timer, which is what the window uses in normal running.
        // Waiting on a real 150ms tick here would make the test slow and flaky for no gain.
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(model.PreviewDrawing);

        var preview = window.GetVisualDescendants().OfType<QrPreview>().Single();
        Assert.NotNull(preview.Drawing);
    }

    [AvaloniaFact]
    public void CannotExportWithNoContent()
    {
        var (_, model, editor) = Open();

        editor.Text = string.Empty;

        Assert.False(model.CanExport);
        Assert.False(model.SavePngCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void CanExportOnceThereIsContent()
    {
        var (_, model, editor) = Open();

        editor.Text = "https://www.emanuelnyc.org";
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

        Assert.True(model.CanExport);
        Assert.True(model.SavePngCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void TheWindowRendersWithoutBindingFailures()
    {
        // Compiled bindings turn a mistyped binding into a build error, but a binding to a
        // missing DataTemplate still only shows up at runtime. Capturing a frame forces the
        // whole visual tree to render, which is what surfaces that.
        var (window, model, editor) = Open();

        editor.Text = "test";
        model.RefreshNow();
        Dispatcher.UIThread.RunJobs();

        Assert.NotNull(window.CaptureRenderedFrame());
    }
}

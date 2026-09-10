using Avalonia.Controls;
using Avalonia.Interactivity;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI;

/// <summary>
/// The shell, and the composition root.
///
/// The view model is built here rather than in App because two of its services need a window:
/// the dialog service owns the parent for every modal, and the clipboard reaches the system
/// through a TopLevel. Constructing them anywhere else would mean handing the window in later
/// and leaving the view model half-built until then.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _model;

    // InitializeComponent(), not AvaloniaXamlLoader.Load(this) -- see MessageWindow.axaml.cs
    // for the full explanation. Load(this) builds the visual tree and registers x:Names in
    // the NameScope, but never populates the compiler-generated backing fields, since that
    // assignment lives in InitializeComponent()'s own generated body. VersionLabel below is
    // one of those fields, so Load(this) would make the very next line throw.
    public MainWindow()
    {
        InitializeComponent();

        VersionLabel.Text = AppInfo.DisplayVersion;

        _model = new MainViewModel(
            new AvaloniaDialogService(this),
            new AvaloniaUiTimer(),
            new AvaloniaImageClipboard(this));

        DataContext = _model;

        // OnClosing has always written these; nothing read them back, so the window reopened
        // at the XAML's 1000x700 however the user had left it. The WPF app restores them the
        // same way (MainWindow.xaml.cs), and it is the same settings file behind both.
        var settings = _model.LoadedSettings;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _model.SaveSession(Width, Height);
        base.OnClosing(e);
    }

    /// <summary>
    /// The gear menu's Settings entry.
    ///
    /// OpenSettings is on the view model because deciding what to persist afterwards is its
    /// job; showing the window is the dialog service's, which it reaches through EditSettings.
    /// Asked of DataContext rather than of the private field, because the model the window is
    /// showing is the one whose settings the user means -- and in the tests those are
    /// deliberately not the same object.
    /// </summary>
    private void OnSettingsClicked(object? sender, RoutedEventArgs e) =>
        (DataContext as MainViewModel)?.OpenSettings();

    /// <summary>
    /// async void because that is what a click handler is. ShowDialog's task completes when
    /// the window closes and faults for nothing this window does, so there is no result to
    /// take and nothing to lose if it ever did.
    /// </summary>
    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);
}

using Avalonia.Controls;
using TrispotQR.UI.Services;
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
    // assignment lives in InitializeComponent()'s own generated body. This window has no named
    // fields to null out today, but the failure mode is silent and would only surface the day
    // one gets added, so it is not worth reintroducing the bug Load(this) caused there.
    public MainWindow()
    {
        InitializeComponent();

        _model = new MainViewModel(
            new AvaloniaDialogService(this),
            new AvaloniaUiTimer(),
            new AvaloniaImageClipboard(this));

        DataContext = _model;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _model.SaveSession(Width, Height);
        base.OnClosing(e);
    }
}

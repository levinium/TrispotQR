using System.Collections.ObjectModel;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;
using TrispotQR.ViewModels;

namespace TrispotQR.UI;

/// <summary>
/// Stands for the "add a favorite" card at the end of the styles strip.
///
/// An empty marker, and deliberately not a <see cref="PresetItem"/> with a special name: the
/// strip picks its template by type, so a real type is what keeps the add card and a style
/// that happens to be called "Favorite" from ever being confused for one another.
///
/// It lives in the view rather than the view model. Nothing about the list of styles changes
/// because the UI offers a way to add one, and a view model carrying a fake style would have to
/// be filtered back out by every consumer that counts styles.
/// </summary>
public sealed class AddFavoriteCard
{
    public static AddFavoriteCard Instance { get; } = new();
}

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

    /// <summary>The styles strip's real source: every preset, then the add card.</summary>
    private readonly ObservableCollection<object> _stripItems = [];

    /// <summary>
    /// The view model currently being listened to, held so its handlers can be detached when the
    /// data context changes. Without this a substituted view model leaves the previous one still
    /// driving the strip and still raising confirmations into this window.
    /// </summary>
    private MainViewModel? _watchedModel;

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

        // The strip shows the styles plus one trailing add card, so its source is mirrored here
        // rather than bound straight to Presets: an ItemsControl has no footer, and the add card
        // has to be the last item rather than a button sitting under the section.
        //
        // Rebuilt on DataContextChanged, not just once. The window is the composition root, but
        // the UI tests substitute a view model over temporary stores after construction, and a
        // strip wired only to the constructor's model would quietly go on showing the wrong one.
        PresetsStrip.ItemsSource = _stripItems;
        DataContextChanged += (_, _) => WatchModel();
        WatchModel();

        // One handler for every colour picker the window will ever hold, present or not yet
        // realised. Two of the five live inside panels that stay collapsed until a checkbox is
        // ticked, so subscribing to instances at load would silently miss them; the event
        // bubbles, so the window hears them all. The picker only raises this when the colour
        // actually changed, which is what keeps opening and dismissing a picker out of the list.
        AddHandler(
            Controls.ColorPicker.ColorCommittedEvent,
            (_, e) =>
            {
                if (e.Source is Controls.ColorPicker picker)
                {
                    _model.RecordRecentColor(picker.SelectedColor);
                }
            });

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
    /// Shows a message briefly and fades it out again.
    /// </summary>
    /// <remarks>
    /// A keyframe animation run from code rather than a style trigger, because the toast has no
    /// state to be in: it is one message, shown once, and a second copy while the first is still
    /// fading has to restart it rather than queue behind it.
    ///
    /// The timings match the WPF window's: a fifth of a second to appear, most of two seconds to
    /// read, then out. Long enough to notice without being long enough to sit in front of the
    /// save buttons while someone is still working.
    /// </remarks>
    private void ShowToast(string message)
    {
        ToastText.Text = message;

        var fade = new Animation
        {
            Duration = TimeSpan.FromSeconds(2.5),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(0.06), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(0.76), Setters = { new Setter(OpacityProperty, 1d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 0d) } },
            },
        };

        // Fire and forget: the animation owns its own lifetime, and nothing waits for a toast.
        _ = fade.RunAsync(Toast);
    }

    /// <summary>
    /// Points everything the window listens to at whichever view model is current.
    ///
    /// Both the strip mirror and the toast follow the data context rather than the field this
    /// window constructed, and they have to agree: a window whose strip showed one model while
    /// its confirmations came from another would be telling the truth about neither.
    /// </summary>
    private void WatchModel()
    {
        if (_watchedModel is not null)
        {
            _watchedModel.Presets.CollectionChanged -= OnPresetsChanged;
            _watchedModel.Announcement -= OnAnnouncement;
        }

        _watchedModel = DataContext as MainViewModel;

        if (_watchedModel is not null)
        {
            _watchedModel.Presets.CollectionChanged += OnPresetsChanged;
            _watchedModel.Announcement += OnAnnouncement;
        }

        RefillStrip();
    }

    private void OnAnnouncement(object? sender, string message) => ShowToast(message);

    private void OnPresetsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefillStrip();

    /// <summary>
    /// Rebuilds the mirror wholesale rather than replaying the change.
    ///
    /// The strip holds a dozen items and is rebuilt only when a style is saved or removed, so
    /// the cost is nothing and the alternative -- translating each add, remove and move into an
    /// index that accounts for the trailing card -- is arithmetic with an off-by-one waiting in
    /// it every time.
    /// </summary>
    private void RefillStrip()
    {
        _stripItems.Clear();

        foreach (var preset in (DataContext as MainViewModel)?.Presets ?? [])
        {
            _stripItems.Add(preset);
        }

        _stripItems.Add(AddFavoriteCard.Instance);
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

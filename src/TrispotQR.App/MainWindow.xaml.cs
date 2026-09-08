using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TrispotQR.App.Services;
using TrispotQR.App.ViewModels;
using TrispotQR.App.Views;

namespace TrispotQR.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
        : this(new MainViewModel(new DialogService()))
    {
    }

    /// <summary>
    /// Takes an already-built view model. The app uses the parameterless constructor; this
    /// overload lets the layout be exercised in tests without touching the real presets
    /// folder or putting a window on screen.
    /// </summary>
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        DataContext = _viewModel;
        _viewModel.Announcement += (_, message) => ShowToast(message);

        VersionLabel.Text = AppInfo.DisplayVersion;

        var settings = _viewModel.LoadedSettings;
        Width = settings.WindowWidth;
        Height = settings.WindowHeight;

        Closing += (_, _) => _viewModel.SaveSession(Width, Height);
    }

    /// <summary>
    /// Opens the gear's own context menu on a plain left click. A ContextMenu normally
    /// waits for a right click, which nobody would think to try on a button.
    /// </summary>
    private void OnGearClicked(object sender, RoutedEventArgs e)
    {
        if (GearButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = GearButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnSettingsClicked(object sender, RoutedEventArgs e) => _viewModel.OpenSettings();

    private void OnAboutClicked(object sender, RoutedEventArgs e)
    {
        var about = new AboutWindow { Owner = this, Icon = Icon };
        about.ShowDialog();
    }

    /// <summary>
    /// Fades a short confirmation in, holds it long enough to read, then fades it out.
    ///
    /// Copying to the clipboard otherwise has no visible effect at all, which leaves the
    /// user unsure whether the button did anything. Starting the animation again simply
    /// replaces the running one, so repeated copies restart the message rather than
    /// queueing up behind each other.
    /// </summary>
    private void ShowToast(string message)
    {
        ToastText.Text = message;

        var fade = new DoubleAnimationUsingKeyFrames();
        fade.KeyFrames.Add(Frame(0, 0));
        fade.KeyFrames.Add(Frame(1, 0.15));
        fade.KeyFrames.Add(Frame(1, 1.9));
        fade.KeyFrames.Add(Frame(0, 2.5));

        Toast.BeginAnimation(OpacityProperty, fade);
    }

    private static LinearDoubleKeyFrame Frame(double opacity, double seconds) =>
        new(opacity, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)));
}

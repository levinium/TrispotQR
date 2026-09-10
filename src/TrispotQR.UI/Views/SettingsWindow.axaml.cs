using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Views;

/// <summary>
/// Preferences, as distinct from the session state the app also remembers.
///
/// The theme applies as soon as it is chosen rather than on Done, because the whole point of
/// choosing an appearance is seeing it. Everything else is read back out of the controls when
/// Done is pressed, so any other way out of the window abandons it.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppTheme _themeOnOpen;
    private bool _loaded;
    private bool _confirmed;

    /// <summary>
    /// Opens on the defaults, and exists so the compiled XAML stays reachable from the runtime
    /// loader: without a public parameterless constructor the Avalonia compiler reports
    /// AVLN3001 and the resource cannot be loaded that way at all. MessageWindow,
    /// TextPromptWindow and AboutWindow all have one already.
    ///
    /// Delegates to the real constructor rather than standing alone the way TextPromptWindow's
    /// does, because this window has state that a bare InitializeComponent() would leave
    /// wrong in two ways: Result is not nullable and would be unassigned, and every control
    /// would sit blank while Result claimed the defaults. Handing the defaults to the one
    /// constructor that knows how to show them keeps the window honest whichever way it is
    /// built. The app itself always uses the overload.
    /// </summary>
    public SettingsWindow()
        : this(AppSettings.Default)
    {
    }

    // InitializeComponent(), never AvaloniaXamlLoader.Load(this) -- see the comment in
    // MessageWindow.axaml.cs. Load(this) populates the NameScope but leaves the generated
    // fields null, so ThemeChoice.SelectedIndex below would throw.
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();

        _themeOnOpen = settings.Theme;
        Result = settings;

        // Index order matches the AppTheme enum, which the two conversions below rely on.
        ThemeChoice.SelectedIndex = settings.Theme switch
        {
            AppTheme.Light => 1,
            AppTheme.Dark => 2,
            _ => 0,
        };

        WarnRisky.IsChecked = settings.WarnOnRiskyCodes;
        RememberStyle.IsChecked = settings.RememberLastStyle;
        SaveFolder.Text = settings.DefaultSaveDirectory ?? string.Empty;

        SizeSmall.IsChecked = settings.DefaultPixelSize == 512;
        SizePrint.IsChecked = settings.DefaultPixelSize == 2048;
        SizeMedium.IsChecked = !SizeSmall.IsChecked.GetValueOrDefault() && !SizePrint.IsChecked.GetValueOrDefault();

        // Set last, so assigning the values above does not count as the user choosing them.
        _loaded = true;
    }

    /// <summary>
    /// The settings as edited. Valid once the window has closed with Done; before that, and
    /// after any other way out, it is still whatever was handed in.
    /// </summary>
    public AppSettings Result { get; private set; }

    public static async Task<AppSettings?> ShowAsync(Window owner, AppSettings current)
    {
        var window = new SettingsWindow(current);
        await window.ShowDialog(owner);
        return window._confirmed ? window.Result : null;
    }

    private AppTheme SelectedTheme => ThemeChoice.SelectedIndex switch
    {
        1 => AppTheme.Light,
        2 => AppTheme.Dark,
        _ => AppTheme.FollowWindows,
    };

    private int SelectedSize =>
        SizeSmall.IsChecked == true ? 512
        : SizePrint.IsChecked == true ? 2048
        : 1024;

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Closing with the title bar X, or with Cancel, abandons the edits, so the live theme
        // preview has to be put back the way it was found.
        if (!_confirmed)
        {
            ThemeSwitcher.Apply(_themeOnOpen);
        }
    }

    private void OnThemeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loaded)
        {
            ThemeSwitcher.Apply(SelectedTheme);
        }
    }

    /// <summary>
    /// async void because that is what a click handler is. The two calls it awaits are the
    /// platform's own folder picker and the resolution of a path this window itself put in the
    /// box, neither of which faults; there is no user work to lose if one ever did.
    /// </summary>
    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = "Choose where codes are saved",
            AllowMultiple = false,
        };

        // Directory.Exists is the guard, not just a convenience: it answers false rather than
        // throwing for every malformed path, including the embedded NUL that makes
        // TryGetFolderFromPathAsync throw ArgumentException where AvaloniaDialogService has to
        // catch it. Nothing malformed reaches the resolution below.
        if (!string.IsNullOrWhiteSpace(SaveFolder.Text) && Directory.Exists(SaveFolder.Text))
        {
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(SaveFolder.Text);
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(options);

        // TryGetLocalPath returns null for a location with no file system path, such as a cloud
        // provider on Android. A folder the app cannot name is one it cannot save into.
        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            SaveFolder.Text = path;
        }
    }

    private void OnClearFolder(object? sender, RoutedEventArgs e) => SaveFolder.Text = string.Empty;

    private void OnResetDefaults(object? sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.Default;

        ThemeChoice.SelectedIndex = 0;
        WarnRisky.IsChecked = defaults.WarnOnRiskyCodes;
        RememberStyle.IsChecked = defaults.RememberLastStyle;
        SaveFolder.Text = string.Empty;
        SizeMedium.IsChecked = true;

        // Does not close the window: reset is an offer to start again, not a way out.
        ThemeSwitcher.Apply(AppTheme.FollowWindows);
    }

    private void OnDone(object? sender, RoutedEventArgs e)
    {
        // Edited rather than rebuilt, so the session state this window has no controls for
        // (the last folder saved to, the window size, the selected tab) survives.
        Result = Result with
        {
            Theme = SelectedTheme,
            WarnOnRiskyCodes = WarnRisky.IsChecked == true,
            RememberLastStyle = RememberStyle.IsChecked == true,
            DefaultPixelSize = SelectedSize,
            DefaultSaveDirectory = string.IsNullOrWhiteSpace(SaveFolder.Text) ? null : SaveFolder.Text,
        };

        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}

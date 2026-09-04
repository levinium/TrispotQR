using System.IO;
using System.Windows;
using Microsoft.Win32;
using TrispotQR.App.Services;
using TrispotQR.Core.Presets;

namespace TrispotQR.App.Views;

/// <summary>
/// Preferences, as distinct from the session state the app also remembers.
///
/// The theme applies as soon as it is clicked rather than on Done, because the whole point
/// of choosing an appearance is seeing it. Everything else is read back out of the controls
/// when the window closes.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly AppTheme _themeOnOpen;
    private bool _loaded;

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

    /// <summary>The settings as edited. Valid once the window has closed.</summary>
    public AppSettings Result { get; private set; }

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

    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loaded)
        {
            ThemeManager.Apply(SelectedTheme);
        }
    }

    private void OnBrowseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where codes are saved" };

        if (!string.IsNullOrWhiteSpace(SaveFolder.Text) && Directory.Exists(SaveFolder.Text))
        {
            dialog.InitialDirectory = SaveFolder.Text;
        }

        if (dialog.ShowDialog() == true)
        {
            SaveFolder.Text = dialog.FolderName;
        }
    }

    private void OnClearFolder(object sender, RoutedEventArgs e) => SaveFolder.Text = string.Empty;

    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.Default;

        ThemeChoice.SelectedIndex = 0;
        WarnRisky.IsChecked = defaults.WarnOnRiskyCodes;
        RememberStyle.IsChecked = defaults.RememberLastStyle;
        SaveFolder.Text = string.Empty;
        SizeMedium.IsChecked = true;

        ThemeManager.Apply(AppTheme.FollowWindows);
    }

    private void OnDone(object sender, RoutedEventArgs e)
    {
        Result = Result with
        {
            Theme = SelectedTheme,
            WarnOnRiskyCodes = WarnRisky.IsChecked == true,
            RememberLastStyle = RememberStyle.IsChecked == true,
            DefaultPixelSize = SelectedSize,
            DefaultSaveDirectory = string.IsNullOrWhiteSpace(SaveFolder.Text) ? null : SaveFolder.Text,
        };

        DialogResult = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Closing with the title bar X rather than Done abandons the edits, so the live
        // theme preview has to be put back the way it was found.
        if (DialogResult != true)
        {
            ThemeManager.Apply(_themeOnOpen);
        }
    }
}

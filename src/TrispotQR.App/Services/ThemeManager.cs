using System.Windows;
using Microsoft.Win32;
using TrispotQR.Core.Presets;

namespace TrispotQR.App.Services;

/// <summary>
/// Swaps the palette dictionary to change the application's appearance.
///
/// The merged dictionaries are ordered deliberately: slot 0 is the palette and everything
/// after it is styles. Switching theme replaces slot 0 in place, which is why every colour
/// reference in the XAML uses DynamicResource. A StaticResource resolves once when its
/// element is loaded and would simply ignore the swap, leaving a half-changed window.
/// </summary>
public static class ThemeManager
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // Absolute pack URIs, not relative ones. A relative URI is resolved against the entry
    // assembly, which is TrispotQR.exe when the app runs but the test runner under xUnit, so
    // a relative path silently fails to locate the palette outside the app.
    private static readonly Uri LightPalette = new("pack://application:,,,/TrispotQR;component/Theme.Light.xaml");
    private static readonly Uri DarkPalette = new("pack://application:,,,/TrispotQR;component/Theme.Dark.xaml");

    /// <summary>The appearance currently on screen, after resolving FollowWindows.</summary>
    public static bool IsDark { get; private set; }

    /// <summary>What was last asked for, which may still be FollowWindows.</summary>
    public static AppTheme Requested { get; private set; } = AppTheme.FollowWindows;

    /// <summary>
    /// Re-evaluates the current choice. Only does anything when following Windows, and is
    /// what makes that setting live rather than read once at startup.
    /// </summary>
    public static void Refresh()
    {
        if (Requested == AppTheme.FollowWindows)
        {
            Apply(AppTheme.FollowWindows);
        }
    }

    public static void Apply(AppTheme theme)
    {
        Requested = theme;

        var dark = theme switch
        {
            AppTheme.Light => false,
            AppTheme.Dark => true,
            _ => WindowsPrefersDark(),
        };

        var resources = Application.Current?.Resources;
        if (resources is null || resources.MergedDictionaries.Count == 0)
        {
            return;
        }

        IsDark = dark;
        resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = dark ? DarkPalette : LightPalette,
        };
    }

    /// <summary>
    /// Reads the Windows setting. The value is named for the light theme and inverted:
    /// AppsUseLightTheme is 0 when Windows is in dark mode. A missing value means the
    /// setting has never been changed, which is light.
    /// </summary>
    public static bool WindowsPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch (Exception)
        {
            // A locked-down or unreadable registry is not worth failing over.
            return false;
        }
    }
}

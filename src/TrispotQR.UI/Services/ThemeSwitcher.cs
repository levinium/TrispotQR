using Avalonia;
using Avalonia.Styling;
using TrispotQR.Core.Presets;

namespace TrispotQR.UI.Services;

/// <summary>
/// Applies the chosen appearance.
///
/// Much smaller than the WPF ThemeManager it replaces, because Avalonia does the work: the
/// palettes live in ThemeDictionaries and RequestedThemeVariant picks between them, so there
/// is no dictionary to swap. ThemeVariant.Default already means "follow the operating system"
/// on every platform, which is why there is no registry read here -- and there could not be
/// one, since this assembly has to run on macOS and Linux too.
/// </summary>
public static class ThemeSwitcher
{
    /// <summary>What was last asked for, which may still be FollowWindows.</summary>
    public static AppTheme Requested { get; private set; } = AppTheme.FollowWindows;

    public static void Apply(AppTheme theme)
    {
        Requested = theme;

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}

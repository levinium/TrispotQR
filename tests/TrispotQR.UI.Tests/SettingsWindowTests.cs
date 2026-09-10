using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The preferences window, as distinct from the session state the app also remembers.
///
/// Every test that touches the appearance puts it back to FollowWindows in a finally.
/// RequestedThemeVariant is application state and the whole suite shares one process, so a
/// leaked dark theme would make a later, unrelated test fail for a reason nobody could find.
/// Closing the window is itself one of the ways the theme moves -- that is the behaviour under
/// test here -- so the restore has to come after the close, not instead of it.
/// </summary>
public class SettingsWindowTests
{
    [AvaloniaFact]
    public void EverySettingHandedInIsShowingWhenTheWindowOpens()
    {
        var settings = AppSettings.Default with
        {
            Theme = AppTheme.Dark,
            WarnOnRiskyCodes = false,
            RememberLastStyle = false,
            DefaultPixelSize = 2048,
            DefaultSaveDirectory = @"C:\Codes",
        };

        WithSettings(settings, window =>
        {
            Assert.Equal(2, Theme(window).SelectedIndex);
            Assert.False(Toggle(window, "WarnRisky").IsChecked);
            Assert.False(Toggle(window, "RememberStyle").IsChecked);
            Assert.Equal(@"C:\Codes", Folder(window).Text);

            Assert.True(Toggle(window, "SizePrint").IsChecked);
            Assert.False(Toggle(window, "SizeSmall").IsChecked);
            Assert.False(Toggle(window, "SizeMedium").IsChecked);
        });
    }

    [AvaloniaFact]
    public void ASizeThatIsNeitherSmallNorPrintShowsAsMedium()
    {
        // settings.json is a plain file with no validation on the way in, and DefaultPixelSize
        // is a bare int, so a hand edit or an older build can hand this window a size none of
        // the three buttons stands for. Three unchecked radio buttons would say the app had no
        // size at all, when what it will actually export at is 1024.
        WithSettings(AppSettings.Default with { DefaultPixelSize = 777 }, window =>
        {
            Assert.True(Toggle(window, "SizeMedium").IsChecked);
            Assert.False(Toggle(window, "SizeSmall").IsChecked);
            Assert.False(Toggle(window, "SizePrint").IsChecked);
        });
    }

    [AvaloniaFact]
    public void ChoosingDarkAppliesItStraightAwayThoughOpeningTheWindowDoesNot()
    {
        try
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);

            WithSettings(AppSettings.Default with { Theme = AppTheme.Light }, window =>
            {
                // Assigning the remembered choice into the combo box in the constructor is not
                // the user choosing it. Without the _loaded guard the window would repaint the
                // whole app on the way up, and then "restore the theme on cancel" would restore
                // the one the window itself had just imposed.
                Assert.Equal(AppTheme.FollowWindows, ThemeSwitcher.Requested);

                Theme(window).SelectedIndex = 2;
                DispatcherPump.Drain();

                // Nothing has been clicked. The point of choosing an appearance is seeing it,
                // so the choice alone has to reach the running application.
                Assert.Equal(AppTheme.Dark, ThemeSwitcher.Requested);
                Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void CancellingPutsBackTheThemeThatWasInEffectOnOpen()
    {
        try
        {
            ThemeSwitcher.Apply(AppTheme.Light);

            WithSettings(AppSettings.Default with { Theme = AppTheme.Light }, window =>
            {
                Theme(window).SelectedIndex = 2;
                DispatcherPump.Drain();
                Assert.Equal(AppTheme.Dark, ThemeSwitcher.Requested);

                Press(window, "CancelButton");

                // A preview that outlives the window it was previewed in is not a preview.
                Assert.False(window.IsVisible, "cancel must close the window");
                Assert.Equal(AppTheme.Light, ThemeSwitcher.Requested);
                Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void ClosingWithTheTitleBarAlsoPutsBackTheThemeThatWasInEffectOnOpen()
    {
        try
        {
            ThemeSwitcher.Apply(AppTheme.Light);

            WithSettings(AppSettings.Default with { Theme = AppTheme.Light }, window =>
            {
                Theme(window).SelectedIndex = 2;
                DispatcherPump.Drain();
                Assert.Equal(AppTheme.Dark, ThemeSwitcher.Requested);

                // The X, which reaches no handler of ours at all. Restoring only from the
                // Cancel handler would leave the app painted dark with nothing saying so.
                window.Close();
                DispatcherPump.Drain();

                Assert.Equal(AppTheme.Light, ThemeSwitcher.Requested);
                Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void DoneCarriesEveryEditBackOutAndKeepsTheThemeItChose()
    {
        try
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);

            var settings = AppSettings.Default with
            {
                ContentTypeIndex = 3,
                LastSaveDirectory = @"C:\Somewhere",
                WindowWidth = 900,
            };

            WithSettings(settings, window =>
            {
                Theme(window).SelectedIndex = 2;
                Press(window, "WarnRisky");
                Press(window, "RememberStyle");
                Press(window, "SizePrint");
                Folder(window).Text = @"C:\Codes";
                DispatcherPump.Drain();

                Press(window, "DoneButton");
                Assert.False(window.IsVisible, "done must close the window");

                var result = window.Result;
                Assert.Equal(AppTheme.Dark, result.Theme);
                Assert.False(result.WarnOnRiskyCodes);
                Assert.False(result.RememberLastStyle);
                Assert.Equal(2048, result.DefaultPixelSize);
                Assert.Equal(@"C:\Codes", result.DefaultSaveDirectory);

                // The record carries session state this window has no controls for, and
                // rebuilding it from scratch rather than editing it would silently throw that
                // away: the app would reopen at the wrong size on the wrong tab.
                Assert.Equal(3, result.ContentTypeIndex);
                Assert.Equal(@"C:\Somewhere", result.LastSaveDirectory);
                Assert.Equal(900, result.WindowWidth);

                // Done is the one close that keeps the preview rather than undoing it.
                Assert.Equal(AppTheme.Dark, ThemeSwitcher.Requested);
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void AnEmptyFolderBoxMeansNullRatherThanAnEmptyString()
    {
        // Not pedantry about a string: MainViewModel reads DefaultSaveDirectory ?? LastSaveDirectory,
        // so an empty string is a folder the save dialog would try to open and fail to, rather
        // than the absence of a preference that lets the app follow wherever you saved last.
        WithSettings(AppSettings.Default with { DefaultSaveDirectory = @"C:\Codes" }, window =>
        {
            Press(window, "ClearFolderButton");
            Assert.True(string.IsNullOrEmpty(Folder(window).Text), "Clear must empty the box");

            Press(window, "DoneButton");

            Assert.Null(window.Result.DefaultSaveDirectory);
        });
    }

    [AvaloniaFact]
    public void ResetToDefaultsPutsTheControlsBackAndLeavesTheWindowOpen()
    {
        try
        {
            ThemeSwitcher.Apply(AppTheme.Dark);

            var settings = AppSettings.Default with
            {
                Theme = AppTheme.Dark,
                WarnOnRiskyCodes = false,
                RememberLastStyle = false,
                DefaultPixelSize = 512,
                DefaultSaveDirectory = @"C:\Codes",
            };

            WithSettings(settings, window =>
            {
                Press(window, "ResetDefaultsButton");

                Assert.Equal(0, Theme(window).SelectedIndex);
                Assert.True(Toggle(window, "WarnRisky").IsChecked);
                Assert.True(Toggle(window, "RememberStyle").IsChecked);
                Assert.True(Toggle(window, "SizeMedium").IsChecked);
                Assert.True(string.IsNullOrEmpty(Folder(window).Text));

                // Live, like every other appearance change here.
                Assert.Equal(AppTheme.FollowWindows, ThemeSwitcher.Requested);

                // Reset is an offer to start again, not a way out. Closing on it would throw
                // away the reset it had just performed.
                Assert.True(window.IsVisible, "reset must leave the window open");
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    private static ComboBox Theme(SettingsWindow window) => Named<ComboBox>(window, "ThemeChoice");

    private static TextBox Folder(SettingsWindow window) => Named<TextBox>(window, "SaveFolder");

    /// <summary>A check box or a radio button, which share the IsChecked these tests read.</summary>
    private static ToggleButton Toggle(SettingsWindow window, string name) =>
        Named<ToggleButton>(window, name);

    private static T Named<T>(SettingsWindow window, string name)
        where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    /// <summary>
    /// A real click at the rendered control, not a call to its handler: a button that lays out
    /// off the edge of the window is a button nobody can press, and the click would be
    /// discarded silently rather than failing.
    /// </summary>
    private static void Press(SettingsWindow window, string name)
    {
        var control = Named<Control>(window, name);
        var point = UiHarness.At(control, 0.5, 0.5);

        Assert.True(
            point.X >= 0 && point.Y >= 0 && point.X < window.Bounds.Width && point.Y < window.Bounds.Height,
            $"{name} is laid out outside the window, where a click goes nowhere");

        UiHarness.Click(window, point);
    }

    /// <summary>
    /// Shows the window non-modally so the body can drive it. ShowDialog would block on a
    /// dispatcher frame and never hand control back, and a modal with nothing to dismiss it
    /// hangs the whole suite until the ten minute dialog timeout.
    /// </summary>
    private static void WithSettings(AppSettings settings, Action<SettingsWindow> work)
    {
        var window = new SettingsWindow(settings);
        window.Show();
        DispatcherPump.Drain();

        try
        {
            work(window);
        }
        finally
        {
            if (window.IsVisible)
            {
                window.Close();
            }

            // After the close, never instead of it: closing is what puts the theme back to
            // whatever the window was opened on, which for most of these is not FollowWindows.
            DispatcherPump.Drain();
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }
}

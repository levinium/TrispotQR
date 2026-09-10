using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The palette and the switch that picks between its two halves.
///
/// Every test here puts the appearance back to FollowWindows afterwards. RequestedThemeVariant
/// is application state, and leaving a window's worth of tests running under the dark palette
/// would quietly change what the rest of the suite reads back.
/// </summary>
public class ThemeTests
{
    private static ResourceDictionary Variant(string name) =>
        (ResourceDictionary)AvaloniaXamlLoader.Load(
            new Uri($"avares://TrispotQR.UI/Themes/Palette.{name}.axaml"));

    private static Color ColourOf(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value), key);
        return ((ISolidColorBrush)value!).Color;
    }

    /// <summary>The colour of the checkerboard's first square, which is all that has to differ.</summary>
    private static Color CheckerGroundOf(ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource("CheckerBrush", variant, out var value));
        var group = (DrawingGroup)((DrawingBrush)value!).Drawing!;
        var ground = (GeometryDrawing)group.Children[0];
        return ((ISolidColorBrush)ground.Brush!).Color;
    }

    [AvaloniaFact]
    public void EveryChromeKeyIsDefinedInBothVariants()
    {
        // A key defined in one variant only does not fail loudly: the lookup falls through to
        // the other palette and that one element stays light in a dark window. Asserting on
        // the whole key set is the only way to catch it, since nobody writes a test for the
        // one brush they forgot.
        var light = Variant("Light").Keys.Select(k => k.ToString()!).OrderBy(k => k).ToArray();
        var dark = Variant("Dark").Keys.Select(k => k.ToString()!).OrderBy(k => k).ToArray();

        Assert.NotEmpty(light);
        Assert.Equal(light, dark);

        // And the same keys are reachable through the running application, which is what any
        // DynamicResource in a window actually asks.
        foreach (var key in light)
        {
            Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Light, out _), key);
            Assert.True(Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _), key);
        }
    }

    [AvaloniaFact]
    public void ThePageColourFollowsAThemeChangeAlreadyOnScreen()
    {
        var border = new Border();
        border[!Border.BackgroundProperty] = new DynamicResourceExtension("PageBrush");
        var window = new Window { Width = 200, Height = 120, Content = border };

        try
        {
            ThemeSwitcher.Apply(AppTheme.Light);
            window.Show();
            DispatcherPump.Drain();

            var light = (border.Background as ISolidColorBrush)?.Color;
            Assert.NotNull(light);
            Assert.Equal(ColourOf("PageBrush", ThemeVariant.Light), light);

            // The switch has to reach a window that is already open, not only the next one to
            // be created, which is the whole reason these are DynamicResource references.
            ThemeSwitcher.Apply(AppTheme.Dark);
            DispatcherPump.Drain();

            var dark = (border.Background as ISolidColorBrush)?.Color;
            Assert.NotNull(dark);
            Assert.Equal(ColourOf("PageBrush", ThemeVariant.Dark), dark);
            Assert.NotEqual(light, dark);
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ApplyMapsEachChoiceToItsThemeVariant()
    {
        try
        {
            ThemeSwitcher.Apply(AppTheme.Dark);
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);

            ThemeSwitcher.Apply(AppTheme.Light);
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);

            // Following the operating system is Avalonia's own Default, which is why this
            // works on macOS and Linux as well and needs no registry read.
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
            Assert.Equal(ThemeVariant.Default, Application.Current!.RequestedThemeVariant);
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void RequestedReportsWhatWasLastAskedForIncludingFollowWindows()
    {
        // The settings window has to show the choice the user made, not the variant it
        // resolved to: FollowWindows and whichever of Light or Dark the machine happens to be
        // set to are the same picture on screen but different answers in a menu.
        try
        {
            ThemeSwitcher.Apply(AppTheme.Dark);
            Assert.Equal(AppTheme.Dark, ThemeSwitcher.Requested);

            ThemeSwitcher.Apply(AppTheme.Light);
            Assert.Equal(AppTheme.Light, ThemeSwitcher.Requested);

            ThemeSwitcher.Apply(AppTheme.FollowWindows);
            Assert.Equal(AppTheme.FollowWindows, ThemeSwitcher.Requested);
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void TheCheckerboardDiffersBetweenTheVariants()
    {
        // The checkerboard is app chrome, so it follows the theme, unlike the colours of a
        // generated code. A white checkerboard on a dark window would read as a lit panel
        // rather than as nothing at all.
        Assert.NotEqual(CheckerGroundOf(ThemeVariant.Light), CheckerGroundOf(ThemeVariant.Dark));
    }
}

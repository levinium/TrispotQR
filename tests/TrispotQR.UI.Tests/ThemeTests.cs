using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Controls;
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

    /// <summary>The colour of every square the checkerboard is drawn from, ground first.</summary>
    private static Color[] CheckerColoursOf(ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource("CheckerBrush", variant, out var value));
        return SquaresOf((DrawingBrush)value!);
    }

    /// <summary>The same, for a checkerboard already resolved onto something on screen.</summary>
    private static Color[] SquaresOf(DrawingBrush brush) =>
        ((DrawingGroup)brush.Drawing!).Children
            .Cast<GeometryDrawing>()
            .Select(d => ((ISolidColorBrush)d.Brush!).Color)
            .ToArray();

    /// <summary>The colour a palette entry stands for, or null for one that is not a colour.</summary>
    private static Color? ColourIn(object? value) => value switch
    {
        Color colour => colour,
        ISolidColorBrush brush => brush.Color,
        _ => null,
    };

    [AvaloniaFact]
    public void EveryChromeKeyIsDefinedInBothVariants()
    {
        // A key defined in one variant only does not fail loudly: the lookup falls through to
        // the other palette and that one element stays light in a dark window. Asserting on
        // the whole key set is the only way to catch it, since nobody writes a test for the
        // one brush they forgot.
        var lightFile = Variant("Light");
        var darkFile = Variant("Dark");
        var light = lightFile.Keys.Select(k => k.ToString()!).OrderBy(k => k).ToArray();
        var dark = darkFile.Keys.Select(k => k.ToString()!).OrderBy(k => k).ToArray();

        Assert.NotEmpty(light);
        Assert.Equal(light, dark);

        // And every key gives back its own variant's colour through the running application,
        // which is what a DynamicResource in a window actually asks. Checking only that the
        // lookup succeeds would prove far less than it looks: the Default entry answers a
        // variant the palette has forgotten to declare, so a palette that had lost its whole
        // Dark dictionary would still resolve every key, in light.
        foreach (var key in light)
        {
            AssertResolvesToItsOwnVariant(key, ThemeVariant.Light, lightFile);
            AssertResolvesToItsOwnVariant(key, ThemeVariant.Dark, darkFile);
        }
    }

    private static void AssertResolvesToItsOwnVariant(string key, ThemeVariant variant, ResourceDictionary file)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var resolved), key);

        // CheckerBrush is the one entry that is not a flat colour; its own test covers it.
        var expected = ColourIn(file[key]);
        if (expected is not null)
        {
            Assert.Equal(expected, ColourIn(resolved));
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
    public void TheMainWindowIsPaintedFromThePaletteRatherThanFromLiteralColours()
    {
        // The window that the whole appearance setting exists for. It carried three colours that
        // no palette knows about: a page with no background at all, so FluentTheme painted the
        // chrome dark while the panel behind the preview stayed a light grey slab, a hard coded
        // #F4F4F4 on that panel, and a hard coded red on the message that says why an export is
        // blocked, which is the one piece of text on the page that has to stay readable.
        try
        {
            UiHarness.WithWindow(session =>
            {
                var window = session.Window;
                var previewArea = window.GetVisualDescendants().OfType<Grid>()
                    .Single(g => g.Name == "PreviewArea");

                // The checkerboard, found by what it is rather than by a name: the one brush on
                // the page that is not a flat colour.
                var checkers = previewArea.GetVisualDescendants().OfType<Border>()
                    .Where(b => b.Background is DrawingBrush)
                    .ToList();

                var checker = Assert.Single(
                    checkers,
                    b => b.GetVisualDescendants().OfType<QrPreview>().Any());

                var blocked = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Name == "ExportBlockedText");

                foreach (var (theme, variant) in new[]
                {
                    (AppTheme.Light, ThemeVariant.Light),
                    (AppTheme.Dark, ThemeVariant.Dark),
                })
                {
                    ThemeSwitcher.Apply(theme);
                    DispatcherPump.Drain();

                    Assert.Equal(ColourOf("PageBrush", variant), (window.Background as ISolidColorBrush)?.Color);
                    Assert.Equal(ColourOf("SurfaceBrush", variant), ((checker.Parent as Border)?.Background as ISolidColorBrush)?.Color);
                    Assert.Equal(ColourOf("DangerBrush", variant), (blocked.Foreground as ISolidColorBrush)?.Color);

                    // Every square of the checkerboard, not only its ground: a dark ground under
                    // pale squares is no less wrong than a pale one on a dark window.
                    Assert.Equal(CheckerColoursOf(variant), SquaresOf((DrawingBrush)checker.Background!));
                }
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void TheRememberedAppearanceIsInEffectBeforeTheFirstWindowIsBuilt()
    {
        // Choosing Dark and restarting has to give a dark app. Nothing else in the product reads
        // Theme back out of settings.json: the settings window writes it and applies it live, so
        // without this step the choice lasted only as long as the session that made it, and the
        // settings window's own "put the theme back on cancel" restored the saved choice rather
        // than undoing a preview, turning Cancel into an apply.
        //
        // Handed its own settings folder rather than the real one. Reading whichever appearance
        // the developer last chose would assert nothing on the machine where it already matches.
        var directory = Path.Combine(Path.GetTempPath(), $"trispotqr-theme-{Guid.NewGuid():N}");

        try
        {
            // Both, so a step that ignored the file and applied one appearance outright would
            // still be caught by the other half.
            foreach (var theme in new[] { AppTheme.Dark, AppTheme.Light })
            {
                new AppSettingsStore(directory).Save(AppSettings.Default with { Theme = theme });

                ThemeSwitcher.Apply(AppTheme.FollowWindows);
                App.ApplyStartupTheme(new AppSettingsStore(directory));

                Assert.Equal(theme, ThemeSwitcher.Requested);
                Assert.Equal(
                    theme == AppTheme.Dark ? ThemeVariant.Dark : ThemeVariant.Light,
                    Application.Current!.RequestedThemeVariant);
            }
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [AvaloniaFact]
    public void TheCheckerboardDiffersBetweenTheVariants()
    {
        // The checkerboard is app chrome, so it follows the theme, unlike the colours of a
        // generated code. A white checkerboard on a dark window would read as a lit panel
        // rather than as nothing at all. Every square it is drawn from has to move, not only
        // the ground: a dark ground behind pale squares is no less wrong than a pale one.
        var light = CheckerColoursOf(ThemeVariant.Light);
        var dark = CheckerColoursOf(ThemeVariant.Dark);

        Assert.Equal(light.Length, dark.Length);
        Assert.NotEmpty(light);
        foreach (var (l, d) in light.Zip(dark))
        {
            Assert.NotEqual(l, d);
        }
    }
}

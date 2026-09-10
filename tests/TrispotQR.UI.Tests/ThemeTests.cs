using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Validation;
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

    /// <summary>The same colour, in the framework-free type Core measures contrast in.</summary>
    private static RgbColor Rgb(Color colour) => RgbColor.FromArgb(colour.A, colour.R, colour.G, colour.B);

    /// <summary>
    /// What a translucent palette colour actually paints once it lands on what is beneath it.
    ///
    /// Measuring the declared colour instead would flatter a translucent one: a 10% white pill
    /// scores as white on paper and as very nearly the page on screen.
    /// </summary>
    private static RgbColor Over(Color colour, Color under)
    {
        var alpha = colour.A / 255.0;
        byte Blend(byte top, byte bottom) => (byte)Math.Round((alpha * top) + ((1 - alpha) * bottom));

        return RgbColor.FromRgb(
            Blend(colour.R, under.R), Blend(colour.G, under.G), Blend(colour.B, under.B));
    }

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
    public void TheColourPickerPopupIsPaintedFromThePaletteRatherThanFromLiteralColours()
    {
        // The last white card in a dark window. The popup carried a literal white ground, a
        // literal pale grey edge and a literal grey caption, and the wave that themed
        // MainWindow and the Views missed all three because they live in a control rather than
        // in a window, so no finding named the file.
        //
        // The card is reached through the Popup's own Child, not by walking down from the
        // picker: an open popup hosts its content in the window's overlay layer, so it is not a
        // visual descendant of the control that owns it. ColorPickerTests established that.
        //
        // What is deliberately not asserted here is the rest of the popup. The saturation
        // square, the hue strip and the translucent hairlines around them are colour space and
        // overlays, not chrome, and they are supposed to stay exactly where they are.
        var picker = new ColorPicker { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        var window = new Window { Width = 400, Height = 520, Content = picker };

        try
        {
            window.Show();
            DispatcherPump.Drain();

            var swatch = picker.FindControl<ToggleButton>("SwatchButton")
                ?? throw new InvalidOperationException("ColorPicker has no SwatchButton.");
            UiHarness.Click(window, UiHarness.At(swatch, 0.5, 0.5));

            var square = picker.FindControl<Control>("SvSquare")
                ?? throw new InvalidOperationException("ColorPicker has no SvSquare.");
            Assert.True(
                DispatcherPump.DrainUntil(() => square.Bounds.Width > 0 && square.Bounds.Height > 0),
                "clicking the swatch did not open a laid-out popup");

            var popup = picker.FindControl<Popup>("PickerPopup")
                ?? throw new InvalidOperationException("ColorPicker has no PickerPopup.");
            var card = Assert.IsType<Border>(popup.Child);

            // One caption, found by the words it shows, standing for the shared style: the
            // Foreground setter lives in this control's own Styles block, so a DynamicResource
            // there has to keep following the theme like any other.
            var caption = Assert.Single(
                card.GetSelfAndVisualDescendants().OfType<TextBlock>(),
                t => t.Text == "Pick a color");

            foreach (var (theme, variant) in new[]
            {
                (AppTheme.Light, ThemeVariant.Light),
                (AppTheme.Dark, ThemeVariant.Dark),
            })
            {
                ThemeSwitcher.Apply(theme);
                DispatcherPump.Drain();

                Assert.Equal(ColourOf("SurfaceBrush", variant), (card.Background as ISolidColorBrush)?.Color);
                Assert.Equal(ColourOf("BorderBrush", variant), (card.BorderBrush as ISolidColorBrush)?.Color);
                Assert.Equal(ColourOf("MutedTextBrush", variant), (caption.Foreground as ISolidColorBrush)?.Color);
            }
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
            window.Close();
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
    public void TheToastIsPaintedFromThePaletteRatherThanFromLiteralColours()
    {
        // The confirmation pill carried four literal colours: a near black ground, white text, a
        // pale green tick and no edge at all. In a dark window that is a near black shape on a
        // near black page, so the one message whose entire job is to be noticed was the hardest
        // thing on screen to see.
        try
        {
            UiHarness.WithWindow(session =>
            {
                var window = session.Window;
                var toast = window.GetVisualDescendants().OfType<Border>()
                    .Single(b => b.Name == "Toast");
                var text = window.GetVisualDescendants().OfType<TextBlock>()
                    .Single(t => t.Name == "ToastText");

                // The tick, found by the glyph it draws: it needs no name of its own, and giving
                // it one to be found by would be the test shaping the markup.
                var tick = Assert.Single(
                    toast.GetVisualDescendants().OfType<TextBlock>(),
                    t => t.Text == "✓");

                foreach (var (theme, variant) in new[]
                {
                    (AppTheme.Light, ThemeVariant.Light),
                    (AppTheme.Dark, ThemeVariant.Dark),
                })
                {
                    ThemeSwitcher.Apply(theme);
                    DispatcherPump.Drain();

                    Assert.Equal(ColourOf("ToastBackgroundBrush", variant), (toast.Background as ISolidColorBrush)?.Color);
                    Assert.Equal(ColourOf("ToastBorderBrush", variant), (toast.BorderBrush as ISolidColorBrush)?.Color);
                    Assert.Equal(ColourOf("ToastTextBrush", variant), (text.Foreground as ISolidColorBrush)?.Color);
                    Assert.Equal(ColourOf("ToastCheckBrush", variant), (tick.Foreground as ISolidColorBrush)?.Color);
                }
            });
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    [AvaloniaFact]
    public void TheToastStandsOutFromThePageInBothAppearances()
    {
        // The requirement behind the test above, rather than its wiring. Four palette keys that
        // all resolve correctly still leave the toast invisible if the dark palette's pill is
        // the same tone as the dark palette's page, and nothing in the key-parity test would
        // notice: it asks that both variants define a key, never that either value is usable.
        //
        // Measured with Core's own WCAG maths, the same ScannabilityChecker.ContrastRatio the
        // app applies to a generated code. The app refuses to call a code scannable at contrast
        // it would fail here.
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var page = ColourOf("PageBrush", variant);
            var pill = Over(ColourOf("ToastBackgroundBrush", variant), page);

            // 4.5 rather than the 3.0 that WCAG asks of a plain interface surface. A toast is
            // not a panel that merely has to be distinguishable from its neighbour; it appears
            // unbidden for two seconds and has to be caught by someone looking somewhere else.
            AssertContrast(4.5, pill, Rgb(page), $"{variant} toast against the page");
            AssertContrast(4.5, Rgb(ColourOf("ToastTextBrush", variant)), pill, $"{variant} toast text");

            // The tick is a bold glyph, not prose, so it takes the large-text floor.
            AssertContrast(3.0, Rgb(ColourOf("ToastCheckBrush", variant)), pill, $"{variant} toast tick");
        }
    }

    private static void AssertContrast(double floor, RgbColor a, RgbColor b, string what)
    {
        var ratio = ScannabilityChecker.ContrastRatio(a, b);
        Assert.True(ratio >= floor, $"{what}: contrast {ratio:0.00} is below {floor:0.0}");
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

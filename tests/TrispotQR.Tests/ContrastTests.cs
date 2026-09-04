using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrispotQR.App.Services;
using TrispotQR.App.Views;
using TrispotQR.Core.Presets;

namespace TrispotQR.Tests;

/// <summary>
/// Every piece of text in every window must be readable against whatever is behind it, in
/// both themes.
///
/// This exists because of a real bug. CheckBox had a style setting its foreground and
/// RadioButton did not, so the radio buttons in the settings window fell back to the system
/// black and became invisible in dark mode. Nothing failed; the window simply had unreadable
/// text in it, and it was found by eye. Walking the tree and measuring the contrast catches
/// that whole class of mistake, including in windows nobody thought to look at.
/// </summary>
[Collection("UI")]
public class ContrastTests
{
    /// <summary>
    /// WCAG's threshold for large text. Deliberately not the stricter 4.5, because muted
    /// captions are a legitimate design choice and this test is hunting for text that is
    /// effectively invisible, not for every imperfect pairing.
    /// </summary>
    private const double Minimum = 3.0;

    private readonly WpfHost _host;

    public ContrastTests(WpfHost host) => _host = host;

    public static TheoryData<AppTheme> Themes => new() { AppTheme.Light, AppTheme.Dark };

    [Theory]
    [MemberData(nameof(Themes))]
    public void SettingsWindow_TextIsReadable(AppTheme theme) =>
        AssertReadable(theme, () => new SettingsWindow(AppSettings.Default));

    [Theory]
    [MemberData(nameof(Themes))]
    public void AboutWindow_TextIsReadable(AppTheme theme) =>
        AssertReadable(theme, () => new AboutWindow());

    [Theory]
    [MemberData(nameof(Themes))]
    public void ConfirmWindow_TextIsReadable(AppTheme theme) =>
        AssertReadable(theme, () => new ConfirmWindow(
            "This code did not scan", "There is not enough contrast.", "Save anyway", false, true));

    /// <summary>
    /// The list a dropdown shows when it is open lives in a popup, on its own visual tree,
    /// so walking the window finds only the closed selection box. That is the half nobody
    /// looks at, and dark mode has already produced blank dropdowns once, so it gets opened
    /// and measured on its own.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void ThemePicker_IsReadableWhenOpen(AppTheme theme)
    {
        var offenders = _host.Run(() =>
        {
            ThemeManager.Apply(theme);
            var window = new SettingsWindow(AppSettings.Default);

            try
            {
                // Shown, because a popup only builds its content once there is a real window
                // behind it. Parked off screen so a test run does not flash windows about.
                window.Left = -20000;
                window.ShowInTaskbar = false;
                window.Show();

                var combo = (ComboBox)window.FindName("ThemeChoice");
                combo.IsDropDownOpen = true;
                combo.UpdateLayout();

                var found = new List<string>();
                var measured = new List<string>();
                var surface = (window.Background as SolidColorBrush)?.Color ?? Colors.White;

                for (var i = 0; i < combo.Items.Count; i++)
                {
                    var item = (ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(i);
                    Assert.NotNull(item);

                    item.UpdateLayout();
                    found.AddRange(Illegible(item, surface));
                    measured.AddRange(Labels(item));
                }

                combo.IsDropDownOpen = false;

                // Without this the test passes just as happily when the popup built no text
                // at all, which is the likelier way for it to stop testing anything.
                Assert.Equal(new[] { "Follow Windows", "Light", "Dark" }, measured);
                return found;
            }
            finally
            {
                window.Close();
                ThemeManager.Apply(AppTheme.Light);
            }
        });

        Assert.True(offenders.Count == 0,
            $"unreadable dropdown items in {theme} mode:\n" + string.Join("\n", offenders));
    }

    private void AssertReadable(AppTheme theme, Func<Window> create)
    {
        var offenders = _host.Run(() =>
        {
            ThemeManager.Apply(theme);

            try
            {
                var window = create();
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.Resources = window.Resources;

                content.Measure(new Size(600, 1200));
                content.Arrange(new Rect(0, 0, 600, content.DesiredSize.Height));
                content.UpdateLayout();

                var background = (window.Background as SolidColorBrush)?.Color ?? Colors.White;
                return Illegible(content, background).ToList();
            }
            finally
            {
                ThemeManager.Apply(AppTheme.Light);
            }
        });

        Assert.True(offenders.Count == 0,
            $"unreadable text in {theme} mode:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Finds text whose colour is too close to the colour actually painted behind it. The
    /// background is resolved by walking up the tree to the nearest ancestor that paints
    /// one, which is what makes white-on-accent pass while black-on-dark fails.
    /// </summary>
    private static IEnumerable<string> Illegible(DependencyObject root, Color inherited)
    {
        var background = BackgroundOf(root) ?? inherited;

        if (root is TextBlock { Text.Length: > 0 } text
            && text.Foreground is SolidColorBrush { Color.A: > 0 } fg)
        {
            var ratio = Contrast(fg.Color, background);

            if (ratio < Minimum)
            {
                var sample = text.Text.Length > 40 ? text.Text[..40] + "..." : text.Text;
                yield return
                    $"  \"{sample}\" at {ratio:0.0}:1 " +
                    $"(#{fg.Color.R:X2}{fg.Color.G:X2}{fg.Color.B:X2} on " +
                    $"#{background.R:X2}{background.G:X2}{background.B:X2})";
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            foreach (var found in Illegible(VisualTreeHelper.GetChild(root, i), background))
            {
                yield return found;
            }
        }
    }

    /// <summary>The text a subtree actually renders, in visual order.</summary>
    private static IEnumerable<string> Labels(DependencyObject root)
    {
        if (root is TextBlock { Text.Length: > 0 } text)
        {
            yield return text.Text;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            foreach (var found in Labels(VisualTreeHelper.GetChild(root, i)))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// Only Border and Panel count. A Control's Background is not necessarily painted
    /// behind its content: on a CheckBox it fills the little box and nothing else, so
    /// treating it as the backdrop for the label reported perfectly readable text as
    /// invisible. Our own button templates paint through a Border, which this still finds.
    /// </summary>
    private static Color? BackgroundOf(DependencyObject element) => element switch
    {
        Border { Background: SolidColorBrush { Color.A: > 200 } b } => b.Color,
        Panel { Background: SolidColorBrush { Color.A: > 200 } p } => p.Color,
        _ => null,
    };

    private static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color c) =>
        (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Channel(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}

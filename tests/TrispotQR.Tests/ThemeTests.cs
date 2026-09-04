using System.Windows;
using System.Windows.Media;
using TrispotQR.App.Services;
using TrispotQR.Core.Presets;

namespace TrispotQR.Tests;

/// <summary>
/// Theming works by swapping one merged dictionary, so the two palettes have to agree on
/// every key. A key present in light and missing from dark would not fail to build; the
/// affected element would simply lose its colour at runtime, in dark mode only.
/// </summary>
[Collection("UI")]
public class ThemeTests
{
    private readonly WpfHost _host;

    public ThemeTests(WpfHost host) => _host = host;

    private static readonly Uri Light = new("pack://application:,,,/TrispotQR;component/Theme.Light.xaml");
    private static readonly Uri Dark = new("pack://application:,,,/TrispotQR;component/Theme.Dark.xaml");

    [Fact]
    public void BothPalettesDefineExactlyTheSameKeys()
    {
        var (missingFromDark, missingFromLight) = _host.Run(() =>
        {
            var light = new ResourceDictionary { Source = Light };
            var dark = new ResourceDictionary { Source = Dark };

            var lightKeys = light.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet();
            var darkKeys = dark.Keys.Cast<object>().Select(k => k.ToString()!).ToHashSet();

            return (lightKeys.Except(darkKeys).Order().ToList(), darkKeys.Except(lightKeys).Order().ToList());
        });

        Assert.True(missingFromDark.Count == 0, "missing from Theme.Dark.xaml: " + string.Join(", ", missingFromDark));
        Assert.True(missingFromLight.Count == 0, "missing from Theme.Light.xaml: " + string.Join(", ", missingFromLight));
    }

    [Fact]
    public void ThePalettesActuallyDifferInColour()
    {
        // Guards against the two files drifting into being copies of each other.
        var (lightPage, darkPage) = _host.Run(() =>
        {
            var light = new ResourceDictionary { Source = Light };
            var dark = new ResourceDictionary { Source = Dark };

            return (((SolidColorBrush)light["PageBrush"]).Color, ((SolidColorBrush)dark["PageBrush"]).Color);
        });

        Assert.NotEqual(lightPage, darkPage);
    }

    [Fact]
    public void DarkModeIsActuallyDarkerThanLightMode()
    {
        var (light, dark) = _host.Run(() =>
        {
            var l = new ResourceDictionary { Source = Light };
            var d = new ResourceDictionary { Source = Dark };
            return (Luminance(((SolidColorBrush)l["PageBrush"]).Color),
                    Luminance(((SolidColorBrush)d["PageBrush"]).Color));
        });

        Assert.True(dark < light, $"dark page luminance {dark:0.00} should be below light {light:0.00}");
    }

    [Theory]
    [InlineData(AppTheme.Light, false)]
    [InlineData(AppTheme.Dark, true)]
    public void ApplyingAnExplicitTheme_SwapsThePalette(AppTheme theme, bool expectDark)
    {
        var isDark = _host.Run(() =>
        {
            ThemeManager.Apply(theme);
            return ThemeManager.IsDark;
        });

        Assert.Equal(expectDark, isDark);

        // Leave the host on the light palette so later UI tests render predictably.
        _host.Run(() => { ThemeManager.Apply(AppTheme.Light); return true; });
    }

    [Fact]
    public void SwitchingTheme_ChangesWhatTheApplicationResolves()
    {
        // The point of the whole exercise: a live lookup returns the new colour, which is
        // what DynamicResource references throughout the UI will pick up.
        var (light, dark) = _host.Run(() =>
        {
            ThemeManager.Apply(AppTheme.Light);
            var l = ((SolidColorBrush)Application.Current.Resources["PageBrush"]).Color;

            ThemeManager.Apply(AppTheme.Dark);
            var d = ((SolidColorBrush)Application.Current.Resources["PageBrush"]).Color;

            ThemeManager.Apply(AppTheme.Light);
            return (l, d);
        });

        Assert.NotEqual(light, dark);
    }

    [Fact]
    public void FollowWindows_ResolvesToWhicheverWindowsIsSet()
    {
        var (resolved, windows) = _host.Run(() =>
        {
            ThemeManager.Apply(AppTheme.FollowWindows);
            var r = (ThemeManager.IsDark, ThemeManager.WindowsPrefersDark());
            ThemeManager.Apply(AppTheme.Light);
            return r;
        });

        Assert.Equal(windows, resolved);
    }

    private static double Luminance(Color c) => (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B);
}

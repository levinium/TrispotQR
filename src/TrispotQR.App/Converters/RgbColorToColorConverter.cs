using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Primitives;

namespace TrispotQR.App.Converters;

/// <summary>
/// Bridges Core's colour type to WPF's for XAML binding.
///
/// The colour picker is a WPF control and speaks WPF colours; the view model speaks Core's.
/// This is the one place they meet, so an Avalonia app replaces this file and nothing else.
/// </summary>
public sealed class RgbColorToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is RgbColor colour ? WpfGeometryAdapter.ToColor(colour) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Color colour ? WpfGeometryAdapter.ToRgbColor(colour) : null;
}

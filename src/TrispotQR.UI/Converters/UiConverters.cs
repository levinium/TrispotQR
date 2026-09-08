using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TrispotQR.Core.Validation;

namespace TrispotQR.UI.Converters;

/// <summary>
/// Colours the scannability badge. Green means it read back cleanly, amber means it read back
/// but carries something cameras often trip on, red means it did not read at all.
/// </summary>
public sealed class VerdictToBrushConverter : IValueConverter
{
    // Immutable and shared, matching AvaloniaGeometry. A SolidColorBrush is an AvaloniaObject
    // with change notification and a property store behind it; handing a fresh one to every
    // binding pass buys nothing here, because there are only ever three colours and none of
    // them changes. ImmutableSolidColorBrush is the plain value type Avalonia provides for
    // exactly this.
    private static readonly IBrush Good = new ImmutableSolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x37));
    private static readonly IBrush Risky = new ImmutableSolidColorBrush(Color.FromRgb(0xB5, 0x6E, 0x00));
    private static readonly IBrush Bad = new ImmutableSolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ScanVerdict.Good => Good,
            ScanVerdict.Risky => Risky,
            _ => Bad,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A verdict cannot be recovered from a colour.");
}

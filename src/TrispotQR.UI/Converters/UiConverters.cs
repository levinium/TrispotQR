using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using TrispotQR.Core.Validation;

namespace TrispotQR.UI.Converters;

/// <summary>
/// Colours the scannability badge. Green means it read back cleanly, amber means it read back
/// but carries something cameras often trip on, red means it did not read at all.
/// </summary>
public sealed class VerdictToBrushConverter : IValueConverter
{
    public static readonly VerdictToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            ScanVerdict.Good => new SolidColorBrush(Color.FromRgb(0x1B, 0x7F, 0x37)),
            ScanVerdict.Risky => new SolidColorBrush(Color.FromRgb(0xB5, 0x6E, 0x00)),
            _ => new SolidColorBrush(Color.FromRgb(0xB3, 0x26, 0x1E)),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A verdict cannot be recovered from a colour.");
}

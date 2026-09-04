using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TrispotQR.App.Validation;
using TrispotQR.Core.Styling;

namespace TrispotQR.App.Converters;

/// <summary>Shows an element only when the bound boolean is true.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

/// <summary>Shows an element only when the bound string has something in it.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Wraps a colour in a frozen brush, for swatches.</summary>
public sealed class ColorToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Color colour)
        {
            return Brushes.Transparent;
        }

        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SolidColorBrush brush ? brush.Color : Colors.Black;
}

/// <summary>Green, amber or red for the scannability badge.</summary>
public sealed class VerdictToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Good = Frozen(0x1E, 0x8E, 0x3E);
    private static readonly SolidColorBrush Risky = Frozen(0xE3, 0x8C, 0x00);
    private static readonly SolidColorBrush Bad = Frozen(0xC5, 0x22, 0x1F);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ScanVerdict.Good => Good,
        ScanVerdict.Risky => Risky,
        _ => Bad,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

/// <summary>
/// True when the bound value equals the parameter. Used to drive the radio-style toggle
/// buttons for background choice and export size.
/// </summary>
public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null)
        {
            return parameter is null;
        }

        // The parameter arrives from XAML as a string, so compare on the text form.
        return string.Equals(value.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        return target.IsEnum
            ? Enum.Parse(target, parameter.ToString()!, ignoreCase: true)
            : System.Convert.ChangeType(parameter, target, culture);
    }
}

/// <summary>
/// Turns the enum values used in the dropdowns into wording a non-technical person will
/// recognise. Doing it here rather than renaming the enum members keeps the saved preset
/// files readable and the code honest about what each shape is.
/// </summary>
public sealed class FriendlyNameConverter : IValueConverter
{
    private static readonly Dictionary<object, string> Names = new()
    {
        [ModuleShape.Square] = "Squares",
        [ModuleShape.RoundedSquare] = "Rounded squares",
        [ModuleShape.Circle] = "Dots",
        [ModuleShape.Diamond] = "Diamonds",
        [ModuleShape.Fluid] = "Flowing",

        [MarkerFrameShape.Square] = "Square",
        [MarkerFrameShape.RoundedSquare] = "Rounded",
        [MarkerFrameShape.Circle] = "Circle",
        [MarkerFrameShape.Leaf] = "Leaf",

        [MarkerCenterShape.Square] = "Square",
        [MarkerCenterShape.RoundedSquare] = "Rounded",
        [MarkerCenterShape.Circle] = "Circle",

        [OutlineTarget.Modules] = "Dots only",
        [OutlineTarget.Markers] = "Corners only",
        [OutlineTarget.Both] = "Everything",

        [LogoPunchShape.Square] = "Square gap",
        [LogoPunchShape.RoundedSquare] = "Rounded gap",
        [LogoPunchShape.Circle] = "Round gap",
        [LogoPunchShape.None] = "No gap",

        [Core.Qr.EccLevel.Low] = "Low (smallest code)",
        [Core.Qr.EccLevel.Medium] = "Medium (recommended)",
        [Core.Qr.EccLevel.Quartile] = "High",
        [Core.Qr.EccLevel.High] = "Highest (needed for logos)",

        [Core.Payloads.WifiSecurity.Wpa] = "WPA / WPA2",
        [Core.Payloads.WifiSecurity.Wep] = "WEP (old)",
        [Core.Payloads.WifiSecurity.None] = "No password",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is not null && Names.TryGetValue(value, out var name) ? name : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

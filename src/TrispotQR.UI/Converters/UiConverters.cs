using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using TrispotQR.Core.Payloads;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Styling;
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

/// <summary>
/// Turns the enum values used in the dropdowns into wording a non-technical person will
/// recognise, so the UI can say "Dots" while the code and the saved preset files keep saying
/// Circle. Ported from the WPF app's FriendlyNameConverter, wording included: "WEP (old)" and
/// "Medium (recommended)" are advice to the user, not decoration, and the two builds must not
/// disagree about what a setting is called.
///
/// Every enum the dropdowns use is here, not just the Wi-Fi one the Avalonia window needs
/// today: Phase 2d brings the styling panel with six more of them, and a converter that had to
/// be extended for each would be a converter that could be forgotten for one.
///
/// A dictionary keyed on the boxed enum values rather than a switch over each type, matching
/// the WPF original: one table reads as the list of wordings it is, and an unlisted value
/// falls through to ToString() rather than to an exception, so a member added to any of these
/// enums renders its own name until someone chooses better words for it.
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

        [EccLevel.Low] = "Low (smallest code)",
        [EccLevel.Medium] = "Medium (recommended)",
        [EccLevel.Quartile] = "High",
        [EccLevel.High] = "Highest (needed for logos)",

        [WifiSecurity.Wpa] = "WPA / WPA2",
        [WifiSecurity.Wep] = "WEP (old)",
        [WifiSecurity.None] = "No password",
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && Names.TryGetValue(value, out var name) ? name : value?.ToString() ?? string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("The enum value cannot be recovered from its friendly name.");
}

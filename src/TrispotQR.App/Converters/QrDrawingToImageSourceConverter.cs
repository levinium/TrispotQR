using System.Globalization;
using System.Windows.Data;
using TrispotQR.App.Rendering;
using TrispotQR.Core.Rendering;

namespace TrispotQR.App.Converters;

/// <summary>
/// Renders a <see cref="QrDrawing"/> for WPF to display.
///
/// The view model deliberately hands out the drawing rather than an image, so this is where
/// the toolkit-specific step happens. Phase 2b adds an Avalonia equivalent beside it and
/// nothing in the view model changes.
/// </summary>
public sealed class QrDrawingToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is QrDrawing drawing ? WpfQrRenderer.RenderToDrawingImage(drawing) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("A drawing cannot be recovered from a rendered image.");
}

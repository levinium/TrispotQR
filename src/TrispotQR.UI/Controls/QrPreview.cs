using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using TrispotQR.Core.Rendering;
using TrispotQR.UI.Rendering;

namespace TrispotQR.UI.Controls;

/// <summary>
/// Draws a <see cref="QrDrawing"/> as vector geometry, scaled to fill the control.
///
/// Vector rather than a rendered bitmap, so the preview stays crisp as the window resizes and
/// so resizing costs nothing in Core. What gets saved does not come through here: export goes
/// through Core's Skia rasteriser.
///
/// The logo is not drawn here yet. It lives on disk as a path in QrDrawing.Logo, and loading
/// it belongs with the logo picker in Phase 2c. Exports already include it.
/// </summary>
public class QrPreview : Control
{
    public static readonly StyledProperty<QrDrawing?> DrawingProperty =
        AvaloniaProperty.Register<QrPreview, QrDrawing?>(nameof(Drawing));

    static QrPreview() => AffectsRender<QrPreview>(DrawingProperty);

    public QrDrawing? Drawing
    {
        get => GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        if (Drawing is not { } drawing || drawing.SizeInUnits <= 0)
        {
            return;
        }

        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        // Square and centred: a QR code is square, and letting it stretch to a non-square
        // control would break the module grid a scanner relies on.
        var scale = side / drawing.SizeInUnits;
        using var _ = context.PushTransform(
            Matrix.CreateScale(scale, scale)
            * Matrix.CreateTranslation((Bounds.Width - side) / 2, (Bounds.Height - side) / 2));

        if (drawing.Background is { A: > 0 } background)
        {
            context.FillRectangle(
                AvaloniaGeometry.ToBrush(background),
                new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits));
        }

        foreach (var layer in drawing.Layers)
        {
            if (layer.Path.IsEmpty)
            {
                continue;
            }

            context.DrawGeometry(
                AvaloniaGeometry.ToBrush(layer.Fill),
                AvaloniaGeometry.ToPen(layer.Stroke),
                AvaloniaGeometry.ToStreamGeometry(layer.Path));
        }
    }
}

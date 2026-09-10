using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
/// The one thing that is not vector is the logo, which is a bitmap on disk named by a path in
/// QrDrawing.Logo. It is drawn last, over the code, in the same box Core's exporters composite
/// it into, so what is on screen while it is being sized is what will be saved.
/// </summary>
public class QrPreview : Control
{
    public static readonly StyledProperty<QrDrawing?> DrawingProperty =
        AvaloniaProperty.Register<QrPreview, QrDrawing?>(nameof(Drawing));

    /// <summary>
    /// The current drawing translated into Avalonia's types, rebuilt only when the drawing
    /// itself changes.
    ///
    /// Render runs on every frame of a window resize, and none of this depends on the size:
    /// the scale is applied as a transform, so the same geometry serves every size. Building
    /// a StreamGeometry and two brushes per layer per frame meant allocating and re-walking
    /// every path in the code hundreds of times for a drag that changes nothing about it.
    /// </summary>
    private IReadOnlyList<PreparedLayer> _layers = [];
    private IBrush? _background;

    /// <summary>
    /// The decoded logo, and the path it came from. Kept for the same reason the layers are:
    /// Prepare runs on every style change, and decoding a large PNG on each tick of the logo
    /// size slider would be a full image decode per frame of a drag.
    ///
    /// Keyed on the path alone, so replacing the file on disk under an unchanged name leaves a
    /// stale picture in the preview until the logo is chosen again. That is the cheap side of
    /// the trade: the alternative is decoding the file on every render to notice a change
    /// almost nobody makes mid-session, and an export always reads the file afresh regardless.
    /// </summary>
    private Bitmap? _logo;
    private string? _logoPath;

    static QrPreview() => AffectsRender<QrPreview>(DrawingProperty);

    public QrDrawing? Drawing
    {
        get => GetValue(DrawingProperty);
        set => SetValue(DrawingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == DrawingProperty)
        {
            Prepare(change.GetNewValue<QrDrawing?>());
        }
    }

    private void Prepare(QrDrawing? drawing)
    {
        PrepareLogo(drawing?.Logo?.Path);

        if (drawing is null || drawing.SizeInUnits <= 0)
        {
            _background = null;
            _layers = [];
            return;
        }

        _background = drawing.Background is { A: > 0 } background
            ? AvaloniaGeometry.ToBrush(background)
            : null;

        _layers =
        [
            .. drawing.Layers
                .Where(layer => !layer.Path.IsEmpty)
                .Select(layer => new PreparedLayer(
                    AvaloniaGeometry.ToBrush(layer.Fill),
                    AvaloniaGeometry.ToPen(layer.Stroke),
                    AvaloniaGeometry.ToStreamGeometry(layer.Path))),
        ];
    }

    /// <summary>
    /// Decodes the logo, or forgets the one that was there. A file that will not open leaves
    /// this null and the code is drawn without it, which is what Core's exporters do with the
    /// same file: a logo that was moved, renamed or deleted since it was chosen must not take
    /// the whole preview down with it, and it would do so on every frame.
    /// </summary>
    private void PrepareLogo(string? path)
    {
        if (path == _logoPath)
        {
            return;
        }

        _logo?.Dispose();
        _logo = null;
        _logoPath = path;

        if (path is null)
        {
            return;
        }

        try
        {
            _logo = new Bitmap(path);
        }
        catch (Exception)
        {
            // Left null: the code draws, the logo does not.
        }
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

        if (_background is { } background)
        {
            context.FillRectangle(background, new Rect(0, 0, drawing.SizeInUnits, drawing.SizeInUnits));
        }

        foreach (var layer in _layers)
        {
            context.DrawGeometry(layer.Fill, layer.Stroke, layer.Geometry);
        }

        // Last, and over the top. The modules underneath it have already been punched out by
        // Core, so this fills a hole rather than hiding anything a scanner needs.
        if (_logo is { } logo && drawing.Logo is { } placement)
        {
            context.DrawImage(
                logo,
                new Rect(placement.X, placement.Y, placement.Width, placement.Height));
        }
    }

    private sealed record PreparedLayer(IBrush Fill, IPen? Stroke, StreamGeometry Geometry);
}

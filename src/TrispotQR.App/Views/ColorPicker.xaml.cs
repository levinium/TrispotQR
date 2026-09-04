using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TrispotQR.Core.Styling;

namespace TrispotQR.App.Views;

/// <summary>
/// A swatch button that opens a small colour picker.
///
/// Built rather than borrowed because WPF ships no colour picker and the Windows common
/// dialog has no hex entry, which is the one thing someone matching a brand colour
/// actually needs. A curated palette covers the casual case in one click.
///
/// Four ways in, all equal: the saturation/value square with its hue strip, the palette,
/// the hex box and the RGB sliders. They stay in step through a single hub. Every one of
/// them writes <see cref="SelectedColor"/> and nothing else, and <see cref="Sync"/> pushes
/// that value back out to all of them. The <c>_updating</c> flag stops the write-back from
/// looping round again.
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor),
        typeof(Color),
        typeof(ColorPicker),
        new FrameworkPropertyMetadata(
            Colors.Black,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnSelectedColorChanged));

    private bool _updating;

    /// <summary>
    /// Hue, saturation and value are held here rather than derived from
    /// <see cref="SelectedColor"/> every time, because that conversion is lossy at two
    /// edges: every grey has an undefined hue, and black has an undefined hue and
    /// saturation. Deriving them would make the crosshair jump to a corner and the hue
    /// strip snap to red the moment someone dragged the brightness to zero, and dragging
    /// back up would return red instead of the colour they started from.
    /// </summary>
    private double _hue;
    private double _saturation;
    private double _value;

    /// <summary>
    /// Set while the square or the hue strip is driving the change. Their own values are
    /// already exact, so re-deriving them from the rounded 8 bit colour would let the
    /// marker drift under the cursor during a drag.
    /// </summary>
    private bool _fromHsv;

    public ColorPicker()
    {
        InitializeComponent();

        PaletteItems.ItemsSource = Palette;

        RedSlider.ValueChanged += OnSliderChanged;
        GreenSlider.ValueChanged += OnSliderChanged;
        BlueSlider.ValueChanged += OnSliderChanged;

        Sync();
    }

    public Color SelectedColor
    {
        get => (Color)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>
    /// A deliberately small palette: greys and strong darks that hold up against a light
    /// background, because those are the colours that actually scan.
    /// </summary>
    private static IReadOnlyList<PaletteColor> Palette { get; } =
    [
        new("Black", Color.FromRgb(0x00, 0x00, 0x00)),
        new("Charcoal", Color.FromRgb(0x24, 0x29, 0x2E)),
        new("Slate", Color.FromRgb(0x3F, 0x4A, 0x59)),
        new("Navy", Color.FromRgb(0x1B, 0x2A, 0x4A)),
        new("Royal blue", Color.FromRgb(0x1B, 0x3F, 0x94)),
        new("Teal", Color.FromRgb(0x0F, 0x5C, 0x5C)),
        new("Forest", Color.FromRgb(0x1B, 0x4D, 0x2E)),
        new("Olive", Color.FromRgb(0x44, 0x51, 0x1F)),
        new("Burgundy", Color.FromRgb(0x6B, 0x14, 0x2A)),
        new("Crimson", Color.FromRgb(0x9B, 0x1B, 0x30)),
        new("Rust", Color.FromRgb(0x8C, 0x3D, 0x14)),
        new("Bronze", Color.FromRgb(0x8A, 0x6D, 0x3B)),
        new("Plum", Color.FromRgb(0x54, 0x24, 0x5C)),
        new("Indigo", Color.FromRgb(0x35, 0x2C, 0x6B)),
        new("Chocolate", Color.FromRgb(0x4A, 0x33, 0x24)),
        new("White", Color.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorPicker)d).Sync();

    /// <summary>Pushes the current colour into every part of the popup.</summary>
    private void Sync()
    {
        if (_updating)
        {
            return;
        }

        if (!_fromHsv)
        {
            AdoptHsvFrom(SelectedColor);
        }

        _updating = true;

        try
        {
            var colour = SelectedColor;

            SwatchFill.Fill = new SolidColorBrush(colour);
            HexBox.Text = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
            HexError.Visibility = Visibility.Collapsed;

            RedSlider.Value = colour.R;
            GreenSlider.Value = colour.G;
            BlueSlider.Value = colour.B;

            // The square's backdrop is the pure form of the current hue; the two gradient
            // layers above it supply the saturation and the darkening.
            SvHueLayer.Fill = new SolidColorBrush(HsvColor.ToRgb(_hue, 1, 1));

            PositionMarkers();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Takes hue, saturation and value from a colour that arrived from somewhere else: a
    /// palette swatch, the hex box, the RGB sliders, or whatever the control is bound to.
    /// Components the colour cannot define are left as they were, so a trip down to black
    /// and back up returns the hue and saturation the user last chose.
    /// </summary>
    private void AdoptHsvFrom(Color colour)
    {
        var hsv = HsvColor.FromRgb(colour);

        if (hsv.Saturation > 0)
        {
            _hue = hsv.Hue;
        }

        if (hsv.Value > 0)
        {
            _saturation = hsv.Saturation;
        }

        _value = hsv.Value;
    }

    /// <summary>Places the crosshair and the hue marker for the current values.</summary>
    private void PositionMarkers()
    {
        if (SvSquare.ActualWidth > 0 && SvSquare.ActualHeight > 0)
        {
            Canvas.SetLeft(SvMarker, (_saturation * SvSquare.ActualWidth) - (SvMarker.Width / 2));
            Canvas.SetTop(SvMarker, ((1 - _value) * SvSquare.ActualHeight) - (SvMarker.Height / 2));
        }

        if (HueStrip.ActualWidth > 0)
        {
            Canvas.SetLeft(HueMarker, ((_hue / 360.0) * HueStrip.ActualWidth) - (HueMarker.Width / 2));
            Canvas.SetTop(HueMarker, (HueStrip.ActualHeight - HueMarker.Height) / 2);
        }
    }

    /// <summary>
    /// The popup has no size until it first opens, so the markers cannot be placed during
    /// construction. This catches that first layout pass.
    /// </summary>
    private void OnPickerResized(object sender, SizeChangedEventArgs e) => PositionMarkers();

    /// <summary>
    /// Publishes the current hue, saturation and value as the selected colour, then
    /// refreshes the popup. The refresh is explicit because a small drag can land on the
    /// same 8 bit colour, which would raise no change notification and leave the marker
    /// stuck behind the cursor.
    /// </summary>
    private void CommitHsv()
    {
        _fromHsv = true;

        try
        {
            SelectedColor = HsvColor.ToRgb(_hue, _saturation, _value);
            Sync();
        }
        finally
        {
            _fromHsv = false;
        }
    }

    private void OnSquareMouseDown(object sender, MouseButtonEventArgs e)
    {
        SvSquare.CaptureMouse();
        TrackSquare(e.GetPosition(SvSquare));
    }

    private void OnSquareMouseMove(object sender, MouseEventArgs e)
    {
        if (SvSquare.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            TrackSquare(e.GetPosition(SvSquare));
        }
    }

    private void OnSquareMouseUp(object sender, MouseButtonEventArgs e) => SvSquare.ReleaseMouseCapture();

    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        HueStrip.CaptureMouse();
        TrackHue(e.GetPosition(HueStrip));
    }

    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (HueStrip.IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            TrackHue(e.GetPosition(HueStrip));
        }
    }

    private void OnHueMouseUp(object sender, MouseButtonEventArgs e) => HueStrip.ReleaseMouseCapture();

    /// <summary>
    /// Positions are clamped rather than ignored when the pointer leaves the control, so a
    /// drag that overshoots the edge pins to it instead of stopping dead.
    /// </summary>
    private void TrackSquare(Point p)
    {
        if (SvSquare.ActualWidth <= 0 || SvSquare.ActualHeight <= 0)
        {
            return;
        }

        SetFromSquare(p.X / SvSquare.ActualWidth, 1 - (p.Y / SvSquare.ActualHeight));
    }

    private void TrackHue(Point p)
    {
        if (HueStrip.ActualWidth <= 0)
        {
            return;
        }

        SetFromHue(Math.Clamp(p.X / HueStrip.ActualWidth, 0, 1) * 360.0);
    }

    /// <summary>The retained hue, saturation and value. Exposed so the tests can check
    /// that a trip through black or grey does not lose the user's place.</summary>
    internal (double Hue, double Saturation, double Value) CurrentHsv => (_hue, _saturation, _value);

    /// <summary>Selects a point in the saturation/value square, both 0 to 1.</summary>
    internal void SetFromSquare(double saturation, double value)
    {
        _saturation = Math.Clamp(saturation, 0, 1);
        _value = Math.Clamp(value, 0, 1);
        CommitHsv();
    }

    /// <summary>Selects a hue in degrees. Wraps, so 360 and 0 mean the same thing.</summary>
    internal void SetFromHue(double hue)
    {
        _hue = ((hue % 360) + 360) % 360;
        CommitHsv();
    }

    private void OnPaletteColorClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: Color colour })
        {
            SelectedColor = colour;
        }
    }

    private void OnSliderChanged(object? sender, EventArgs e)
    {
        if (_updating)
        {
            return;
        }

        SelectedColor = Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);
    }

    private void OnHexKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void OnHexCommitted(object sender, RoutedEventArgs e) => CommitHex();

    /// <summary>
    /// Accepts what people actually type: with or without the hash, three or six digits.
    /// Anything else leaves the current colour alone and says so, rather than silently
    /// snapping to black.
    /// </summary>
    private void CommitHex()
    {
        if (_updating)
        {
            return;
        }

        var text = HexBox.Text.Trim().TrimStart('#');

        if (text.Length == 3)
        {
            text = string.Concat(text.Select(c => new string(c, 2)));
        }

        if (text.Length == 6 && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            HexError.Visibility = Visibility.Collapsed;
            SelectedColor = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
            return;
        }

        HexError.Visibility = Visibility.Visible;
    }

    private sealed record PaletteColor(string Name, Color Color)
    {
        public Brush Brush { get; } = CreateBrush(Color);

        private static Brush CreateBrush(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }
    }
}

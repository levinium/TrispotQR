using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using TrispotQR.Core.Primitives;
using TrispotQR.UI.Rendering;

// Avalonia.Media has an HsvColor of its own, so the one this picker's arithmetic is written
// against is named explicitly rather than left to a using directive to disambiguate.
using HsvColor = TrispotQR.Core.Styling.HsvColor;

namespace TrispotQR.UI.Controls;

/// <summary>
/// A swatch button that opens a small colour picker.
///
/// Built rather than borrowed because a curated palette plus hex entry is what someone
/// matching a brand colour actually needs, and neither Avalonia nor any platform's colour
/// dialog offers both.
///
/// Four ways in, all equal: the saturation/value square with its hue strip, the palette, the
/// hex box and the RGB sliders. They stay in step because none of them decides anything --
/// every one of them asks <see cref="ColorPickerState"/> and then publishes what it says.
/// This file owns only what genuinely needs a toolkit: pointers, layout and paint.
/// </summary>
public partial class ColorPicker : UserControl
{
    /// <summary>
    /// Two-way by default, which is the whole point of the control: the styling panel binds a
    /// colour in and expects the user's edits to come back out without writing Mode=TwoWay at
    /// seven call sites. <see cref="ColorPickerTests"/> drives real gestures through to a bound
    /// source precisely so that losing this stays impossible to miss.
    ///
    /// The value is an <see cref="RgbColor"/>, not an Avalonia <see cref="Color"/>. The WPF
    /// picker exposed a toolkit colour and converted at every call site; RgbColor is already
    /// the platform-neutral type the view models speak, so there is nothing to convert.
    /// </summary>
    public static readonly StyledProperty<RgbColor> SelectedColorProperty =
        AvaloniaProperty.Register<ColorPicker, RgbColor>(
            nameof(SelectedColor), RgbColor.Black, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// The other colours the thing being styled is already wearing, offered so one part can be
    /// matched to another without reading a hex code off a different picker.
    ///
    /// The control neither knows nor cares where these come from; the window binds them. That is
    /// what keeps a picker usable anywhere, including in its own tests, without dragging a view
    /// model in behind it.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<RgbColor>?> ColorsInUseProperty =
        AvaloniaProperty.Register<ColorPicker, IEnumerable<RgbColor>?>(nameof(ColorsInUse));

    /// <summary>Colours chosen before, newest first. Bound in the same way and for the same reason.</summary>
    public static readonly StyledProperty<IEnumerable<RgbColor>?> RecentColorsProperty =
        AvaloniaProperty.Register<ColorPicker, IEnumerable<RgbColor>?>(nameof(RecentColors));

    /// <summary>
    /// Raised when the user finishes with a colour, meaning the popup closed on a different
    /// colour from the one it opened on.
    /// </summary>
    ///
    /// A routed event rather than a plain one, and this is load bearing: the pickers live in
    /// three different places, two of them inside collapsed panels that are not realised at
    /// window load, so nothing can reliably subscribe to each instance. Bubbling lets the window
    /// add one handler and hear from every picker it will ever contain.
    ///
    /// It carries no colour of its own. The sender is the picker and its SelectedColor is the
    /// answer, so an args type would only be a second copy of a value already in reach.
    public static readonly RoutedEvent<RoutedEventArgs> ColorCommittedEvent =
        RoutedEvent.Register<ColorPicker, RoutedEventArgs>(
            nameof(ColorCommitted), RoutingStrategies.Bubble);

    public event EventHandler<RoutedEventArgs>? ColorCommitted
    {
        add => AddHandler(ColorCommittedEvent, value);
        remove => RemoveHandler(ColorCommittedEvent, value);
    }

    private readonly ColorPickerState _state = new();

    /// <summary>
    /// The colour the popup was showing when it opened, so closing can tell a change from a
    /// look. Without it, opening and dismissing a picker would file the colour as freshly
    /// chosen and push it to the front of the recent list.
    /// </summary>
    private RgbColor _colorWhenOpened;

    /// <summary>
    /// Set while <see cref="Sync"/> is writing the current colour into the popup's parts, so
    /// that a slider moving under its own programme is not mistaken for the user moving it.
    /// </summary>
    private bool _updating;

    public ColorPicker()
    {
        InitializeComponent();

        PaletteItems.ItemsSource = Palette;

        RedSlider.ValueChanged += OnSliderChanged;
        GreenSlider.ValueChanged += OnSliderChanged;
        BlueSlider.ValueChanged += OnSliderChanged;

        // The button and the popup, kept in step by hand rather than by a two-way binding
        // between a bool? and a bool. The Closed half is not optional: light dismiss closes the
        // popup without going anywhere near the button, and a button left checked afterwards
        // takes two clicks to reopen.
        SwatchButton.IsCheckedChanged += (_, _) => PickerPopup.IsOpen = SwatchButton.IsChecked == true;
        PickerPopup.Closed += (_, _) => SwatchButton.IsChecked = false;

        PickerPopup.Opened += (_, _) =>
        {
            _colorWhenOpened = SelectedColor;
            SyncOfferedColors();
        };

        PickerPopup.Closed += (_, _) =>
        {
            if (SelectedColor != _colorWhenOpened)
            {
                RaiseEvent(new RoutedEventArgs(ColorCommittedEvent));
            }
        };

        Sync();
    }

    public IEnumerable<RgbColor>? ColorsInUse
    {
        get => GetValue(ColorsInUseProperty);
        set => SetValue(ColorsInUseProperty, value);
    }

    public IEnumerable<RgbColor>? RecentColors
    {
        get => GetValue(RecentColorsProperty);
        set => SetValue(RecentColorsProperty, value);
    }

    /// <summary>
    /// Fills the two offered rows and hides either one that has nothing to offer.
    ///
    /// Run when the popup opens rather than when the bound lists change, because that is the
    /// only moment the contents are about to be looked at, and it means a colour recorded while
    /// this picker was open is already there the next time it is.
    /// </summary>
    private void SyncOfferedColors()
    {
        // A picker never offers the colour it is already showing: choosing it would change
        // nothing, and it takes a slot from a colour that would.
        var inUse = (ColorsInUse ?? [])
            .Where(c => c != SelectedColor)
            .Select(c => new PaletteColor(c.ToHex(), c))
            .ToList();

        var recent = (RecentColors ?? [])
            .Where(c => c != SelectedColor)
            .Select(c => new PaletteColor(c.ToHex(), c))
            .ToList();

        InUseItems.ItemsSource = inUse;
        InUseGroup.IsVisible = inUse.Count > 0;

        RecentItems.ItemsSource = recent;
        RecentGroup.IsVisible = recent.Count > 0;
    }

    public RgbColor SelectedColor
    {
        get => GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    /// <summary>
    /// A deliberately small palette: greys and strong darks that hold up against a light
    /// background, because those are the colours that actually scan.
    /// </summary>
    private static IReadOnlyList<PaletteColor> Palette { get; } =
    [
        new("Black", RgbColor.FromRgb(0x00, 0x00, 0x00)),
        new("Charcoal", RgbColor.FromRgb(0x24, 0x29, 0x2E)),
        new("Slate", RgbColor.FromRgb(0x3F, 0x4A, 0x59)),
        new("Navy", RgbColor.FromRgb(0x1B, 0x2A, 0x4A)),
        new("Royal blue", RgbColor.FromRgb(0x1B, 0x3F, 0x94)),
        new("Teal", RgbColor.FromRgb(0x0F, 0x5C, 0x5C)),
        new("Forest", RgbColor.FromRgb(0x1B, 0x4D, 0x2E)),
        new("Olive", RgbColor.FromRgb(0x44, 0x51, 0x1F)),
        new("Burgundy", RgbColor.FromRgb(0x6B, 0x14, 0x2A)),
        new("Crimson", RgbColor.FromRgb(0x9B, 0x1B, 0x30)),
        new("Rust", RgbColor.FromRgb(0x8C, 0x3D, 0x14)),
        new("Bronze", RgbColor.FromRgb(0x8A, 0x6D, 0x3B)),
        new("Plum", RgbColor.FromRgb(0x54, 0x24, 0x5C)),
        new("Indigo", RgbColor.FromRgb(0x35, 0x2C, 0x6B)),
        new("Chocolate", RgbColor.FromRgb(0x4A, 0x33, 0x24)),
        new("White", RgbColor.FromRgb(0xFF, 0xFF, 0xFF)),
    ];

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property != SelectedColorProperty)
        {
            return;
        }

        var incoming = SelectedColor;

        // Only adopt a colour that did not come from here. ColorPickerState keeps hue and
        // saturation of its own precisely because deriving them from an 8 bit colour is lossy;
        // handing its own answer straight back would round-trip them through that loss on every
        // step of a drag and let the crosshair drift out from under the cursor.
        if (incoming != _state.Color)
        {
            _state.Color = incoming;
        }

        Sync();
    }

    /// <summary>Pushes the current colour into every part of the popup.</summary>
    private void Sync()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;

        try
        {
            var colour = _state.Color;

            SwatchFill.Fill = AvaloniaGeometry.ToBrush(colour);
            HexBox.Text = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
            HexError.IsVisible = false;

            RedSlider.Value = colour.R;
            GreenSlider.Value = colour.G;
            BlueSlider.Value = colour.B;

            RedValue.Text = colour.R.ToString(CultureInfo.InvariantCulture);
            GreenValue.Text = colour.G.ToString(CultureInfo.InvariantCulture);
            BlueValue.Text = colour.B.ToString(CultureInfo.InvariantCulture);

            // The square's backdrop is the pure form of the current hue; the two gradient
            // layers above it supply the saturation and the darkening.
            SvHueLayer.Fill = AvaloniaGeometry.ToBrush(new HsvColor(_state.Hue, 1, 1).ToRgb(255));

            PositionMarkers();
        }
        finally
        {
            _updating = false;
        }
    }

    /// <summary>
    /// Publishes whatever the state now holds and repaints. The repaint is explicit rather than
    /// left to the property-changed handler because a small drag can land on the same 8 bit
    /// colour, which raises no change notification and would leave the marker behind the cursor.
    /// </summary>
    private void Publish()
    {
        SelectedColor = _state.Color;
        Sync();
    }

    /// <summary>Places the crosshair and the hue marker for the current values.</summary>
    private void PositionMarkers()
    {
        if (SvSquare.Bounds is { Width: > 0, Height: > 0 })
        {
            Canvas.SetLeft(SvMarker, (_state.Saturation * SvSquare.Bounds.Width) - (SvMarker.Width / 2));
            Canvas.SetTop(SvMarker, ((1 - _state.Value) * SvSquare.Bounds.Height) - (SvMarker.Height / 2));
        }

        if (HueStrip.Bounds.Width > 0)
        {
            Canvas.SetLeft(HueMarker, ((_state.Hue / 360.0) * HueStrip.Bounds.Width) - (HueMarker.Width / 2));
            Canvas.SetTop(HueMarker, (HueStrip.Bounds.Height - HueMarker.Height) / 2);
        }
    }

    /// <summary>
    /// The popup has no size until it first opens, so the markers cannot be placed during
    /// construction. This catches that first layout pass.
    /// </summary>
    private void OnPickerResized(object? sender, SizeChangedEventArgs e) => PositionMarkers();

    /// <summary>
    /// Capture is what makes a drag survive leaving the control. Without it the very first move
    /// past the edge goes to whatever is under the pointer instead, and the colour stops dead
    /// at the boundary; with it the square keeps receiving moves and clamps them.
    /// </summary>
    private void OnSquarePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SvSquare).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(SvSquare);
        TrackSquare(e.GetPosition(SvSquare));
    }

    private void OnSquarePointerMoved(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer.Captured, SvSquare))
        {
            TrackSquare(e.GetPosition(SvSquare));
        }
    }

    private void OnSquarePointerReleased(object? sender, PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    private void OnHuePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(HueStrip).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(HueStrip);
        TrackHue(e.GetPosition(HueStrip));
    }

    private void OnHuePointerMoved(object? sender, PointerEventArgs e)
    {
        if (ReferenceEquals(e.Pointer.Captured, HueStrip))
        {
            TrackHue(e.GetPosition(HueStrip));
        }
    }

    private void OnHuePointerReleased(object? sender, PointerReleasedEventArgs e) => e.Pointer.Capture(null);

    /// <summary>
    /// Out of range positions are handed on rather than dropped, because ColorPickerState
    /// clamps them: a drag that overshoots the edge pins to it instead of stopping dead.
    /// </summary>
    private void TrackSquare(Point p)
    {
        if (SvSquare.Bounds is not { Width: > 0, Height: > 0 })
        {
            return;
        }

        _state.SetSaturationValue(p.X / SvSquare.Bounds.Width, 1 - (p.Y / SvSquare.Bounds.Height));
        Publish();
    }

    /// <summary>
    /// Clamped here rather than left to the state, because the state wraps a hue instead of
    /// clamping it. Wrapping is right for 370 degrees; it is wrong for a pointer dragged off the
    /// right-hand end of the strip, which should stick at red rather than jump back to it.
    /// </summary>
    private void TrackHue(Point p)
    {
        if (HueStrip.Bounds.Width <= 0)
        {
            return;
        }

        _state.SetHue(Math.Clamp(p.X / HueStrip.Bounds.Width, 0, 1) * 360.0);
        Publish();
    }

    private void OnPaletteColorClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: PaletteColor swatch })
        {
            _state.Color = swatch.Color;
            Publish();
        }
    }

    /// <summary>
    /// The alpha is carried across rather than reset, because these three sliders only speak for
    /// red, green and blue. The app offers part-transparent backgrounds, and nudging the red on
    /// one should not quietly make it opaque.
    /// </summary>
    private void OnSliderChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updating)
        {
            return;
        }

        _state.Color = RgbColor.FromArgb(
            _state.Color.A, (byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);

        Publish();
    }

    private void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHex();
            e.Handled = true;
        }
    }

    private void OnHexCommitted(object? sender, RoutedEventArgs e) => CommitHex();

    /// <summary>
    /// What is and is not a colour code is <see cref="ColorPickerState"/>'s business; all this
    /// does is say so when the answer is no, rather than letting the picker snap to black.
    /// </summary>
    private void CommitHex()
    {
        if (_updating)
        {
            return;
        }

        if (_state.TrySetHex(HexBox.Text))
        {
            Publish();
            return;
        }

        HexError.IsVisible = true;
    }
}

/// <summary>
/// One entry in the picker's palette: a colour, the name shown in its tooltip, and the brush
/// that paints it. A namespace-level type rather than a nested one so the item template can
/// name it in x:DataType, which is what keeps the palette's bindings compiled.
/// </summary>
internal sealed record PaletteColor(string Name, RgbColor Color)
{
    public IBrush Brush { get; } = AvaloniaGeometry.ToBrush(Color);
}

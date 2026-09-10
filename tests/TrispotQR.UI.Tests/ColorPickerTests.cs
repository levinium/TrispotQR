using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using TrispotQR.Core.Primitives;
using TrispotQR.UI.Controls;

// Avalonia.Media has an HsvColor of its own; this file means Core's.
using HsvColor = TrispotQR.Core.Styling.HsvColor;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The colour picker, driven the way a user drives it.
///
/// Every gesture here goes through Avalonia's own hit-testing and routed-event pipeline --
/// a real pointer press on the rendered swatch, a real drag across the rendered square, real
/// typing into the rendered hex box -- rather than calling a handler or setting a property.
/// Phase 2c found that nothing on this branch drove the UI that way, which meant a one-way
/// binding would have been invisible: seventeen boxes could have done nothing at all with
/// every test still green. These tests exist so that cannot happen here.
///
/// What is deliberately *not* tested through the control is the colour arithmetic itself.
/// That lives in ColorPickerState, has its own suite, and needs no window.
/// </summary>
public class ColorPickerTests
{
    private static readonly RgbColor Navy = RgbColor.FromRgb(0x1B, 0x2A, 0x4A);

    [AvaloniaFact]
    public void TheSwatchIsPaintedInTheSelectedColour()
    {
        var (_, picker) = Open();

        picker.SelectedColor = Navy;
        DispatcherPump.Drain();

        // The rendered fill, not the property that was just assigned: a swatch that never
        // repaints would satisfy the property and show the wrong colour.
        Assert.Equal(Color.FromArgb(255, 0x1B, 0x2A, 0x4A), SolidFill(Part<Rectangle>(picker, "SwatchFill")));
    }

    [AvaloniaFact]
    public void SettingTheColourFromOutsideUpdatesEveryPartOfThePopup()
    {
        var (window, picker) = Open();

        picker.SelectedColor = Navy;
        OpenPopup(window, picker);

        Assert.Equal("#1B2A4A", Part<TextBox>(picker, "HexBox").Text);
        Assert.Equal(0x1B, Part<Slider>(picker, "RedSlider").Value);
        Assert.Equal(0x2A, Part<Slider>(picker, "GreenSlider").Value);
        Assert.Equal(0x4A, Part<Slider>(picker, "BlueSlider").Value);

        // The square's backdrop is the pure form of the current hue, which for navy is a
        // fully saturated blue rather than navy itself.
        var pureHue = new HsvColor(HsvColor.FromRgb(Navy).Hue, 1, 1).ToRgb(255);
        Assert.Equal(Color.FromArgb(255, pureHue.R, pureHue.G, pureHue.B), SolidFill(Part<Rectangle>(picker, "SvHueLayer")));
    }

    [AvaloniaFact]
    public void SettingTheColourFromOutsideMovesTheMarkersOverTheChosenPoint()
    {
        var (window, picker) = Open();

        picker.SelectedColor = RgbColor.FromRgb(0xFF, 0x00, 0x00);
        OpenPopup(window, picker);

        // Pure red is fully saturated and fully bright, which puts the crosshair at the top
        // right of the square and the hue marker hard against its left edge.
        var square = Part<Control>(picker, "SvSquare");
        var marker = Part<Control>(picker, "SvMarker");
        Assert.Equal(square.Bounds.Width - (marker.Width / 2), Canvas.GetLeft(marker), 1);
        Assert.Equal(-(marker.Height / 2), Canvas.GetTop(marker), 1);

        var strip = Part<Control>(picker, "HueStrip");
        var hueMarker = Part<Control>(picker, "HueMarker");
        Assert.Equal(-(hueMarker.Width / 2), Canvas.GetLeft(hueMarker), 1);
        Assert.True(strip.Bounds.Width > 0, "the hue strip never got a layout pass");
    }

    [AvaloniaFact]
    public void DraggingAcrossTheSquareCarriesTheNewColourOutThroughATwoWayBinding()
    {
        // The round trip that matters: a real drag on the rendered square, all the way out to
        // a bound source object. Nothing in this test touches SelectedColor or ColorPickerState
        // directly, so a binding that only carried values inwards would fail it.
        var holder = new ColourHolder { Colour = RgbColor.FromRgb(0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        var square = Part<Control>(picker, "SvSquare");

        // Press at the top right (full saturation, full brightness) and drag to the middle of
        // the left edge, which is a mid-grey: saturation zero, value one half.
        Drag(window, At(square, 0.98, 0.02), At(square, 0.0, 0.5));

        Assert.NotEqual(RgbColor.FromRgb(0xFF, 0x00, 0x00), holder.Colour);
        Assert.Equal(holder.Colour.R, holder.Colour.G);
        Assert.Equal(holder.Colour.G, holder.Colour.B);
        Assert.InRange(holder.Colour.R, 0x70, 0x90);
    }

    [AvaloniaFact]
    public void DraggingTheHueStripCarriesTheNewHueOutThroughATwoWayBinding()
    {
        var holder = new ColourHolder { Colour = RgbColor.FromRgb(0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        var strip = Part<Control>(picker, "HueStrip");

        // A third of the way along the strip is 120 degrees, which is pure green.
        Drag(window, At(strip, 0.02, 0.5), At(strip, 1.0 / 3.0, 0.5));

        Assert.Equal(RgbColor.FromRgb(0x00, 0xFF, 0x00), holder.Colour);
    }

    [AvaloniaFact]
    public void ADragThatLeavesTheSquareKeepsTrackingAndPinsToTheEdge()
    {
        // Pointer capture, proved from outside: the pointer ends up well beyond the square's
        // own bounds, over a different control entirely, and the square is still the thing
        // reading it. Without the capture the move would be delivered elsewhere and the colour
        // would stop at wherever the pointer crossed the edge.
        var holder = new ColourHolder { Colour = RgbColor.FromRgb(0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        var square = Part<Control>(picker, "SvSquare");

        var start = At(square, 0.5, 0.5);
        var outside = At(square, 0.5, 0.5) + new Vector(0, square.Bounds.Height * 3);

        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        DispatcherPump.Drain();
        window.MouseMove(outside, RawInputModifiers.LeftMouseButton);
        DispatcherPump.Drain();
        window.MouseUp(outside, MouseButton.Left);
        DispatcherPump.Drain();

        // Dragged far below the bottom edge, so the value pins to zero: black.
        Assert.Equal(RgbColor.FromRgb(0x00, 0x00, 0x00), holder.Colour);
    }

    [AvaloniaFact]
    public void ClickingAPaletteSwatchCarriesThatColourOutThroughATwoWayBinding()
    {
        var holder = new ColourHolder { Colour = RgbColor.White };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);

        // The first swatch is black, and starting from white makes that an unmistakable change.
        Click(window, At(PaletteSwatches(picker).First(), 0.5, 0.5));

        Assert.Equal(RgbColor.Black, holder.Colour);
    }

    [AvaloniaFact]
    public void TypingAHexCodeAndPressingEnterCarriesItOutThroughATwoWayBinding()
    {
        var holder = new ColourHolder { Colour = RgbColor.Black };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);

        TypeHex(window, picker, "#1B2A4A");

        Assert.Equal(Navy, holder.Colour);
    }

    [AvaloniaFact]
    public void AHexCodeThatIsNotAColourSaysSoAndLeavesTheColourAlone()
    {
        var holder = new ColourHolder { Colour = Navy };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);

        TypeHex(window, picker, "not a colour");

        Assert.Equal(Navy, holder.Colour);
        Assert.True(Part<Control>(picker, "HexError").IsVisible, "the picker silently ignored an unusable hex code");
    }

    [AvaloniaFact]
    public void MovingAnRgbSliderCarriesTheNewColourOutThroughATwoWayBinding()
    {
        var holder = new ColourHolder { Colour = RgbColor.Black };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);

        // A press on the track, not a value assignment. Avalonia's Slider moves to the point
        // pressed, so this lands near the right-hand end of the red channel.
        Click(window, At(Part<Slider>(picker, "RedSlider"), 0.98, 0.5));

        Assert.InRange(holder.Colour.R, 0xC0, 0xFF);
        Assert.Equal(0x00, holder.Colour.G);
        Assert.Equal(0x00, holder.Colour.B);
    }

    [AvaloniaFact]
    public void APartTransparentColourKeepsItsAlphaThroughAHexEdit()
    {
        // The app offers part-transparent backgrounds, and the hex box has no alpha digits, so
        // the alpha has to come from the colour being edited rather than from the text.
        var holder = new ColourHolder { Colour = RgbColor.FromArgb(0x80, 0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        TypeHex(window, picker, "#1B2A4A");

        Assert.Equal(RgbColor.FromArgb(0x80, 0x1B, 0x2A, 0x4A), holder.Colour);
    }

    [AvaloniaFact]
    public void APartTransparentColourKeepsItsAlphaThroughAHueDrag()
    {
        var holder = new ColourHolder { Colour = RgbColor.FromArgb(0x80, 0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        var strip = Part<Control>(picker, "HueStrip");
        Drag(window, At(strip, 0.02, 0.5), At(strip, 1.0 / 3.0, 0.5));

        Assert.Equal(RgbColor.FromArgb(0x80, 0x00, 0xFF, 0x00), holder.Colour);
    }

    [AvaloniaFact]
    public void APartTransparentColourKeepsItsAlphaThroughASaturationDrag()
    {
        var holder = new ColourHolder { Colour = RgbColor.FromArgb(0x80, 0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        var square = Part<Control>(picker, "SvSquare");

        // Top right to the middle of the left edge: saturation to zero, value to one half,
        // which is a mid-grey. The alpha has to come through untouched.
        Drag(window, At(square, 0.98, 0.02), At(square, 0.0, 0.5));

        Assert.Equal(0x80, holder.Colour.A);
        Assert.Equal(holder.Colour.R, holder.Colour.G);
        Assert.Equal(holder.Colour.G, holder.Colour.B);
        Assert.InRange(holder.Colour.R, 0x70, 0x90);
    }

    [AvaloniaFact]
    public void APaletteSwatchReplacesTheWholeColourIncludingItsAlpha()
    {
        // Pinned because it is the one place the picker deliberately does not preserve alpha,
        // and the reasoning is invisible from the code. A palette entry is a named opaque
        // colour; choosing one is choosing the whole colour, not editing the current one the
        // way the square, the strip and the hex box do.
        var holder = new ColourHolder { Colour = RgbColor.FromArgb(0x80, 0xFF, 0x00, 0x00) };
        var (window, picker) = Open(holder);

        OpenPopup(window, picker);
        Click(window, At(PaletteSwatches(picker).First(), 0.5, 0.5));

        Assert.Equal(RgbColor.Black, holder.Colour);
    }

    [AvaloniaFact]
    public void EveryPaletteSwatchIsAPlainColourChipRatherThanAThemedButton()
    {
        // A guard for the Button.palette style, not for the palette itself. This codebase has
        // twice shipped a style that silently did nothing while every test stayed green, once
        // because an Avalonia type selector does not match subclasses. Asserting the rendered
        // properties the setters control -- on the buttons the ItemsControl actually realised,
        // reached through the visual tree -- is what makes deleting the style fail a test
        // instead of quietly restoring the theme's padded, accent-bordered button.
        var (window, picker) = Open();
        OpenPopup(window, picker);

        var swatches = PaletteSwatches(picker).ToList();
        Assert.Equal(16, swatches.Count);

        foreach (var swatch in swatches)
        {
            Assert.Equal(new Thickness(0), swatch.Padding);
            Assert.Equal(new Thickness(1), swatch.BorderThickness);
            Assert.Equal(new CornerRadius(4), swatch.CornerRadius);
            Assert.Equal(Color.FromArgb(0x40, 0, 0, 0), ((ISolidColorBrush)swatch.BorderBrush!).Color);
        }
    }

    /// <summary>
    /// The captions the popup writes out in full, by the words they show: one over each of the
    /// three sections, and one naming each of the three colour channels. The channel readouts
    /// show whatever number the colour currently is, so they are pinned by name instead.
    /// </summary>
    private static readonly string[] Captions =
    [
        "Pick a colour", "Or start from a preset", "Or type a colour code",
        "Red", "Green", "Blue",
    ];

    /// <summary>The three channel readouts, whose text moves with the colour.</summary>
    private static readonly string[] Readouts = ["RedValue", "GreenValue", "BlueValue"];

    [AvaloniaFact]
    public void EveryCaptionInThePopupIsSetInTheCaptionTreatment()
    {
        // A guard for TextBlock.caption, in the shape StylingPanelTests uses for headings and
        // labels. Each caption is found by the words it shows -- or, for the three readouts, by
        // its name -- then checked for the class and for the two properties the class's setter
        // controls. Both halves are load bearing: without the class check a caption could
        // quietly lose it, and without the FontSize and Foreground checks the style itself could
        // be deleted, which is the failure this codebase has shipped twice.
        //
        // This replaces a version that collected the TextBlocks already carrying the class and
        // asserted there were nine of them, under a comment claiming that caught a caption added
        // later without the class. It could not: an unclassed caption is invisible to a search
        // for the class, so the nine never moved. Verified by injecting an unclassed TextBlock
        // into the popup, which left the whole suite green.
        //
        // The backstop below is what catches that case now. Every TextBlock written into this
        // control's own markup is a caption, with exactly one exception -- the hex error line,
        // which carries a warning treatment of its own -- so anything else authored here without
        // the class fails. It is scoped to authored TextBlocks (TemplatedParent is null) because
        // the hex box and the sliders bring TextBlocks of their own from their control
        // templates, which this control neither writes nor styles.
        //
        // That scoping has one consequence worth stating rather than discovering: a TextBlock
        // authored inside a ControlTemplate written in THIS file would also carry a
        // TemplatedParent, so the backstop would not see it. This file authors no
        // ControlTemplates today, so there is nothing it misses; if one is ever added here,
        // this guard stops covering its contents.
        var (window, picker) = Open();
        OpenPopup(window, picker);

        var captions = new List<TextBlock>();

        foreach (var text in Captions)
        {
            var caption = Assert.Single(PopupTextBlocks(picker), t => t.Text == text);

            Assert.True(caption.Classes.Contains("caption"), $"the \"{text}\" caption is not classed as one");
            captions.Add(caption);
        }

        foreach (var name in Readouts)
        {
            var readout = Part<TextBlock>(picker, name);

            Assert.True(readout.Classes.Contains("caption"), $"the {name} readout is not classed as a caption");
            captions.Add(readout);
        }

        // The colour is the palette's muted text rather than a literal, because the caption
        // treatment was themed in the final fix wave. Read back for whichever variant the app is
        // currently in, so this stays a guard on the setter being applied at all; that it moves
        // with the theme is ThemeTests's job.
        var muted = MutedTextColour();

        foreach (var caption in captions)
        {
            Assert.Equal(12d, caption.FontSize);
            Assert.Equal(muted, ((ISolidColorBrush)caption.Foreground!).Color);
        }

        var loose = PopupTextBlocks(picker)
            .Where(t => t.TemplatedParent is null && t.Name != "HexError")
            .Where(t => !t.Classes.Contains("caption"))
            .Select(t => t.Name ?? (string.IsNullOrEmpty(t.Text) ? "an unnamed empty TextBlock" : $"\"{t.Text}\""))
            .ToList();

        Assert.True(
            loose.Count == 0,
            "these are written into the popup's own markup, where everything but the hex error "
                + $"line is a caption, and are not classed as one: {string.Join(", ", loose)}");
    }

    /// <summary>The palette's muted text colour, in the variant the app is currently showing.</summary>
    private static Color MutedTextColour()
    {
        Assert.True(
            Application.Current!.TryGetResource("MutedTextBrush", Application.Current.ActualThemeVariant, out var value),
            "the palette has no MutedTextBrush");

        return ((ISolidColorBrush)value!).Color;
    }

    /// <summary>Every TextBlock the open popup is showing, template-generated ones included.</summary>
    private static IEnumerable<TextBlock> PopupTextBlocks(ColorPicker picker) =>
        PopupContent(picker).GetSelfAndVisualDescendants().OfType<TextBlock>();

    [AvaloniaFact]
    public void TheSwatchStaysAPlainColourFrameWhileThePopupIsOpen()
    {
        // The :checked half of the swatch styling. Without it the Fluent theme paints its accent
        // colour behind the swatch for exactly as long as the popup is open, which is exactly
        // when the user is looking at the colour they are choosing.
        var (window, picker) = Open();
        picker.SelectedColor = Navy;

        OpenPopup(window, picker);

        var button = Part<ToggleButton>(picker, "SwatchButton");
        Assert.True(button.IsChecked, "opening the popup did not leave the swatch button checked");
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)Presenter(button).Background!).Color);
    }

    [AvaloniaFact]
    public void TheSwatchStaysAPlainColourFrameUnderThePointer()
    {
        // The :pointerover half. The headless backend does raise real pointer-over state -- the
        // assertion below on IsPointerOver is there so this cannot pass vacuously if that ever
        // stops being true, because a hover that never happened would leave the presenter at the
        // resting background and look exactly like a working style.
        var (window, picker) = Open();
        picker.SelectedColor = Navy;
        DispatcherPump.Drain();

        var button = Part<ToggleButton>(picker, "SwatchButton");
        window.MouseMove(At(button, 0.5, 0.5));
        DispatcherPump.Drain();

        Assert.True(button.IsPointerOver, "the headless backend no longer reports pointer-over, so this proves nothing");
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)Presenter(button).Background!).Color);
    }

    [AvaloniaFact]
    public void ATransparentColourShowsTheCheckerboardRatherThanReadingAsWhite()
    {
        var (window, picker) = Open();

        picker.SelectedColor = RgbColor.Transparent;
        DispatcherPump.Drain();

        Assert.True(
            SwatchColours(window, picker).Count > 1,
            "a fully transparent swatch came out a single flat colour, so the checkerboard is not showing through");
    }

    [AvaloniaFact]
    public void AnOpaqueColourHidesTheCheckerboardCompletely()
    {
        // The other half of the pair. Without it, a swatch that always showed the checkerboard
        // -- or one that never painted the colour at all -- would pass the transparency test.
        var (window, picker) = Open();

        picker.SelectedColor = Navy;
        DispatcherPump.Drain();

        Assert.Equal(["1B2A4A"], SwatchColours(window, picker));
    }

    /// <summary>
    /// Opens a window holding one picker, optionally bound two-way to <paramref name="source"/>.
    /// The window is deliberately roomy: the popup is laid out inside it, so a cramped one would
    /// push parts of the picker off the bottom and give them nothing to hit-test against.
    /// </summary>
    private static (Window Window, ColorPicker Picker) Open(ColourHolder? source = null)
    {
        var picker = new ColorPicker { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };

        if (source is not null)
        {
            picker.DataContext = source;

            // A plain Binding, with no explicit Mode. SelectedColorProperty is registered as
            // two-way by default, and that default is exactly what these tests are here to
            // prove: writing Mode=TwoWay here would hide a property that had lost it.
            picker.Bind(ColorPicker.SelectedColorProperty, new Binding(nameof(ColourHolder.Colour)));
        }

        var window = new Window { Width = 400, Height = 520, Content = picker, Background = Brushes.White };
        window.Show();
        DispatcherPump.Drain();

        return (window, picker);
    }

    /// <summary>
    /// Opens the popup by clicking the swatch, the way a user does, and waits for the contents
    /// to be laid out. In the headless backend a Popup has no platform window to live in, so
    /// its child is hosted in this window's own overlay layer -- which is why every gesture
    /// below is sent to the window rather than to a separate popup root.
    /// </summary>
    private static void OpenPopup(Window window, ColorPicker picker)
    {
        Click(window, At(Part<ToggleButton>(picker, "SwatchButton"), 0.5, 0.5));

        var square = Part<Control>(picker, "SvSquare");
        Assert.True(
            DispatcherPump.DrainUntil(() => square.Bounds.Width > 0 && square.Bounds.Height > 0),
            "clicking the swatch did not open a laid-out popup");
    }

    private static T Part<T>(ColorPicker picker, string name)
        where T : Control =>
        picker.FindControl<T>(name) ?? throw new InvalidOperationException($"ColorPicker has no {name}.");

    /// <summary>
    /// What the popup is showing. Not a visual descendant of the picker: an open popup's content
    /// is hosted by the window (its overlay layer, in the headless backend), so walking down from
    /// the picker finds nothing at all. Named elements inside it are still reachable by name,
    /// because the whole thing shares one name scope; anonymous ones have to be found from here.
    /// </summary>
    private static Control PopupContent(ColorPicker picker) =>
        Part<Popup>(picker, "PickerPopup").Child
            ?? throw new InvalidOperationException("The picker's popup has no content.");

    /// <summary>The palette buttons the ItemsControl actually realised, in the order shown.</summary>
    private static IEnumerable<Button> PaletteSwatches(ColorPicker picker) =>
        Part<ItemsControl>(picker, "PaletteItems").GetVisualDescendants().OfType<Button>();

    /// <summary>
    /// The presenter inside a templated control, which is where a "/template/ ContentPresenter"
    /// setter lands. The control's own Background says nothing about it: the theme's checked and
    /// hover treatments are written against the presenter, not the control.
    /// </summary>
    private static ContentPresenter Presenter(TemplatedControl control) =>
        control.GetVisualDescendants().OfType<ContentPresenter>().First();

    // At, Click and Drag now live on UiHarness: the styling panel drives the same gestures at
    // rendered controls, and one copy of the M31/M32 arithmetic is enough.
    private static Point At(Control control, double fractionX, double fractionY) =>
        UiHarness.At(control, fractionX, fractionY);

    private static void Click(Window window, Point point) => UiHarness.Click(window, point);

    private static void Drag(Window window, Point from, Point to) => UiHarness.Drag(window, from, to);

    /// <summary>Selects the hex box, types over it and commits with Enter, as a user would.</summary>
    private static void TypeHex(Window window, ColorPicker picker, string text)
    {
        var box = Part<TextBox>(picker, "HexBox");
        Click(window, At(box, 0.5, 0.5));
        box.SelectAll();
        window.KeyTextInput(text);
        DispatcherPump.Drain();
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        window.KeyRelease(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
        DispatcherPump.Drain();
    }

    /// <summary>
    /// Every distinct colour actually rendered inside the swatch, inset far enough to clear its
    /// rounded border and the antialiasing along it.
    /// </summary>
    private static List<string> SwatchColours(Window window, ColorPicker picker)
    {
        var swatch = Part<ToggleButton>(picker, "SwatchButton");
        var origin = At(swatch, 0, 0);

        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);

        var size = frame!.PixelSize;
        using var buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * size.Height];
        Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);

        // The headless Skia surface hands back RGBA rather than the BGRA that reading a Windows
        // bitmap usually means, so the channel order is taken from the buffer instead of assumed.
        var redFirst = buffer.Format == PixelFormat.Rgba8888;
        Assert.True(redFirst || buffer.Format == PixelFormat.Bgra8888, $"unhandled pixel format {buffer.Format}");

        const int Inset = 6;
        var colours = new HashSet<string>();

        for (var y = (int)origin.Y + Inset; y < origin.Y + swatch.Bounds.Height - Inset; y++)
        {
            for (var x = (int)origin.X + Inset; x < origin.X + swatch.Bounds.Width - Inset; x++)
            {
                var i = (y * buffer.RowBytes) + (x * 4);
                var (r, b) = redFirst ? (bytes[i], bytes[i + 2]) : (bytes[i + 2], bytes[i]);
                colours.Add($"{r:X2}{bytes[i + 1]:X2}{b:X2}");
            }
        }

        Assert.NotEmpty(colours);
        return [.. colours];
    }

    /// <summary>The colour a shape was actually painted in, or a failure if it has no solid brush.</summary>
    private static Color SolidFill(Shape shape) =>
        shape.Fill is ISolidColorBrush solid
            ? solid.Color
            : throw new InvalidOperationException($"{shape.Name} is filled with {shape.Fill?.GetType().Name ?? "nothing"}, not a solid colour.");

    /// <summary>
    /// A minimal stand-in for whatever the styling panel will bind to in Task 3: one colour,
    /// with change notification, and nothing else.
    /// </summary>
    private sealed class ColourHolder : INotifyPropertyChanged
    {
        private RgbColor _colour;

        public event PropertyChangedEventHandler? PropertyChanged;

        public RgbColor Colour
        {
            get => _colour;
            set
            {
                if (_colour == value)
                {
                    return;
                }

                _colour = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Colour)));
            }
        }
    }
}

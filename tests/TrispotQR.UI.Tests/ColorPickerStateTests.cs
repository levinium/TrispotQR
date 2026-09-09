using TrispotQR.Core.Primitives;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The picker keeps four inputs in step: the saturation/value square with its hue strip,
/// the palette, the hex box and the RGB sliders. Everything they share lives in
/// <see cref="ColorPickerState"/>, which is why these are plain <c>[Fact]</c>s: no window,
/// no dispatcher, no headless platform. The WPF originals had to build a real control on
/// an STA thread to ask the same questions.
///
/// They concentrate on the part that is easy to get wrong and invisible when it breaks.
/// Converting RGB to HSV is lossy at two edges, because every grey has an undefined hue
/// and black has an undefined hue and saturation. A picker that re-derives them from the
/// colour each time sends the crosshair to a corner and the hue strip to red the instant
/// the brightness reaches zero, and dragging back up returns red rather than the colour
/// the user started from.
/// </summary>
public class ColorPickerStateTests
{
    private static readonly RgbColor Red = RgbColor.FromRgb(255, 0, 0);
    private static readonly RgbColor Blue = RgbColor.FromRgb(0, 0, 255);
    private static readonly RgbColor Green = RgbColor.FromRgb(0, 128, 0);

    [Fact]
    public void SettingAColourAdoptsItsHueSaturationAndValue()
    {
        var state = new ColorPickerState { Color = RgbColor.FromRgb(0x00, 0x80, 0xFF) };

        // Two decimal places, as the WPF test used: 0x0080FF's hue is an irrational-looking
        // 209.8823..., so an exact comparison would be asserting the floating point noise
        // rather than the arithmetic.
        Assert.Equal(209.88, state.Hue, 2);
        Assert.Equal(1.0, state.Saturation, 2);
        Assert.Equal(1.0, state.Value, 2);
    }

    [Fact]
    public void DraggingBrightnessToZeroAndBackReturnsTheOriginalColour()
    {
        var state = new ColorPickerState { Color = Red };

        // Down to black, which cannot express a hue or a saturation of its own.
        state.SetSaturationValue(1.0, 0.0);
        var atBlack = state.Color;
        var hueAtBlack = state.Hue;

        // Back up to full brightness.
        state.SetSaturationValue(1.0, 1.0);

        Assert.Equal(RgbColor.Black, atBlack);
        Assert.Equal(0, hueAtBlack, 3);
        Assert.Equal(Red, state.Color);
    }

    [Fact]
    public void DraggingSaturationToZeroAndBackKeepsTheHue()
    {
        var state = new ColorPickerState();

        state.SetHue(240);
        state.SetSaturationValue(1.0, 1.0);
        var saturated = state.Color;

        // All the way to the left edge: a pure grey, which has no hue of its own.
        state.SetSaturationValue(0.0, 1.0);
        var grey = state.Color;

        state.SetSaturationValue(1.0, 1.0);

        Assert.Equal(Blue, saturated);
        Assert.Equal(RgbColor.White, grey);
        Assert.Equal(240, state.Hue, 3);
        Assert.Equal(Blue, state.Color);
    }

    [Fact]
    public void AnExternalGreyDoesNotDiscardTheChosenHue()
    {
        // A grey arriving from the palette, the hex box or a binding says nothing about
        // hue, so the strip should stay where the user left it.
        var state = new ColorPickerState();

        state.SetHue(120);
        state.Color = RgbColor.FromRgb(0x80, 0x80, 0x80);

        Assert.Equal(120, state.Hue, 3);
    }

    [Fact]
    public void AnExternalColourDoesOverrideTheHue()
    {
        // The opposite case: a colour that genuinely carries a hue must win.
        var state = new ColorPickerState();

        state.SetHue(120);
        state.Color = Red;

        Assert.Equal(0, state.Hue, 3);
    }

    [Fact]
    public void AnExternalBlackDoesNotDiscardTheChosenSaturation()
    {
        // Black defines neither hue nor saturation, so both survive it.
        var state = new ColorPickerState();

        state.SetHue(200);
        state.SetSaturationValue(0.4, 0.9);
        state.Color = RgbColor.Black;

        Assert.Equal(200, state.Hue, 3);
        Assert.Equal(0.4, state.Saturation, 3);
        Assert.Equal(0.0, state.Value, 3);
    }

    [Theory]
    [InlineData(0, 255, 0, 0)]
    [InlineData(60, 255, 255, 0)]
    [InlineData(120, 0, 255, 0)]
    [InlineData(180, 0, 255, 255)]
    [InlineData(240, 0, 0, 255)]
    [InlineData(300, 255, 0, 255)]
    public void MovingTheHueStripProducesTheExpectedColour(double hue, byte r, byte g, byte b)
    {
        // Fully saturated and fully bright, so the hue is the only variable and the expected
        // values are the primaries rather than something that needs its own arithmetic to check.
        var state = new ColorPickerState { Color = Red };
        state.SetSaturationValue(1, 1);

        state.SetHue(hue);

        Assert.Equal(RgbColor.FromArgb(255, r, g, b), state.Color);
    }

    [Fact]
    public void SquareCoordinatesOutsideTheControlAreClampedNotIgnored()
    {
        // A drag that overshoots the edge should pin to it rather than stop dead.
        var state = new ColorPickerState();

        state.SetHue(0);
        state.SetSaturationValue(5.0, 5.0);
        var high = state.Color;

        state.SetSaturationValue(-3.0, -3.0);

        Assert.Equal(Red, high);
        Assert.Equal(RgbColor.Black, state.Color);
    }

    [Fact]
    public void HueWrapsRatherThanClamping()
    {
        var state = new ColorPickerState();

        state.SetSaturationValue(1.0, 1.0);
        state.SetHue(0);
        var atZero = state.Color;

        state.SetHue(360);

        Assert.Equal(atZero, state.Color);
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(720, 0)]
    public void HueWrapsInBothDirections(double given, double equivalent)
    {
        var wrapped = new ColorPickerState();
        wrapped.SetSaturationValue(1, 1);
        wrapped.SetHue(given);

        var direct = new ColorPickerState();
        direct.SetSaturationValue(1, 1);
        direct.SetHue(equivalent);

        Assert.Equal(equivalent, wrapped.Hue, 3);
        Assert.Equal(direct.Color, wrapped.Color);
    }

    [Fact]
    public void TheColourFollowsTheSquareAndTheStripTogether()
    {
        // The whole point of the hub: every input reflects every other one.
        var state = new ColorPickerState();

        state.SetHue(240);
        state.SetSaturationValue(1.0, 1.0);

        Assert.Equal(Blue, state.Color);
    }

    [Theory]
    [InlineData("#1B3F94", 0x1B, 0x3F, 0x94)]
    [InlineData("1B3F94", 0x1B, 0x3F, 0x94)]     // the hash is optional
    [InlineData("  #1b3f94  ", 0x1B, 0x3F, 0x94)] // trimmed, and case does not matter
    [InlineData("#F0A", 0xFF, 0x00, 0xAA)]        // three digits expand by doubling each
    [InlineData("fff", 0xFF, 0xFF, 0xFF)]
    public void TrySetHexAcceptsWhatPeopleActuallyType(string text, byte r, byte g, byte b)
    {
        var state = new ColorPickerState();

        Assert.True(state.TrySetHex(text));
        Assert.Equal(RgbColor.FromArgb(255, r, g, b), state.Color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#")]
    [InlineData("12")]
    [InlineData("12345")]
    [InlineData("#1234567")]
    [InlineData("#FF1B3F94")]  // eight digit ARGB, which the WPF box never accepted either
    [InlineData("nonsense")]
    [InlineData("#GGGGGG")]
    [InlineData("rebeccapurple")]
    public void TrySetHexRejectsRubbishWithoutChangingTheColour(string text)
    {
        // Someone half way through typing is not an error, so this reports false and leaves
        // the colour alone rather than throwing or snapping to black.
        var state = new ColorPickerState { Color = Green };

        Assert.False(state.TrySetHex(text));
        Assert.Equal(Green, state.Color);
    }

    [Fact]
    public void TrySetHexTreatsNullAsRubbishRatherThanThrowing()
    {
        var state = new ColorPickerState { Color = Green };

        Assert.False(state.TrySetHex(null));
        Assert.Equal(Green, state.Color);
    }

    [Fact]
    public void AHexColourOverridesTheHueJustLikeAnyOtherExternalColour()
    {
        var state = new ColorPickerState();

        state.SetHue(120);
        Assert.True(state.TrySetHex("#0000FF"));

        Assert.Equal(240, state.Hue, 3);
    }

    [Fact]
    public void EditingKeepsTheAlphaOfTheColourItStartedFrom()
    {
        // Nothing in the picker sets alpha, so nothing in the picker may quietly change it.
        // The background colour can legitimately be part transparent.
        var state = new ColorPickerState { Color = RgbColor.FromArgb(0x40, 0xFF, 0x00, 0x00) };

        state.SetHue(240);
        state.SetSaturationValue(1, 1);
        Assert.Equal(RgbColor.FromArgb(0x40, 0x00, 0x00, 0xFF), state.Color);

        Assert.True(state.TrySetHex("#00FF00"));
        Assert.Equal(RgbColor.FromArgb(0x40, 0x00, 0xFF, 0x00), state.Color);
    }

    [Fact]
    public void ANewStateIsOpaqueBlack()
    {
        var state = new ColorPickerState();

        Assert.Equal(RgbColor.Black, state.Color);
        Assert.Equal(0, state.Hue, 3);
        Assert.Equal(0, state.Saturation, 3);
        Assert.Equal(0, state.Value, 3);
    }

    [Fact]
    public void TheHueBackdropIsThePureFormOfTheCurrentHue()
    {
        // What the square's bottom layer is painted with: the hue at full saturation and
        // full value, carrying the alpha the caller asks for rather than the colour's own.
        var state = new ColorPickerState();
        state.SetHue(120);

        var backdrop = new HsvColor(state.Hue, 1, 1).ToRgb(255);

        Assert.Equal(RgbColor.FromArgb(255, 0, 255, 0), backdrop);
    }

    [Fact]
    public void SmallDragsRoundTripThroughEightBitColourWithoutMovingTheMarker()
    {
        // The reason the state holds hue, saturation and value rather than deriving them:
        // several nearby drag positions land on the same 8 bit colour, and re-deriving would
        // snap the crosshair back to whatever that rounded colour happens to mean.
        var state = new ColorPickerState();
        state.SetHue(30);
        state.SetSaturationValue(0.5001, 0.5001);

        Assert.Equal(0.5001, state.Saturation, 4);
        Assert.Equal(0.5001, state.Value, 4);
        Assert.Equal(30, state.Hue, 4);
    }
}

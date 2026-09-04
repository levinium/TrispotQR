using System.Windows.Media;
using TrispotQR.Core.Styling;

namespace TrispotQR.Tests;

public class HsvColorTests
{
    [Theory]
    [InlineData(255, 0, 0, 0)]      // red
    [InlineData(255, 255, 0, 60)]   // yellow
    [InlineData(0, 255, 0, 120)]    // green
    [InlineData(0, 255, 255, 180)]  // cyan
    [InlineData(0, 0, 255, 240)]    // blue
    [InlineData(255, 0, 255, 300)]  // magenta
    public void FromRgb_PrimariesAndSecondaries_LandOnTheirHue(byte r, byte g, byte b, double hue)
    {
        var hsv = HsvColor.FromRgb(Color.FromRgb(r, g, b));

        Assert.Equal(hue, hsv.Hue, 3);
        Assert.Equal(1.0, hsv.Saturation, 3);
        Assert.Equal(1.0, hsv.Value, 3);
    }

    [Fact]
    public void FromRgb_White_IsFullValueAndNoSaturation()
    {
        var hsv = HsvColor.FromRgb(Colors.White);

        Assert.Equal(1.0, hsv.Value, 3);
        Assert.Equal(0.0, hsv.Saturation, 3);
    }

    [Fact]
    public void FromRgb_Black_IsAllZero()
    {
        var hsv = HsvColor.FromRgb(Colors.Black);

        Assert.Equal(0.0, hsv.Value, 3);
        Assert.Equal(0.0, hsv.Saturation, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(200)]
    [InlineData(255)]
    public void FromRgb_AnyGrey_HasNoSaturation(byte level)
    {
        var hsv = HsvColor.FromRgb(Color.FromRgb(level, level, level));

        Assert.Equal(0.0, hsv.Saturation, 3);
        Assert.Equal(level / 255.0, hsv.Value, 3);
    }

    /// <summary>
    /// The property the picker actually depends on: any colour survives a trip through HSV
    /// and back unchanged. Rounding to 8 bits is the only tolerance allowed, and here that
    /// means exact.
    /// </summary>
    [Fact]
    public void RoundTrip_EveryColourInABroadSweep_ComesBackIdentical()
    {
        var failures = new List<string>();

        for (var r = 0; r <= 255; r += 17)
        {
            for (var g = 0; g <= 255; g += 17)
            {
                for (var b = 0; b <= 255; b += 17)
                {
                    var original = Color.FromRgb((byte)r, (byte)g, (byte)b);
                    var back = HsvColor.FromRgb(original).ToRgb();

                    if (back != original)
                    {
                        failures.Add($"#{r:X2}{g:X2}{b:X2} -> #{back.R:X2}{back.G:X2}{back.B:X2}");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0,
            $"{failures.Count} colours did not survive the round trip:\n" + string.Join("\n", failures.Take(10)));
    }

    [Theory]
    [InlineData(-90, 270)]
    [InlineData(360, 0)]
    [InlineData(450, 90)]
    [InlineData(720, 0)]
    public void ToRgb_HueWrapsInBothDirections(double given, double equivalent)
    {
        Assert.Equal(HsvColor.ToRgb(equivalent, 1, 1), HsvColor.ToRgb(given, 1, 1));
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(1.5)]
    public void ToRgb_SaturationAndValueAreClamped(double outOfRange)
    {
        var clampedLow = HsvColor.ToRgb(200, outOfRange, 0.5);
        var clampedHigh = HsvColor.ToRgb(200, 0.5, outOfRange);

        // Clamping means these match the nearest legal input rather than producing nonsense.
        var expectedSat = HsvColor.ToRgb(200, Math.Clamp(outOfRange, 0, 1), 0.5);
        var expectedVal = HsvColor.ToRgb(200, 0.5, Math.Clamp(outOfRange, 0, 1));

        Assert.Equal(expectedSat, clampedLow);
        Assert.Equal(expectedVal, clampedHigh);
    }

    [Fact]
    public void ToRgb_ValueOfZero_IsBlackWhateverTheHue()
    {
        foreach (var hue in new double[] { 0, 90, 180, 270 })
        {
            Assert.Equal(Colors.Black, HsvColor.ToRgb(hue, 1, 0));
        }
    }

    [Fact]
    public void ToRgb_SaturationOfZero_IsGreyWhateverTheHue()
    {
        foreach (var hue in new double[] { 0, 90, 180, 270 })
        {
            var c = HsvColor.ToRgb(hue, 0, 0.5);
            Assert.Equal(c.R, c.G);
            Assert.Equal(c.G, c.B);
        }
    }
}

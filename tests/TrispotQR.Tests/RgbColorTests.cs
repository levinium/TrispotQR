using TrispotQR.Core.Primitives;

namespace TrispotQR.Tests;

public class RgbColorTests
{
    [Fact]
    public void FromRgb_IsFullyOpaque() => Assert.Equal(255, RgbColor.FromRgb(1, 2, 3).A);

    [Fact]
    public void ToHex_OmitsAlphaWhenOpaque() =>
        Assert.Equal("#1B2A4A", RgbColor.FromRgb(0x1B, 0x2A, 0x4A).ToHex());

    [Fact]
    public void ToHex_IncludesAlphaWhenNotOpaque() =>
        Assert.Equal("#801B2A4A", RgbColor.FromArgb(0x80, 0x1B, 0x2A, 0x4A).ToHex());

    [Theory]
    [InlineData("#1B2A4A", 255, 0x1B, 0x2A, 0x4A)]
    [InlineData("#801B2A4A", 0x80, 0x1B, 0x2A, 0x4A)]
    [InlineData("  #1b2a4a  ", 255, 0x1B, 0x2A, 0x4A)]
    public void TryParse_ReadsTheFormatsWeWrite(string text, int a, int r, int g, int b)
    {
        Assert.True(RgbColor.TryParse(text, out var colour));
        Assert.Equal(new RgbColor((byte)a, (byte)r, (byte)g, (byte)b), colour);
    }

    /// <summary>A hand-edited preset file may carry a WPF colour name.</summary>
    [Theory]
    [InlineData("Black", 0, 0, 0)]
    [InlineData("White", 255, 255, 255)]
    public void TryParse_StillReadsTheCommonNames(string text, int r, int g, int b)
    {
        Assert.True(RgbColor.TryParse(text, out var colour));
        Assert.Equal(RgbColor.FromRgb((byte)r, (byte)g, (byte)b), colour);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("nonsense")]
    [InlineData("#12345")]
    public void TryParse_RejectsTheRest(string? text) => Assert.False(RgbColor.TryParse(text, out _));

    [Fact]
    public void RoundTrip_SurvivesHex()
    {
        var original = RgbColor.FromArgb(0x7F, 0x10, 0x20, 0x30);
        Assert.True(RgbColor.TryParse(original.ToHex(), out var parsed));
        Assert.Equal(original, parsed);
    }

    [Fact]
    public void Transparent_KnowsItIs()
    {
        Assert.True(RgbColor.Transparent.IsTransparent);
        Assert.False(RgbColor.Black.IsTransparent);
    }
}

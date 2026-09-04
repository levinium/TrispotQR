using TrispotQR.Core.Qr;

namespace TrispotQR.Tests;

public class QrEncoderTests
{
    [Fact]
    public void Encode_SimpleText_ProducesSquareMatrixOfValidSize()
    {
        var result = QrEncoder.Encode("HELLO", EccLevel.Medium);

        Assert.True(result.Success);
        var matrix = result.Matrix!;

        // QR versions 1..40 are sized 21, 25, 29 ... 177 modules.
        Assert.InRange(matrix.Size, 21, 177);
        Assert.Equal(0, (matrix.Size - 21) % 4);
        Assert.InRange(matrix.Version, 1, 40);
    }

    [Fact]
    public void Encode_PlacesTheThreeFinderPatterns()
    {
        var matrix = QrEncoder.Encode("https://www.example.org", EccLevel.Medium).Matrix!;
        var last = matrix.Size - 7;

        foreach (var (ox, oy) in new[] { (0, 0), (last, 0), (0, last) })
        {
            // Outer ring dark, the ring inside it light, the 3x3 core dark.
            Assert.True(matrix.IsDark(ox + 0, oy + 0), $"outer corner at {ox},{oy}");
            Assert.True(matrix.IsDark(ox + 6, oy + 6), $"outer corner at {ox},{oy}");
            Assert.False(matrix.IsDark(ox + 1, oy + 1), $"inner ring at {ox},{oy}");
            Assert.False(matrix.IsDark(ox + 5, oy + 5), $"inner ring at {ox},{oy}");
            Assert.True(matrix.IsDark(ox + 3, oy + 3), $"core at {ox},{oy}");
        }
    }

    [Fact]
    public void IsDark_OutsideTheMatrix_ReturnsFalseRatherThanThrowing()
    {
        var matrix = QrEncoder.Encode("x", EccLevel.Medium).Matrix!;

        Assert.False(matrix.IsDark(-1, 0));
        Assert.False(matrix.IsDark(0, -1));
        Assert.False(matrix.IsDark(matrix.Size, 0));
        Assert.False(matrix.IsDark(0, matrix.Size));
    }

    [Fact]
    public void Encode_ShortText_BoostsErrorCorrectionForFree()
    {
        // "HI" fits in a version 1 symbol with room to spare, so the encoder should
        // spend the slack on stronger error correction rather than waste it.
        var result = QrEncoder.Encode("HI", EccLevel.Low);

        Assert.True(result.Success);
        Assert.True(result.Matrix!.EffectiveEcc > EccLevel.Low);
    }

    [Fact]
    public void Encode_TextTooLong_ReturnsFriendlyFailureInsteadOfThrowing()
    {
        var tooLong = new string('A', 5000);

        var result = QrEncoder.Encode(tooLong, EccLevel.High);

        Assert.False(result.Success);
        Assert.Null(result.Matrix);
        Assert.Contains("too long", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
        // The message must tell the user both ways out.
        Assert.Contains("error correction", result.ErrorMessage!, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Encode_EmptyContent_ReturnsFailureWithGuidance(string? text)
    {
        var result = QrEncoder.Encode(text!, EccLevel.Medium);

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));
    }

    [Fact]
    public void Encode_HonoursARequestedEccFloor()
    {
        // Long enough that there is no slack to boost with, so the level stays put.
        var text = new string('A', 1000);

        var result = QrEncoder.Encode(text, EccLevel.Quartile);

        Assert.True(result.Success);
        Assert.True(result.Matrix!.EffectiveEcc >= EccLevel.Quartile);
    }

    [Fact]
    public void Encode_UnicodeContent_Succeeds()
    {
        var result = QrEncoder.Encode("Northgate Studios — שלום", EccLevel.Medium);

        Assert.True(result.Success);
    }

    [Theory]
    [InlineData(EccLevel.Low, 2953)]
    [InlineData(EccLevel.Medium, 2331)]
    [InlineData(EccLevel.Quartile, 1663)]
    [InlineData(EccLevel.High, 1273)]
    public void ByteCapacity_ReportsTheVersion40Limit(EccLevel level, int expected)
    {
        Assert.Equal(expected, QrEncoder.ByteCapacity(level));
    }
}

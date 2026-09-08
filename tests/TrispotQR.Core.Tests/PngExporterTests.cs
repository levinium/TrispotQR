using System.IO;
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.Tests;

public class PngExporterTests : IDisposable
{
    private const string Payload = "https://example.org/tickets";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trispotqr-png-{Guid.NewGuid():N}");

    public PngExporterTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static RasterImage Render(RgbColor? background, int size = 256)
    {
        var encoded = QrEncoder.Encode(Payload, EccLevel.Medium);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, QrStyle.Default with { Background = background });
        return SkiaRasterizer.Render(drawing, size);
    }

    [Fact]
    public void Save_WritesAFileThatDecodesBack()
    {
        var path = Path.Combine(_directory, "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.True(File.Exists(path));
        Assert.Equal(Payload, QrDecoder.Decode(Render(RgbColor.White)));
    }

    [Fact]
    public void Save_CreatesMissingDirectories()
    {
        var path = Path.Combine(_directory, "nested", "deeper", "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Save_LeavesNoTemporaryFileBehind()
    {
        var path = Path.Combine(_directory, "code.png");
        PngExporter.Save(Render(RgbColor.White), path);

        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void ToBytes_IsAPng() =>
        Assert.Equal([0x89, 0x50, 0x4E, 0x47], PngExporter.ToBytes(Render(RgbColor.White)).Take(4));

    /// <summary>A code saved with no background must stay genuinely transparent.</summary>
    [Fact]
    public void ATransparentCode_KeepsItsAlphaChannel() => Assert.Equal(0, Render(background: null).Pixels[3]);

    [Fact]
    public void ATransparentCode_StillDecodesBecauseDecodingFlattensOntoWhite() =>
        Assert.Equal(Payload, QrDecoder.Decode(Render(background: null)));
}

using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Media;
using TrispotQR.Core.Rendering;

namespace TrispotQR.Core.Export;

/// <summary>
/// Writes a code as vector SVG, for signage and anything else that gets scaled up in
/// print. It serialises the very same geometry the PNG renderer paints, so the two
/// outputs cannot drift apart.
/// </summary>
public static class SvgExporter
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void Save(QrDrawing drawing, int pixelSize, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = path + ".tmp";
        File.WriteAllText(temporary, ToSvg(drawing, pixelSize), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    /// <summary>
    /// Serialises the drawing. The viewBox is in module units and only width and height
    /// are in pixels, so the file stays sharp at any size the printer asks for.
    /// </summary>
    public static string ToSvg(QrDrawing drawing, int pixelSize)
    {
        ArgumentNullException.ThrowIfNull(drawing);

        var units = drawing.SizeInUnits.ToString("0.####", Invariant);
        var builder = new StringBuilder();

        builder.AppendLine(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no""?>");
        builder.Append(Invariant, $@"<svg xmlns=""http://www.w3.org/2000/svg"" ");
        builder.Append(Invariant, $@"xmlns:xlink=""http://www.w3.org/1999/xlink"" ");
        builder.Append(Invariant, $@"width=""{pixelSize}"" height=""{pixelSize}"" ");
        builder.AppendLine(Invariant, $@"viewBox=""0 0 {units} {units}"" shape-rendering=""geometricPrecision"">");

        if (drawing.Background is { A: > 0 } background)
        {
            builder.AppendLine(Invariant,
                $@"  <rect x=""0"" y=""0"" width=""{units}"" height=""{units}"" fill=""{Hex(background)}""{Opacity(background)} />");
        }

        foreach (var layer in drawing.Layers)
        {
            AppendLayer(builder, layer);
        }

        AppendLogo(builder, drawing);

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static void AppendLayer(StringBuilder builder, QrLayer layer)
    {
        var (data, fillRule) = PathData(layer.Geometry);
        if (data.Length == 0)
        {
            return;
        }

        var fill = layer.Fill is SolidColorBrush brush ? brush.Color : Colors.Black;

        builder.Append(Invariant, $@"  <path id=""{layer.Name}"" d=""{data}""");
        builder.Append(Invariant, $@" fill=""{Hex(fill)}""{Opacity(fill)}");
        builder.Append(Invariant, $@" fill-rule=""{fillRule}""");

        if (layer.Stroke is { Brush: SolidColorBrush strokeBrush } pen)
        {
            builder.Append(Invariant, $@" stroke=""{Hex(strokeBrush.Color)}""");
            builder.Append(Invariant, $@" stroke-width=""{pen.Thickness.ToString("0.####", Invariant)}""");
            builder.Append(@" stroke-linejoin=""round""");
        }

        builder.AppendLine(" />");
    }

    /// <summary>
    /// Converts a WPF geometry to SVG path data.
    ///
    /// WPF's path mini-language is a superset of the SVG <c>d</c> syntax with one
    /// difference that matters: it prefixes the string with a fill rule token, F0 for
    /// even-odd or F1 for nonzero. Left in place, an SVG renderer treats the whole path
    /// as malformed. So the token is stripped here and re-expressed as the SVG
    /// <c>fill-rule</c> attribute, which is the only translation the format needs.
    /// </summary>
    private static (string Data, string FillRule) PathData(Geometry geometry)
    {
        var raw = geometry.ToString(Invariant).Trim();

        // The geometry's own fill rule is the source of truth; the prefix is only how WPF
        // chose to serialise it, and it may be absent when the rule is the default.
        var fillRule = geometry is PathGeometry { FillRule: FillRule.EvenOdd } ? "evenodd" : "nonzero";

        if (raw.StartsWith("F0", StringComparison.Ordinal) || raw.StartsWith("F1", StringComparison.Ordinal))
        {
            fillRule = raw[1] == '0' ? "evenodd" : "nonzero";
            raw = raw[2..].TrimStart();
        }

        return (raw, fillRule);
    }

    private static void AppendLogo(StringBuilder builder, QrDrawing drawing)
    {
        if (drawing.Logo is not { } logo || !File.Exists(logo.Path))
        {
            return;
        }

        // Embedded as a data URI so the .svg is a single self-contained file. A linked
        // image would break the moment the file is emailed or moved.
        var mime = Path.GetExtension(logo.Path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/png",
        };

        var base64 = Convert.ToBase64String(File.ReadAllBytes(logo.Path));

        builder.Append(Invariant, $@"  <image x=""{Num(logo.X)}"" y=""{Num(logo.Y)}"" ");
        builder.Append(Invariant, $@"width=""{Num(logo.Width)}"" height=""{Num(logo.Height)}"" ");
        builder.Append(@"preserveAspectRatio=""xMidYMid meet"" ");
        builder.AppendLine(Invariant, $@"xlink:href=""data:{mime};base64,{base64}"" />");
    }

    private static string Num(double value) => value.ToString("0.####", Invariant);

    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>SVG carries alpha separately, so a partly transparent colour needs the extra attribute.</summary>
    private static string Opacity(Color color) =>
        color.A == 255 ? string.Empty : $@" fill-opacity=""{(color.A / 255.0).ToString("0.###", Invariant)}""";
}

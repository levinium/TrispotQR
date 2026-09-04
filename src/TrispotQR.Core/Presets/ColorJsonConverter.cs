using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;

namespace TrispotQR.Core.Presets;

/// <summary>
/// Reads and writes <see cref="Color"/> as a hex string, so a preset file stays something
/// a person can open and edit rather than a wall of channel numbers. Alpha is written only
/// when it is not fully opaque.
/// </summary>
public sealed class ColorJsonConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException("Expected a colour such as \"#1B2A4A\".");
        }

        try
        {
            return (Color)ColorConverter.ConvertFromString(text)!;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or InvalidOperationException)
        {
            throw new JsonException($"\"{text}\" is not a colour this app understands.", ex);
        }
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options)
    {
        var hex = value.A == 255
            ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
            : $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";

        writer.WriteStringValue(hex);
    }
}

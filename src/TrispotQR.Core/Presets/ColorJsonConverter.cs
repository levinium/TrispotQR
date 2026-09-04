using System.Text.Json;
using System.Text.Json.Serialization;
using TrispotQR.Core.Primitives;

namespace TrispotQR.Core.Presets;

/// <summary>
/// Reads and writes <see cref="RgbColor"/> as a hex string, so a preset file stays
/// something a person can open and edit rather than a wall of channel numbers. Alpha is
/// written only when it is not fully opaque.
///
/// The format is unchanged from the version that stored a WPF colour, so preset files
/// written before the port still load.
/// </summary>
public sealed class ColorJsonConverter : JsonConverter<RgbColor>
{
    public override RgbColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();

        return RgbColor.TryParse(text, out var colour)
            ? colour
            : throw new JsonException($"\"{text}\" is not a colour this app understands.");
    }

    public override void Write(Utf8JsonWriter writer, RgbColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}

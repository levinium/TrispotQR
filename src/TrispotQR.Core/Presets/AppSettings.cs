using System.IO;
using System.Text.Json;
using TrispotQR.Core.Styling;

namespace TrispotQR.Core.Presets;

/// <summary>Which appearance the user asked for.</summary>
public enum AppTheme
{
    /// <summary>Match whatever Windows is set to.</summary>
    FollowWindows,

    Light,

    Dark,
}

/// <summary>
/// What the app remembers between runs. Two kinds of thing live here and they behave
/// differently: session state, which records where the user left off, and preferences,
/// which the user chose deliberately in the settings window.
/// </summary>
public sealed record AppSettings
{
    /// <summary>The style in effect when the app last closed. Session state.</summary>
    public QrStyle Style { get; init; } = QrStyle.Default;

    /// <summary>Index of the content type tab that was selected. Session state.</summary>
    public int ContentTypeIndex { get; init; }

    /// <summary>Folder the last export was saved into. Session state, and only consulted
    /// when <see cref="DefaultSaveDirectory"/> is not set.</summary>
    public string? LastSaveDirectory { get; init; }

    public double WindowWidth { get; init; } = 1180;

    public double WindowHeight { get; init; } = 800;

    public AppTheme Theme { get; init; } = AppTheme.FollowWindows;

    /// <summary>
    /// Whether an amber "may not scan" verdict interrupts an export. A red "did not scan"
    /// always warns and is deliberately not configurable: silencing it would defeat the
    /// point of checking at all.
    /// </summary>
    public bool WarnOnRiskyCodes { get; init; } = true;

    /// <summary>
    /// A fixed folder for the save dialog to open in. When set it overrides
    /// <see cref="LastSaveDirectory"/>; when null the app follows wherever you last saved.
    /// </summary>
    public string? DefaultSaveDirectory { get; init; }

    /// <summary>
    /// Export size every launch starts at. Applied whatever <see cref="RememberLastStyle"/>
    /// says, because size is a decision about one export rather than part of a look.
    /// </summary>
    public int DefaultPixelSize { get; init; } = 1024;

    /// <summary>
    /// Whether the last style is restored on reopen. Off means every launch starts from the
    /// Classic defaults. Does not affect <see cref="DefaultPixelSize"/>.
    /// </summary>
    public bool RememberLastStyle { get; init; } = true;

    /// <summary>
    /// Colors the user has actually chosen, most recent first, as hex.
    ///
    /// Session state rather than a preference: nobody sets this deliberately, it is a record of
    /// what they did. Stored as hex strings rather than <see cref="RgbColor"/> so a hand edited
    /// or half written settings.json degrades to "one fewer swatch" instead of a parse failure
    /// that costs the whole file, and so the list stays readable to a person who opens it.
    ///
    /// Capped where it is written rather than here, because a cap is a rule about recording and
    /// this record only has to carry what was recorded.
    /// </summary>
    public IReadOnlyList<string> RecentColors { get; init; } = [];

    public static AppSettings Default { get; } = new();
}

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Every failure path returns defaults rather
/// than throwing: remembered preferences are a convenience, and losing them must never
/// prevent the app from starting or from closing cleanly.
/// </summary>
public sealed class AppSettingsStore
{
    private const string FileName = "settings.json";

    private static readonly JsonSerializerOptions SerializerOptions = PresetStore.CreateOptions();

    private readonly string _path;

    public AppSettingsStore(string? directory = null) =>
        _path = Path.Combine(directory ?? PresetStore.DefaultDirectory, FileName);

    public AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            return AppSettings.Default;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), SerializerOptions);
            return settings is null ? AppSettings.Default : settings with { Style = settings.Style.Normalised() };
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing useful to tell the user on the way out of the app.
        }
    }
}

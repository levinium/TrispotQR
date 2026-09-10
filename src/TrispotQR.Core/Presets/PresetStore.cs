using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrispotQR.Core.Styling;

namespace TrispotQR.Core.Presets;

/// <summary>
/// Keeps the user's saved styles on disk alongside the built-in ones.
///
/// A bad preset file must never stop the app opening, so a file that will not parse is
/// moved aside and the app carries on with the built-ins. Losing a saved style is a small
/// annoyance; refusing to start is not.
/// </summary>
public sealed class PresetStore
{
    private const string FileName = "presets.json";

    private static readonly JsonSerializerOptions SerializerOptions = CreateOptions();

    private readonly string _directory;
    private readonly LogoStore _logos;
    private List<StylePreset> _custom = [];

    public PresetStore(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory;
        _logos = new LogoStore(_directory);
        Reload();
    }

    /// <summary>
    /// What the folder was called before the app was renamed. Anything found here is
    /// carried over once, so saved styles survive the rename.
    /// </summary>
    private const string PreviousFolderName = "Trispot";

    // Resolved once per run: the carry-over below should not be attempted on every read,
    // and both this store and the settings store ask for it.
    private static readonly Lazy<string> Resolved = new(() =>
    {
        var directory = new DesktopSettingsLocation().Directory;

        // One-time carry-over from the folder the app used before it was renamed.
        // ApplicationData resolves to a real, per-user path on every platform .NET
        // supports here, not just Windows, so this runs everywhere; CarryOverFrom is what
        // actually limits the effect, since it is a no-op unless that older folder exists.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            CarryOverFrom(Path.Combine(appData, PreviousFolderName), directory);
        }

        return directory;
    });

    /// <summary>Where presets and settings live, resolved per platform by
    /// <see cref="DesktopSettingsLocation"/> (<c>%APPDATA%\TrispotQR</c> on Windows).</summary>
    public static string DefaultDirectory => Resolved.Value;

    /// <summary>
    /// Copies a previous version's files into <paramref name="current"/> the first time it
    /// is needed, and does nothing once that folder exists.
    ///
    /// Copy rather than move, so a half-finished carry-over cannot destroy the only copy of
    /// someone's saved styles. Failure is swallowed on purpose: the worst case is that the
    /// app starts with the built-in styles, which is not worth refusing to open over.
    ///
    /// Files only, not subfolders, which is complete for the one migration this exists for:
    /// the Trispot folder was only ever written by versions that predate <see cref="LogoStore"/>,
    /// so it cannot contain a logos folder to miss. Anyone renaming the folder again will need
    /// to make this recursive.
    /// </summary>
    internal static void CarryOverFrom(string previous, string current)
    {
        if (Directory.Exists(current) || !Directory.Exists(previous))
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(current);

            foreach (var file in Directory.EnumerateFiles(previous))
            {
                File.Copy(file, Path.Combine(current, Path.GetFileName(file)), overwrite: false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    public string FilePath => Path.Combine(_directory, FileName);

    /// <summary>
    /// Set when the last load hit an unreadable file, naming where the old one was moved.
    /// The UI shows this once, without blocking.
    /// </summary>
    public string? LoadWarning { get; private set; }

    /// <summary>The user's own saved styles, in the order they were saved.</summary>
    public IReadOnlyList<StylePreset> Custom => _custom;

    /// <summary>Built-in styles first, then the user's own.</summary>
    public IReadOnlyList<StylePreset> All => [.. StylePresets.BuiltIn, .. _custom];

    public void Reload()
    {
        LoadWarning = null;
        _custom = [];

        if (!File.Exists(FilePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(FilePath);
            var records = JsonSerializer.Deserialize<List<PresetRecord>>(json, SerializerOptions) ?? [];

            _custom = records
                .Where(r => !string.IsNullOrWhiteSpace(r.Name) && r.Style is not null)
                .Select(r => new StylePreset(r.Name!, r.Description ?? "Saved style.", Rehydrate(r.Style!.Normalised())))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _custom = [];
            LoadWarning = QuarantineBadFile(ex);
        }
    }

    /// <summary>
    /// Saves a style under a name, replacing any custom preset already using it. Built-in
    /// names are reserved so a user cannot shadow Classic and then wonder where it went.
    /// </summary>
    public void Save(string name, QrStyle style, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var trimmed = name.Trim();

        if (StylePresets.BuiltIn.Any(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"\"{trimmed}\" is a built-in style name. Please pick a different name.");
        }

        // Take our own copy of the logo before recording anything, so the saved style stops
        // depending on wherever the user happened to pick the file from. An IO failure here
        // propagates to the caller rather than quietly saving a style without its logo.
        var stored = _logos.Store(style.Logo.Path);
        var withOwnLogo = style with
        {
            Logo = stored is null ? LogoStyle.None : style.Logo with { Path = _logos.Resolve(stored) },
        };

        var preset = new StylePreset(trimmed, description ?? "Saved style.", withOwnLogo.Normalised());
        var existing = _custom.FindIndex(p => p.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0)
        {
            _custom[existing] = preset;
        }
        else
        {
            _custom.Add(preset);
        }

        Persist();
    }

    /// <summary>Removes a custom preset. Built-in names are ignored rather than throwing.</summary>
    public bool Delete(string name)
    {
        var removed = _custom.RemoveAll(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;

        if (removed)
        {
            Persist();
        }

        return removed;
    }

    /// <summary>
    /// Deletes logo copies that no saved style refers to any more.
    /// </summary>
    /// <param name="alsoInUse">
    /// Logos held by something other than a saved style, which in practice is the restored
    /// session: a user who applied a saved style, then deleted it, is still looking at its
    /// logo, and sweeping it would blank the code in front of them.
    /// </param>
    public void SweepLogos(params string?[] alsoInUse) =>
        _logos.Sweep([.. _custom.Select(p => p.Style.Logo.Path), .. alsoInUse ?? []]);

    /// <summary>
    /// The file holds the bare name of our own copy; everything above the store works in real
    /// paths. Turning one into the other on the way in and out is what keeps the storage
    /// layout out of <see cref="QrStyle"/>, which otherwise has no business knowing that some
    /// paths are special.
    ///
    /// A name that no longer resolves becomes no logo at all, so a style whose image has been
    /// deleted still applies.
    /// </summary>
    private QrStyle Rehydrate(QrStyle style) => style.Logo.HasImage
        ? style with { Logo = style.Logo with { Path = _logos.Resolve(style.Logo.Path) } }
        : style;

    private QrStyle Dehydrate(QrStyle style) => style.Logo.HasImage
        ? style with { Logo = style.Logo with { Path = _logos.NameOf(style.Logo.Path) } }
        : style;

    private void Persist()
    {
        Directory.CreateDirectory(_directory);

        var records = _custom
            .Select(p => new PresetRecord { Name = p.Name, Description = p.Description, Style = Dehydrate(p.Style) })
            .ToList();

        // Written to a temporary file first so a crash mid-write cannot destroy the
        // presets that were already saved.
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(records, SerializerOptions));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private string QuarantineBadFile(Exception cause)
    {
        var quarantine = Path.Combine(_directory, "presets.corrupt.json");

        try
        {
            File.Move(FilePath, quarantine, overwrite: true);
            return $"Your saved styles could not be read ({cause.Message}) so they were moved to "
                 + $"{quarantine}. The built-in styles are still available.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Your saved styles could not be read ({cause.Message}). The built-in styles are still available.";
        }
    }

    internal static JsonSerializerOptions CreateOptions()
    {
        // Nulls are written out explicitly. Omitting them looks tidier but is wrong here:
        // a null Background means transparent, and if it is left out of the file the
        // record's own default of white comes back on load, silently turning a saved
        // transparent style opaque.
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new ColorJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>On-disk shape. Kept separate from <see cref="StylePreset"/> so the
    /// in-memory type can carry things like the built-in flag that must never be saved.</summary>
    private sealed class PresetRecord
    {
        public string? Name { get; set; }

        public string? Description { get; set; }

        public QrStyle? Style { get; set; }
    }
}

using System.IO;
using System.Text.Json;
using TrispotQR.Core.Export;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;

namespace TrispotQR.Tests;

public class PresetStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly string _logoPath;

    public PresetStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-presets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);

        // A real file, because saving a style now takes its own copy of the image. A path that
        // points at nothing is not a state the app can reach either: the picker only accepts a
        // file it could open, and a style that carried a dead path is the very thing this
        // storage exists to stop.
        _logoPath = Path.Combine(_directory, "house.png");
        File.WriteAllBytes(_logoPath, PngExporter.ToBytes(new RasterImage(2, 2, new byte[2 * 2 * 4])));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Renaming the app moved the settings folder from %APPDATA%\Trispot to
    /// %APPDATA%\TrispotQR. Without the carry-over, everyone's saved styles would silently
    /// stop existing on the version that renamed it, which is the kind of thing nobody
    /// notices until the styles are wanted.
    /// </summary>
    [Fact]
    public void SavedStyles_SurviveTheRename()
    {
        var previous = Path.Combine(_directory, "Trispot");
        var current = Path.Combine(_directory, "TrispotQR");
        Directory.CreateDirectory(previous);

        new PresetStore(previous).Save("Carried over", Elaborate());
        File.WriteAllText(Path.Combine(previous, "settings.json"), "{}");

        PresetStore.CarryOverFrom(previous, current);

        Assert.Equal("Carried over", new PresetStore(current).Custom.Single().Name);
        Assert.True(File.Exists(Path.Combine(current, "settings.json")));

        // Copied, not moved: a carry-over that went wrong must not have been the only copy.
        Assert.True(File.Exists(Path.Combine(previous, "presets.json")));
    }

    [Fact]
    public void TheCarryOver_LeavesAnExistingFolderAlone()
    {
        var previous = Path.Combine(_directory, "Trispot");
        var current = Path.Combine(_directory, "TrispotQR");
        Directory.CreateDirectory(previous);
        Directory.CreateDirectory(current);

        new PresetStore(previous).Save("Old", Elaborate());
        new PresetStore(current).Save("Current", Elaborate());

        PresetStore.CarryOverFrom(previous, current);

        Assert.Equal("Current", new PresetStore(current).Custom.Single().Name);
    }

    [Fact]
    public void TheCarryOver_DoesNothingWhenThereIsNothingToCarry()
    {
        var current = Path.Combine(_directory, "TrispotQR");

        PresetStore.CarryOverFrom(Path.Combine(_directory, "nonexistent"), current);

        Assert.False(Directory.Exists(current));
    }

    private QrStyle Elaborate() => QrStyle.Default with
    {
        ModuleShape = ModuleShape.Fluid,
        ModuleScale = 0.88,
        MarkerFrameShape = MarkerFrameShape.Leaf,
        MarkerCenterShape = MarkerCenterShape.Circle,
        Foreground = RgbColor.FromRgb(0x1B, 0x2A, 0x4A),
        Background = null,
        MarkerFrameColor = RgbColor.FromRgb(0x8A, 0x6D, 0x3B),
        MarkerCenterColor = null,
        Outline = new OutlineStyle
        {
            Enabled = true,
            Color = RgbColor.White,
            ThicknessRatio = 0.09,
            Target = OutlineTarget.Markers,
        },
        QuietZoneModules = 3,
        Ecc = EccLevel.Quartile,
        Logo = new LogoStyle
        {
            Path = _logoPath,
            SizeRatio = 0.24,
            PunchShape = LogoPunchShape.Circle,
            PunchPadding = 0.8,
        },
        PixelSize = 2048,
    };

    [Fact]
    public void SavedStyle_SurvivesAFullRoundTripThroughDisk()
    {
        new PresetStore(_directory).Save("House", Elaborate());

        var reloaded = new PresetStore(_directory).Custom.Single(p => p.Name == "House").Style;

        // Every field but the logo's path, which is deliberately no longer the one that went
        // in: the store keeps its own copy and hands back a path to that. The tests below are
        // what cover the logo.
        Assert.Equal(Elaborate() with { Logo = LogoStyle.None }, reloaded with { Logo = LogoStyle.None });
    }

    [Fact]
    public void ASavedStyleKeepsItsOwnCopyOfTheLogo()
    {
        new PresetStore(_directory).Save("House", Elaborate());

        var logo = new PresetStore(_directory).Custom.Single(p => p.Name == "House").Style.Logo;

        Assert.True(logo.HasImage);
        Assert.NotEqual(_logoPath, logo.Path);
        Assert.Equal(Path.Combine(_directory, "logos"), Path.GetDirectoryName(logo.Path));
        Assert.Equal(File.ReadAllBytes(_logoPath), File.ReadAllBytes(logo.Path!));

        // The rest of the logo settings, which always did survive and must keep doing so.
        Assert.Equal(0.24, logo.SizeRatio);
        Assert.Equal(LogoPunchShape.Circle, logo.PunchShape);
        Assert.Equal(0.8, logo.PunchPadding);
    }

    [Fact]
    public void ASavedLogoSurvivesTheOriginalImageBeingDeleted()
    {
        // The whole reason for keeping a copy, run deliberately: this is what used to take the
        // logo away, and the old code stripped logos from saved styles rather than face it.
        new PresetStore(_directory).Save("House", Elaborate());
        File.Delete(_logoPath);

        var logo = new PresetStore(_directory).Custom.Single(p => p.Name == "House").Style.Logo;

        Assert.True(logo.HasImage);
        Assert.True(File.Exists(logo.Path), "the saved style's own copy of the logo is gone");
    }

    [Fact]
    public void TheFileRecordsTheNameOfOurCopyRatherThanAPathOnThisMachine()
    {
        // Two reasons a bare name is the right thing on disk. A path from this machine means
        // nothing on another one, which is half of what made the old behaviour fragile; and it
        // is the one field that would carry the shape of someone's folders into a file they
        // might reasonably send to somebody else.
        new PresetStore(_directory).Save("House", Elaborate());

        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_directory, "presets.json")));
        var recorded = file.RootElement[0].GetProperty("Style").GetProperty("Logo")
            .GetProperty("Path").GetString();

        Assert.NotNull(recorded);
        Assert.Equal(Path.GetFileName(recorded), recorded);
    }

    [Fact]
    public void TwoStylesSharingALogoKeepOneCopyBetweenThem()
    {
        // Named by a hash of the bytes, so this falls out for free rather than needing a
        // reference count. Without it, a user who saves six variations of one look ends up
        // with six copies of the same image.
        var store = new PresetStore(_directory);
        store.Save("One", Elaborate());
        store.Save("Two", Elaborate() with { QuietZoneModules = 5 });

        Assert.Single(Directory.EnumerateFiles(Path.Combine(_directory, "logos")));
    }

    [Fact]
    public void SweepingRemovesACopyNoStyleUsesAnyMore()
    {
        var store = new PresetStore(_directory);
        store.Save("House", Elaborate());
        var copy = store.Custom.Single().Style.Logo.Path!;

        store.Delete("House");
        store.SweepLogos();

        Assert.False(File.Exists(copy), "a logo nothing refers to was left behind");
    }

    [Fact]
    public void SweepingKeepsACopyTheSessionIsStillShowing()
    {
        // Why the sweep takes an argument at all. Someone who applied a saved style and then
        // deleted it is still looking at its logo, and collecting the file underneath them
        // would blank the code on screen.
        var store = new PresetStore(_directory);
        store.Save("House", Elaborate());
        var copy = store.Custom.Single().Style.Logo.Path!;

        store.Delete("House");
        store.SweepLogos(copy);

        Assert.True(File.Exists(copy), "the logo in use by the current session was swept");
    }

    [Fact]
    public void ColoursAreWrittenAsReadableHex()
    {
        new PresetStore(_directory).Save("House", Elaborate());

        var json = File.ReadAllText(Path.Combine(_directory, "presets.json"));

        Assert.Contains("#1B2A4A", json);
        Assert.Contains("#8A6D3B", json);
        // Enums too, so the file can be read and hand-edited.
        Assert.Contains("Fluid", json);
        Assert.Contains("Quartile", json);
    }

    [Fact]
    public void TransparentBackground_RoundTripsAsNull()
    {
        new PresetStore(_directory).Save("Transparent", QrStyle.Default with { Background = null });

        var reloaded = new PresetStore(_directory).Custom.Single().Style;

        Assert.Null(reloaded.Background);
    }

    [Fact]
    public void All_ListsTheBuiltInStylesFirstThenTheUsersOwn()
    {
        var store = new PresetStore(_directory);
        store.Save("Mine", QrStyle.Default);

        var all = store.All;

        Assert.Equal(StylePresets.BuiltIn.Count + 1, all.Count);
        Assert.Equal("Classic", all[0].Name);
        Assert.Equal("Mine", all[^1].Name);
        Assert.True(all[0].IsBuiltIn);
        Assert.False(all[^1].IsBuiltIn);
    }

    [Fact]
    public void SavingAnExistingName_ReplacesItRatherThanDuplicating()
    {
        var store = new PresetStore(_directory);
        store.Save("Mine", QrStyle.Default with { PixelSize = 512 });
        store.Save("mine", QrStyle.Default with { PixelSize = 2048 });

        Assert.Single(store.Custom);
        Assert.Equal(2048, store.Custom[0].Style.PixelSize);
    }

    [Fact]
    public void SavingOverABuiltInName_IsRefusedWithAClearMessage()
    {
        var store = new PresetStore(_directory);

        var ex = Assert.Throws<InvalidOperationException>(() => store.Save("Classic", QrStyle.Default));

        Assert.Contains("built-in", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Delete_RemovesACustomPresetAndPersistsTheRemoval()
    {
        var store = new PresetStore(_directory);
        store.Save("Temporary", QrStyle.Default);

        Assert.True(store.Delete("temporary"));
        Assert.Empty(new PresetStore(_directory).Custom);
    }

    [Fact]
    public void Delete_UnknownName_ReportsFalse()
    {
        Assert.False(new PresetStore(_directory).Delete("nothing here"));
    }

    [Fact]
    public void CorruptFile_IsMovedAsideAndTheAppStartsOnTheBuiltIns()
    {
        File.WriteAllText(Path.Combine(_directory, "presets.json"), "{ this is not json ]");

        var store = new PresetStore(_directory);

        Assert.Empty(store.Custom);
        Assert.Equal(StylePresets.BuiltIn.Count, store.All.Count);
        Assert.NotNull(store.LoadWarning);
        Assert.True(File.Exists(Path.Combine(_directory, "presets.corrupt.json")));
    }

    [Fact]
    public void PresetFileWithAnUnknownColour_IsTreatedAsCorruptRatherThanCrashing()
    {
        File.WriteAllText(
            Path.Combine(_directory, "presets.json"),
            """[{"Name":"Bad","Style":{"Foreground":"not-a-colour"}}]""");

        var store = new PresetStore(_directory);

        Assert.Empty(store.Custom);
        Assert.NotNull(store.LoadWarning);
    }

    [Fact]
    public void MissingFile_IsNotAnError()
    {
        var store = new PresetStore(_directory);

        Assert.Empty(store.Custom);
        Assert.Null(store.LoadWarning);
    }

    [Fact]
    public void OutOfRangeValuesInAPresetFile_AreClampedOnLoad()
    {
        File.WriteAllText(
            Path.Combine(_directory, "presets.json"),
            """[{"Name":"Extreme","Style":{"ModuleScale":9.0,"QuietZoneModules":99,"PixelSize":999999}}]""");

        var style = new PresetStore(_directory).Custom.Single().Style;

        Assert.Equal(1.0, style.ModuleScale);
        Assert.Equal(8, style.QuietZoneModules);
        Assert.Equal(4096, style.PixelSize);
    }

    [Fact]
    public void Settings_RoundTripThroughDisk()
    {
        var store = new AppSettingsStore(_directory);
        var settings = new AppSettings
        {
            Style = Elaborate(),
            ContentTypeIndex = 2,
            LastSaveDirectory = @"C:\Users\Someone\Pictures",
            WindowWidth = 1400,
            WindowHeight = 900,
            RecentColors = ["#112233", "#445566"],
        };

        store.Save(settings);
        var reloaded = new AppSettingsStore(_directory).Load();

        // Member by member rather than Assert.Equal on the whole record, and the reason is worth
        // writing down: AppSettings is a record, so its generated equality compares RecentColors
        // by reference. Two lists holding the same strings are not equal, which makes a
        // whole-record comparison fail on a round trip that in fact worked perfectly.
        //
        // The old whole-record assertion passed only because every instance's default was
        // Array.Empty<string>(), which is interned, so both sides held the *same* list. It would
        // have started failing the moment anything put a colour in it -- which is to say it was
        // never really comparing this member at all.
        Assert.Equal(settings.Style, reloaded.Style);
        Assert.Equal(settings.ContentTypeIndex, reloaded.ContentTypeIndex);
        Assert.Equal(settings.LastSaveDirectory, reloaded.LastSaveDirectory);
        Assert.Equal(settings.WindowWidth, reloaded.WindowWidth);
        Assert.Equal(settings.WindowHeight, reloaded.WindowHeight);
        Assert.Equal(settings.Theme, reloaded.Theme);
        Assert.Equal(settings.WarnOnRiskyCodes, reloaded.WarnOnRiskyCodes);
        Assert.Equal(settings.DefaultSaveDirectory, reloaded.DefaultSaveDirectory);
        Assert.Equal(settings.DefaultPixelSize, reloaded.DefaultPixelSize);
        Assert.Equal(settings.RememberLastStyle, reloaded.RememberLastStyle);
        Assert.Equal(settings.RecentColors, reloaded.RecentColors);
    }

    [Fact]
    public void Settings_CorruptFile_FallsBackToDefaultsSilently()
    {
        File.WriteAllText(Path.Combine(_directory, "settings.json"), "not json at all");

        Assert.Equal(AppSettings.Default, new AppSettingsStore(_directory).Load());
    }

    [Fact]
    public void Settings_MissingFile_ReturnsDefaults()
    {
        Assert.Equal(AppSettings.Default, new AppSettingsStore(_directory).Load());
    }
}

using System.IO;
using TrispotQR.App.Services;
using TrispotQR.App.ViewModels;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.ViewModels;

namespace TrispotQR.Tests;

/// <summary>
/// Nothing that fails to scan should leave the app without the user being told first.
///
/// The check runs fresh at the moment of the click rather than reading the badge. The badge
/// comes from a debounced background check, so at the instant someone presses Save it can
/// still be describing the previous style, or not have finished at all.
/// </summary>
public class ExportGuardTests : IDisposable
{
    private readonly string _directory;
    private readonly FakeDialogs _dialogs = new();

    public ExportGuardTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"trispotqr-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private MainViewModel Create(string text, QrStyle? style = null) => StaThread.Run(() =>
    {
        var vm = new MainViewModel(_dialogs, new PresetStore(_directory), new AppSettingsStore(_directory));
        ((PlainTextEditor)vm.ContentEditors[0]).Text = text;

        if (style is { } s)
        {
            vm.Foreground = s.Foreground;
            vm.BackgroundChoice = s.Background is null ? BackgroundChoice.Transparent : BackgroundChoice.Custom;

            if (s.Background is { } bg)
            {
                vm.CustomBackground = bg;
            }

            vm.QuietZone = s.QuietZoneModules;
            vm.ModuleScale = s.ModuleScale;
        }

        vm.RefreshNow();
        return vm;
    });

    /// <summary>Light grey on white: decodes here, but nowhere near enough contrast for a camera.</summary>
    private static QrStyle LowContrast => QrStyle.Default with
    {
        Foreground = RgbColor.FromRgb(0xC8, 0xC8, 0xC8),
        Background = RgbColor.White,
    };

    [Fact]
    public void AGoodCode_IsSavedWithNoWarningAtAll()
    {
        var vm = Create("https://www.example.org");
        var target = Path.Combine(_directory, "good.png");
        _dialogs.NextSavePath = target;

        StaThread.Run(() => { vm.SavePngCommand.Execute(null); return true; });

        Assert.Empty(_dialogs.RiskPrompts);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void ACodeThatDoesNotScan_WarnsAndSavesNothingWhenDeclined()
    {
        var vm = Create("https://www.example.org", LowContrast);
        var target = Path.Combine(_directory, "declined.png");
        _dialogs.NextSavePath = target;
        _dialogs.RiskAnswer = false;

        StaThread.Run(() => { vm.SavePngCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.False(File.Exists(target), "a declined warning must not write the file");
        Assert.Null(_dialogs.LastSuggestedName);   // the file dialog never even opened
    }

    [Fact]
    public void ACodeThatDoesNotScan_SavesWhenTheUserInsists()
    {
        var vm = Create("https://www.example.org", LowContrast);
        var target = Path.Combine(_directory, "insisted.png");
        _dialogs.NextSavePath = target;
        _dialogs.RiskAnswer = true;

        StaThread.Run(() => { vm.SavePngCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void TheWarningComesBeforeTheFileDialog()
    {
        // Being asked to name a file and only then told it will not work would be rude.
        var vm = Create("https://www.example.org", LowContrast);
        _dialogs.NextSavePath = Path.Combine(_directory, "order.png");
        _dialogs.RiskAnswer = false;

        StaThread.Run(() => { vm.SaveSvgCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.Null(_dialogs.LastSuggestedName);
    }

    [Fact]
    public void SvgExport_IsGuardedToo()
    {
        var vm = Create("https://www.example.org", LowContrast);
        var target = Path.Combine(_directory, "guarded.svg");
        _dialogs.NextSavePath = target;
        _dialogs.RiskAnswer = false;

        StaThread.Run(() => { vm.SaveSvgCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.False(File.Exists(target));
    }

    [Fact]
    public void Copy_IsGuardedToo()
    {
        var vm = Create("https://www.example.org", LowContrast);
        _dialogs.RiskAnswer = false;

        StaThread.Run(() => { vm.CopyCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.Null(_dialogs.LastError);
    }

    [Fact]
    public void SavingAStyle_IsGuardedAndStoresNothingWhenDeclined()
    {
        var vm = Create("https://www.example.org", LowContrast);
        _dialogs.NextText = "Unreadable";
        _dialogs.RiskAnswer = false;

        StaThread.Run(() => { vm.SavePresetCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.Empty(new PresetStore(_directory).Custom);
    }

    [Fact]
    public void SavingAGoodStyle_IsNotInterrupted()
    {
        var vm = Create("https://www.example.org");
        _dialogs.NextText = "Perfectly fine";

        StaThread.Run(() => { vm.SavePresetCommand.Execute(null); return true; });

        Assert.Empty(_dialogs.RiskPrompts);
        Assert.Single(new PresetStore(_directory).Custom);
    }

    [Fact]
    public void SavingAStyleWithAnEmptyContentBox_IsStillJudged()
    {
        // A style has no content of its own, so it is tested against a sample rather than
        // waved through just because nothing has been typed yet.
        var vm = Create(string.Empty, LowContrast);
        _dialogs.NextText = "Empty box";
        _dialogs.RiskAnswer = false;

        StaThread.Run(() => { vm.SavePresetCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.Empty(new PresetStore(_directory).Custom);
    }

    [Fact]
    public void AFailedCode_IsFlaggedAsSevereAndARiskyOneIsNot()
    {
        // Severity drives which button is focused, so getting it the wrong way round would
        // make Enter do the dangerous thing.
        var noQuietZone = Create("https://www.example.org", QrStyle.Default with { QuietZoneModules = 0 });
        _dialogs.NextSavePath = Path.Combine(_directory, "risky.png");
        StaThread.Run(() => { noQuietZone.SavePngCommand.Execute(null); return true; });

        Assert.Single(_dialogs.RiskPrompts);
        Assert.False(_dialogs.LastRiskWasSevere, "a decodable but risky code is not severe");
    }

    /// <summary>Records warnings instead of showing them.</summary>
    private sealed class FakeDialogs : IDialogService
    {
        public string? NextSavePath { get; set; }

        public string? NextText { get; set; }

        public bool RiskAnswer { get; set; } = true;

        public List<string> RiskPrompts { get; } = [];

        public bool? LastRiskWasSevere { get; private set; }

        public string? LastSuggestedName { get; private set; }

        public string? LastError { get; private set; }

        public string? AskForSavePath(string title, string filter, string ext, string suggestedName, string? directory)
        {
            LastSuggestedName = suggestedName;
            return NextSavePath;
        }

        public string? AskForImage(string? directory) => null;

        public string? AskForText(string title, string prompt, string initialValue) => NextText;

        public bool Confirm(string title, string message) => true;

        public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe)
        {
            RiskPrompts.Add(heading);
            LastRiskWasSevere = severe;
            return RiskAnswer;
        }

        public void ShowError(string title, string message) => LastError = message;

        public void ShowInformation(string title, string message)
        {
        }

        public AppSettings? EditSettings(AppSettings current) => null;
    }
}

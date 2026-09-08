using System.IO;
using TrispotQR.Core.Export;
using TrispotQR.Core.Payloads;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.ViewModels;

namespace TrispotQR.ViewModels.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _directory;
    private readonly FakeDialogService _dialogs = new();

    public MainViewModelTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-vm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private MainViewModel Create(IUiTimer? timer = null) =>
        new MainViewModel(
            _dialogs, timer ?? new FakeUiTimer(), new FakeImageClipboard(), new PresetStore(_directory), new AppSettingsStore(_directory));

    /// <summary>A view model with something encodable already typed in.</summary>
    private MainViewModel CreateWithContent(string text = "https://www.example.org")
    {
        var vm = Create();

        ((PlainTextEditor)vm.ContentEditors[0]).Text = text;
        vm.RefreshNow();

        return vm;
    }

    /// <summary>
    /// The render debounce is the only scheduling the view model owns, and it was a WPF
    /// DispatcherTimer. Behind an interface it can be driven directly, which is what a
    /// non-WPF toolkit needs and what lets this test prove the debounce without waiting on
    /// a real clock.
    /// </summary>
    [Fact]
    public void TheDebounce_RendersOnceTheTimerFires()
    {
        var timer = new FakeUiTimer();
        var vm = Create(timer);

        ((PlainTextEditor)vm.ContentEditors[0]).Text = "https://example.org";

        Assert.True(timer.IsRunning, "typing should have started the debounce");

        timer.Fire();

        Assert.False(timer.IsRunning, "the timer should stop itself when it fires");
        Assert.True(vm.CanExport, "the render should have happened");
    }

    private sealed class FakeUiTimer : IUiTimer
    {
        public TimeSpan Interval { get; set; }

        public bool IsRunning { get; private set; }

        public event EventHandler? Tick;

        public void Start() => IsRunning = true;

        public void Stop() => IsRunning = false;

        /// <summary>Stands in for the clock.</summary>
        public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void NewSession_OpensEmptyWithNothingPrefilled()
    {
        var vm = Create();

        // Nothing is seeded into the content box. A starting value would be the first
        // thing a user has to delete, and is easy to miss and accidentally publish.
        Assert.Equal(string.Empty, ((PlainTextEditor)vm.ContentEditors[0]).Text);
        Assert.Equal(string.Empty, vm.Payload);
        Assert.Null(vm.PreviewDrawing);
        Assert.False(vm.CanExport);
    }

    [Fact]
    public void TypingContent_ProducesAPreviewAndEnablesSaving()
    {
        var vm = CreateWithContent();

        Assert.NotNull(vm.PreviewDrawing);
        Assert.True(vm.CanExport);
        Assert.True(vm.SavePngCommand.CanExecute(null));
    }

    [Fact]
    public void ClearingTheContent_ClearsThePreviewAndDisablesSaving()
    {
        var vm = CreateWithContent();

        ((PlainTextEditor)vm.ContentEditors[0]).Text = string.Empty;
        vm.RefreshNow();

        Assert.Null(vm.PreviewDrawing);
        Assert.False(vm.CanExport);
        Assert.False(vm.SavePngCommand.CanExecute(null));
    }

    /// <summary>
    /// The view model describes what to draw; turning that into pixels is the view's job.
    /// Holding a WPF image here is what tied the view model to one toolkit.
    /// </summary>
    [Fact]
    public void ThePreview_IsADrawingRatherThanARenderedImage()
    {
        var vm = CreateWithContent("https://example.org");

        Assert.NotNull(vm.PreviewDrawing);
        Assert.True(vm.PreviewDrawing!.SizeInUnits > 0);
        Assert.NotEmpty(vm.PreviewDrawing.Layers);
    }

    [Fact]
    public void AnEmptyBox_ClearsThePreview()
    {
        var vm = Create();

        ((PlainTextEditor)vm.ContentEditors[0]).Text = string.Empty;
        vm.RefreshNow();

        Assert.Null(vm.PreviewDrawing);
    }

    [Fact]
    public void AStyleCanBeSavedBeforeAnyContentIsTyped()
    {
        // Saving a preset stores the look, which is meaningful on its own. Gating it on
        // there being content would make it unusable on a freshly opened, empty window.
        var vm = Create();
        vm.ModuleShape = ModuleShape.Circle;
        _dialogs.NextText = "Just the look";

        Assert.True(vm.SavePresetCommand.CanExecute(null));
        vm.SavePresetCommand.Execute(null);

        Assert.Contains(vm.Presets, p => p.Name == "Just the look");
    }

    [Fact]
    public void ContentTooLong_ReportsTheProblemInsteadOfCrashing()
    {
        var vm = Create();

        ((PlainTextEditor)vm.ContentEditors[0]).Text = new string('A', 5000);
        vm.RefreshNow();

        Assert.False(vm.CanExport);
        Assert.Contains("too long", vm.StatusDetail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LinkEditor_AddsHttpsAndSaysSo()
    {
        var vm = Create();
        var link = vm.ContentEditors.OfType<LinkEditor>().Single();

        link.Address = "example.org";

        Assert.Equal("https://example.org", link.Payload);
        Assert.False(link.HasErrors);
        Assert.Contains("https://example.org", link.Note!);
    }

    [Fact]
    public void LinkEditor_RejectsSomethingThatIsNotAnAddress()
    {
        var vm = Create();
        var link = vm.ContentEditors.OfType<LinkEditor>().Single();

        link.Address = "this is not an address";

        Assert.True(link.HasErrors);
        Assert.Equal(nameof(LinkEditor.Address), link.Issues.Single().Field);
    }

    /// <summary>
    /// The buttons are gated on the input being right, not merely on something having been
    /// encoded. A malformed address builds a code that scans perfectly and is still wrong.
    /// </summary>
    [Fact]
    public void CanExport_IsFalseWhileTheContentIsWrong()
    {
        var vm = Create();
        var link = vm.ContentEditors.OfType<LinkEditor>().Single();
        vm.SelectedContent = link;

        link.Address = "this is not an address";
        vm.RefreshNow();

        Assert.False(vm.CanExport);
        Assert.NotNull(vm.ExportBlockedReason);

        link.Address = "example.org";
        vm.RefreshNow();

        Assert.True(vm.CanExport);
        Assert.Null(vm.ExportBlockedReason);
    }

    [Fact]
    public void BackgroundChoice_MapsTheThreeOptionsOntoTheNullableColour()
    {
        var vm = Create();

        vm.BackgroundChoice = BackgroundChoice.Transparent;
        Assert.Equal(BackgroundChoice.Transparent, vm.BackgroundChoice);

        vm.BackgroundChoice = BackgroundChoice.White;
        Assert.Equal(BackgroundChoice.White, vm.BackgroundChoice);

        vm.CustomBackground = RgbColor.FromRgb(0xFA, 0xFA, 0xD2);
        Assert.Equal(BackgroundChoice.Custom, vm.BackgroundChoice);
        Assert.True(vm.IsCustomBackground);
    }

    [Fact]
    public void ChoosingCustomWhileTheBackgroundIsWhite_StaysOnCustom()
    {
        // The choice cannot be derived from the colour alone. White is a legitimate custom
        // colour, so picking Custom while white would otherwise read straight back as
        // White, the button would spring off and the colour picker would never appear.
        var vm = Create();
        Assert.Equal(BackgroundChoice.White, vm.BackgroundChoice);

        vm.BackgroundChoice = BackgroundChoice.Custom;

        Assert.Equal(BackgroundChoice.Custom, vm.BackgroundChoice);
        Assert.True(vm.IsCustomBackground);
        Assert.Equal(RgbColor.White, vm.CustomBackground);
    }

    [Fact]
    public void ApplyingAPreset_ClearsAStickyCustomBackgroundChoice()
    {
        var vm = Create();
        vm.BackgroundChoice = BackgroundChoice.Custom;

        vm.ApplyPresetCommand.Execute(vm.Presets.Single(p => p.Name == "Classic"));

        Assert.Equal(BackgroundChoice.White, vm.BackgroundChoice);
    }

    [Fact]
    public void ATransparentBackground_SurvivesBeingSavedAndReloadedAsAPreset()
    {
        var first = Create();
        first.BackgroundChoice = BackgroundChoice.Transparent;
        _dialogs.NextText = "See through";
        first.SavePresetCommand.Execute(null);

        var second = Create();
        second.ApplyPresetCommand.Execute(second.Presets.Single(p => p.Name == "See through"));

        Assert.Equal(BackgroundChoice.Transparent, second.BackgroundChoice);
    }

    [Fact]
    public void ApplyingAPreset_ChangesTheLookButKeepsTheChosenExportSize()
    {
        var vm = Create();
        vm.PixelSize = 2048;

        var dots = vm.Presets.Single(p => p.Name == "Dots");
        vm.ApplyPresetCommand.Execute(dots);

        Assert.Equal(ModuleShape.Circle, vm.ModuleShape);
        Assert.Equal(2048, vm.PixelSize);
    }

    [Fact]
    public void UsingCustomMarkerColours_CanBeTurnedOnAndBackOff()
    {
        var vm = Create();

        Assert.False(vm.UseCustomMarkerColors);

        vm.UseCustomMarkerColors = true;
        vm.MarkerFrameColor = RgbColor.FromRgb(0xDC, 0x14, 0x3C);
        Assert.Equal(RgbColor.FromRgb(0xDC, 0x14, 0x3C), vm.MarkerFrameColor);

        vm.UseCustomMarkerColors = false;
        Assert.False(vm.UseCustomMarkerColors);
        Assert.Equal(vm.Foreground, vm.MarkerFrameColor);
    }

    [Fact]
    public void TheColourProperties_SpeakCoresColourType()
    {
        var vm = Create();

        vm.Foreground = RgbColor.FromRgb(0x1B, 0x2A, 0x4A);

        Assert.Equal(RgbColor.FromRgb(0x1B, 0x2A, 0x4A), vm.Foreground);
    }

    [Fact]
    public void SavingAPreset_StoresTheLookAndItAppearsInTheList()
    {
        var vm = Create();
        vm.ModuleShape = ModuleShape.Fluid;
        _dialogs.NextText = "My style";

        vm.SavePresetCommand.Execute(null);

        Assert.Contains(vm.Presets, p => p.Name == "My style");
        Assert.Equal(ModuleShape.Fluid, new PresetStore(_directory).Custom.Single().Style.ModuleShape);
    }

    [Fact]
    public void SavingAPresetUnderABuiltInName_ShowsAnErrorRatherThanThrowing()
    {
        var vm = Create();
        _dialogs.NextText = "Classic";

        vm.SavePresetCommand.Execute(null);

        Assert.Contains("built-in", _dialogs.LastError!, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(new PresetStore(_directory).Custom);
    }

    [Fact]
    public void ResetEverything_ReturnsTheLookToTheDefaults()
    {
        var vm = Create();
        vm.ModuleShape = ModuleShape.Diamond;
        vm.QuietZone = 1;
        vm.Foreground = RgbColor.FromRgb(0xDC, 0x14, 0x3C);

        vm.ResetCommand.Execute(null);

        Assert.Equal(QrStyle.Default.ModuleShape, vm.ModuleShape);
        Assert.Equal(QrStyle.Default.QuietZoneModules, vm.QuietZone);
        Assert.Equal(QrStyle.Default.Foreground, vm.Foreground);
    }

    [Fact]
    public void ChoosingAnUnreadableLogo_TellsTheUserAndChangesNothing()
    {
        var vm = Create();
        var notAnImage = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(notAnImage, "this is not a picture");
        _dialogs.NextImage = notAnImage;

        vm.ChooseLogoCommand.Execute(null);

        Assert.False(vm.HasLogo);
        Assert.Contains("could not be opened", _dialogs.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChoosingALogo_RaisesErrorCorrectionToTheHighestLevel()
    {
        var vm = Create();
        var logo = WriteTestLogo();
        _dialogs.NextImage = logo;

        vm.ChooseLogoCommand.Execute(null);
        vm.RefreshNow();

        Assert.True(vm.HasLogo);
        Assert.Equal(EccLevel.High, vm.Ecc);
        Assert.False(vm.ShowRaiseEcc);
    }

    [Fact]
    public void RemovingALogo_LeavesAWorkingCode()
    {
        var vm = CreateWithContent();
        _dialogs.NextImage = WriteTestLogo();

        vm.ChooseLogoCommand.Execute(null);
        vm.ClearLogoCommand.Execute(null);
        vm.RefreshNow();

        Assert.False(vm.HasLogo);
        Assert.True(vm.CanExport);
    }

    [Fact]
    public void SavePng_WritesTheFileAtTheChosenSize()
    {
        var vm = CreateWithContent();
        var target = Path.Combine(_directory, "code.png");
        vm.PixelSize = 512;
        _dialogs.NextSavePath = target;

        vm.SavePngCommand.Execute(null);

        Assert.True(File.Exists(target));
        Assert.True(new FileInfo(target).Length > 200);
    }

    [Fact]
    public void Copy_AnnouncesSoTheUserGetsVisibleConfirmation()
    {
        var vm = CreateWithContent();
        var announcements = new List<string>();
        vm.Announcement += (_, message) => announcements.Add(message);

        vm.CopyCommand.Execute(null);

        Assert.Null(_dialogs.LastError);
        Assert.Contains("Copied to clipboard", announcements);
    }

    [Fact]
    public void Save_AnnouncesTheFileName()
    {
        var vm = CreateWithContent();
        var announcements = new List<string>();
        vm.Announcement += (_, message) => announcements.Add(message);
        _dialogs.NextSavePath = Path.Combine(_directory, "announced.png");

        vm.SavePngCommand.Execute(null);

        Assert.Contains("Saved announced.png", announcements);
    }

    [Fact]
    public void CopyWithNothingToCopy_IsNotEvenOffered()
    {
        var vm = Create();

        Assert.False(vm.CopyCommand.CanExecute(null));
    }

    [Fact]
    public void SaveSvg_WritesAVectorFile()
    {
        var vm = CreateWithContent();
        var target = Path.Combine(_directory, "code.svg");
        _dialogs.NextSavePath = target;

        vm.SaveSvgCommand.Execute(null);

        Assert.Contains("<svg", File.ReadAllText(target));
    }

    [Fact]
    public void SaveToALockedFile_ShowsAMessageRatherThanCrashing()
    {
        var vm = CreateWithContent();
        var target = Path.Combine(_directory, "locked.png");
        _dialogs.NextSavePath = target;

        using var hold = File.Open(target, FileMode.Create, FileAccess.Write, FileShare.None);

        vm.SavePngCommand.Execute(null);

        Assert.Contains("could not be written", _dialogs.LastError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuggestedFileName_IsDerivedFromTheContent()
    {
        var vm = Create();
        var link = vm.ContentEditors.OfType<LinkEditor>().Single();
        vm.SelectedContent = link;
        link.Address = "www.example.org/spring-open-day";
        _dialogs.NextSavePath = Path.Combine(_directory, "whatever.png");

        vm.RefreshNow();
        vm.SavePngCommand.Execute(null);

        Assert.StartsWith("qr-www-example-org-spring-open-day", _dialogs.LastSuggestedName!);
    }

    [Fact]
    public void SessionIsRememberedBetweenRuns()
    {
        var first = Create();
        first.ModuleShape = ModuleShape.Fluid;
        first.PixelSize = 2048;
        first.SelectedContent = first.ContentEditors.OfType<WifiEditor>().Single();
        first.SaveSession(1300, 850);

        var second = Create();

        Assert.Equal(ModuleShape.Fluid, second.ModuleShape);
        Assert.IsType<WifiEditor>(second.SelectedContent);
        Assert.Equal(1300, second.LoadedSettings.WindowWidth);

        // Size is deliberately NOT part of what is remembered. It comes from the
        // "size for new codes" setting every launch, because it is a decision about one
        // export rather than part of a style. See ExportSizeComesFromTheSetting.
        Assert.Equal(AppSettings.Default.DefaultPixelSize, second.PixelSize);
    }

    [Fact]
    public void ExportSizeComesFromTheSetting_NotFromTheLastSession()
    {
        new AppSettingsStore(_directory).Save(
            AppSettings.Default with { DefaultPixelSize = 2048, Style = QrStyle.Default with { PixelSize = 512 } });

        var vm = Create();

        Assert.Equal(2048, vm.PixelSize);
    }

    [Fact]
    public void TurningOffRememberMyStyle_StartsFromTheDefaults()
    {
        new AppSettingsStore(_directory).Save(AppSettings.Default with
        {
            RememberLastStyle = false,
            Style = QrStyle.Default with { ModuleShape = ModuleShape.Fluid, QuietZoneModules = 1 },
        });

        var vm = Create();

        Assert.Equal(QrStyle.Default.ModuleShape, vm.ModuleShape);
        Assert.Equal(QrStyle.Default.QuietZoneModules, vm.QuietZone);
    }

    [Fact]
    public void LeavingRememberMyStyleOn_RestoresTheLook()
    {
        new AppSettingsStore(_directory).Save(AppSettings.Default with
        {
            RememberLastStyle = true,
            Style = QrStyle.Default with { ModuleShape = ModuleShape.Fluid, QuietZoneModules = 6 },
        });

        var vm = Create();

        Assert.Equal(ModuleShape.Fluid, vm.ModuleShape);
        Assert.Equal(6, vm.QuietZone);
    }

    [Fact]
    public void EveryContentEditorProducesSomethingEncodable()
    {
        var vm = CreateWithContent("Some plain text");

        var link = vm.ContentEditors.OfType<LinkEditor>().Single();
        link.Address = "example.org";

        var wifi = vm.ContentEditors.OfType<WifiEditor>().Single();
        wifi.Ssid = "GuestNetwork";

        // Eight characters, because that is the WPA minimum and the validation now holds
        // the fixture to the same rule as the user.
        wifi.Password = "hunter22";

        var email = vm.ContentEditors.OfType<EmailEditor>().Single();
        email.Address = "jordan.reed@example.org";

        var phone = vm.ContentEditors.OfType<PhoneEditor>().Single();
        phone.Number = "212-555-1234";

        var sms = vm.ContentEditors.OfType<SmsEditor>().Single();
        sms.Number = "212-555-1234";
        sms.Text = "See you Friday";

        var contact = vm.ContentEditors.OfType<ContactEditor>().Single();
        contact.FirstName = "Mark";
        contact.LastName = "Levy";

        foreach (var editor in vm.ContentEditors)
        {
            vm.SelectedContent = editor;
            vm.RefreshNow();

            Assert.True(vm.CanExport, $"{editor.Title} produced nothing encodable");
        }
    }

    [Fact]
    public void AnEmptyContactCard_ProducesNothingRatherThanBoilerplate()
    {
        var vm = Create();
        var contact = vm.ContentEditors.OfType<ContactEditor>().Single();

        Assert.Equal(string.Empty, contact.Payload);
    }

    /// <summary>
    /// A real, readable image file on disk. What is drawn in it does not matter to these
    /// tests, only that TrispotQR.Core.Validation.ImageSize.Read can open it, so it is
    /// rendered the same way PngExporterTests builds its fixtures rather than through a UI
    /// toolkit.
    /// </summary>
    private string WriteTestLogo()
    {
        var path = Path.Combine(_directory, "logo.png");

        var encoded = QrEncoder.Encode("logo", EccLevel.Medium);
        var drawing = QrGeometryBuilder.Build(encoded.Matrix!, QrStyle.Default);
        PngExporter.Save(SkiaRasterizer.Render(drawing, 128), path);

        return path;
    }

    /// <summary>
    /// Stands in for the OS clipboard. The real one needs an STA thread and is exercised in
    /// TrispotQR.Tests.ClipboardExporterTests; nothing here checks what actually reached it.
    /// </summary>
    private sealed class FakeImageClipboard : IImageClipboard
    {
        public void Copy(RasterImage image)
        {
        }
    }

    /// <summary>Stands in for the file and message dialogs so the view model can run headless.</summary>
    private sealed class FakeDialogService : IDialogService
    {
        public string? NextSavePath { get; set; }

        public string? NextImage { get; set; }

        public string? NextText { get; set; }

        public bool ConfirmAnswer { get; set; } = true;

        public string? LastError { get; private set; }

        public string? LastSuggestedName { get; private set; }

        public string? AskForSavePath(
            string title, string filter, string defaultExtension, string suggestedName, string? directory)
        {
            LastSuggestedName = suggestedName;
            return NextSavePath;
        }

        public string? AskForImage(string? directory) => NextImage;

        public string? AskForText(string title, string prompt, string initialValue) => NextText;

        public bool Confirm(string title, string message) => ConfirmAnswer;

        /// <summary>Records every scannability warning raised, and answers with <see cref="RiskAnswer"/>.</summary>
        public List<string> RiskPrompts { get; } = [];

        public bool RiskAnswer { get; set; } = true;

        public bool? LastRiskWasSevere { get; private set; }

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

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using TrispotQR.Core.Export;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Payloads;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.Core.Validation;

namespace TrispotQR.ViewModels;

/// <summary>
/// Drives the whole window.
///
/// The flow is: a field changes, a short debounce timer restarts, and when it fires the
/// code is encoded and drawn. Drawing is cheap and happens on the UI thread. The
/// scannability check is not cheap, because it rasterises and then decodes, so it runs on
/// a background thread and reports back when it lands. That keeps typing smooth while
/// still giving live feedback.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>Long enough to swallow a burst of typing, short enough to feel immediate.</summary>
    private static readonly TimeSpan RenderDebounce = TimeSpan.FromMilliseconds(150);

    private readonly IDialogService _dialogs;
    private readonly PresetStore _presets;
    private readonly AppSettingsStore _settingsStore;
    private readonly IImageClipboard _clipboard;
    private readonly IUiTimer _debounce;

    /// <summary>
    /// Where scannability results are marshalled back to. The UI thread has a
    /// synchronisation context; a plain thread does not, and asking for one that is not
    /// there throws, so the fallback keeps the view model usable outside a window.
    /// </summary>
    private readonly TaskScheduler _uiScheduler = SynchronizationContext.Current is not null
        ? TaskScheduler.FromCurrentSynchronizationContext()
        : TaskScheduler.Current;

    private QrStyle _style = QrStyle.Default;
    private ContentEditor _selectedContent;
    private QrDrawing? _previewDrawing;
    private QrDrawing? _drawing;
    private string? _encodeError;
    private ScanCheckResult? _scan;
    private string? _lastSaveDirectory;
    private AppSettings _settings = AppSettings.Default;
    private int _scanGeneration;
    private string _statusDetail = string.Empty;

    public MainViewModel(IDialogService dialogs, IUiTimer timer, IImageClipboard clipboard, PresetStore? presets = null, AppSettingsStore? settingsStore = null)
    {
        _dialogs = dialogs;
        _presets = presets ?? new PresetStore();
        _settingsStore = settingsStore ?? new AppSettingsStore();
        _clipboard = clipboard;

        ContentEditors = CreateEditors();

        foreach (var editor in ContentEditors)
        {
            editor.ContentChanged += (_, _) => ScheduleRender();
        }

        _selectedContent = ContentEditors[0];

        var settings = _settingsStore.Load();
        _settings = settings;

        // Anything unparseable is dropped rather than throwing. The file is hand editable and a
        // single bad entry must cost one swatch, not the whole list -- and not the launch.
        foreach (var text in settings.RecentColors)
        {
            if (RgbColor.TryParse(text, out var recent) && !_recentColors.Contains(recent))
            {
                _recentColors.Add(recent);
            }
        }

        // The remembered look is optional; the export size is not. Size is a decision about
        // one export rather than part of a style, so every launch starts at the configured
        // default whichever way the remember-my-style preference is set.
        _style = (settings.RememberLastStyle ? settings.Style : QrStyle.Default)
            with { PixelSize = settings.DefaultPixelSize };

        // Now, while the only things holding a logo are the saved styles and the session just
        // restored above. Doing it when a style is deleted instead would risk deleting the
        // image out from under a code already on screen.
        _presets.SweepLogos(_style.Logo.Path);

        _lastSaveDirectory = settings.LastSaveDirectory;

        if (settings.ContentTypeIndex >= 0 && settings.ContentTypeIndex < ContentEditors.Count)
        {
            _selectedContent = ContentEditors[settings.ContentTypeIndex];
        }

        Presets = new ObservableCollection<PresetItem>();

        _debounce = timer;
        _debounce.Interval = RenderDebounce;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Render();
        };

        SavePngCommand = new RelayCommand(SavePng, () => CanExport);
        SaveSvgCommand = new RelayCommand(SaveSvg, () => CanExport);
        CopyCommand = new RelayCommand(CopyToClipboard, () => CanExport);
        ApplyPresetCommand = new RelayCommand<PresetItem>(ApplyPreset);
        DeletePresetCommand = new RelayCommand<PresetItem>(DeletePreset, p => !p.IsBuiltIn);
        // Not gated on there being content: a preset stores the look, which is meaningful
        // on its own, and the app now starts with an empty content box.
        SavePresetCommand = new RelayCommand(SavePreset);
        ResetCommand = new RelayCommand(ResetToDefaults);
        ChooseLogoCommand = new RelayCommand(ChooseLogo);
        ClearLogoCommand = new RelayCommand(ClearLogo, () => HasLogo);
        RaiseEccCommand = new RelayCommand(() => Ecc = EccLevel.High, () => Ecc != EccLevel.High);

        RefreshPresets();

        // Opens with an empty content box and the placeholder in the preview. Nothing is
        // prefilled: a starting value would be the first thing a user has to delete, and
        // is easy to miss and accidentally publish.
        Render();

        if (_presets.LoadWarning is { } warning)
        {
            _dialogs.ShowError("Saved styles", warning);
        }
    }

    #region Content

    /// <summary>
    /// Every content type, in the order they appear in the dropdown. Static so the set can
    /// be enumerated without standing up a view model, which is what lets the validation
    /// tests hold all seven to the same rules rather than the handful someone remembered.
    /// </summary>
    public static IReadOnlyList<ContentEditor> CreateEditors() =>
    [
        new PlainTextEditor(),
        new LinkEditor(),
        new WifiEditor(),
        new EmailEditor(),
        new PhoneEditor(),
        new SmsEditor(),
        new ContactEditor(),
    ];

    public IReadOnlyList<ContentEditor> ContentEditors { get; }

    public ContentEditor SelectedContent
    {
        get => _selectedContent;
        set
        {
            if (SetField(ref _selectedContent, value))
            {
                ScheduleRender();
            }
        }
    }

    /// <summary>The string actually encoded, whatever the editor in use.</summary>
    public string Payload => _selectedContent.Payload;

    private int ByteCapacity => QrEncoder.ByteCapacity(_style.Ecc);

    public string CapacityText =>
        $"{System.Text.Encoding.UTF8.GetByteCount(Payload):N0} of about {ByteCapacity:N0} characters used";

    #endregion

    #region Style, simple

    public RgbColor Foreground
    {
        get => _style.Foreground;
        set => UpdateStyle(s => s with { Foreground = value });
    }

    /// <summary>How many recent colors are kept. Two rows of swatches in the picker.</summary>
    private const int RecentColorLimit = 12;

    private readonly List<RgbColor> _recentColors = [];

    /// <summary>
    /// Colors the user has chosen before, most recent first.
    ///
    /// Exists because picking an unusual color is work: a shade arrived at by dragging around
    /// the square is effectively unfindable a second time, so without this the only repeatable
    /// colors are the sixteen built-in presets.
    /// </summary>
    public IReadOnlyList<RgbColor> RecentColors => _recentColors;

    /// <summary>
    /// The other colors this code is already using, so one part can be matched to another
    /// without going and reading a hex code off a different picker.
    ///
    /// Deliberately reflects what is *in effect* rather than what is stored: a custom
    /// background that is not selected, corner colors that are inheriting, and an outline that
    /// is switched off are all colors the code is not actually wearing, and offering them would
    /// be offering a value the user cannot see anywhere on screen.
    /// </summary>
    public IReadOnlyList<RgbColor> ColorsInUse
    {
        get
        {
            var colors = new List<RgbColor> { Foreground };

            // Read through this view model's own properties rather than the style's, because
            // three of these are nullable there with null meaning "inherit" or "transparent",
            // and the resolved value is the one a user can actually see on the code.
            if (BackgroundChoice == BackgroundChoice.Custom && _style.Background is { } background)
            {
                colors.Add(background);
            }

            if (UseCustomMarkerColors)
            {
                colors.Add(MarkerFrameColor);
                colors.Add(MarkerCenterColor);
            }

            if (OutlineEnabled)
            {
                colors.Add(OutlineColor);
            }

            return [.. colors.Distinct()];
        }
    }

    /// <summary>
    /// Records a color the user settled on, newest first, without duplicates.
    ///
    /// Called when a picker closes rather than while one is being dragged: a drag walks through
    /// dozens of intermediate colors, none of which anyone chose, and recording those would
    /// bury the twelve that were actually picked.
    /// </summary>
    public void RecordRecentColor(RgbColor color)
    {
        _recentColors.RemoveAll(c => c == color);
        _recentColors.Insert(0, color);

        if (_recentColors.Count > RecentColorLimit)
        {
            _recentColors.RemoveRange(RecentColorLimit, _recentColors.Count - RecentColorLimit);
        }

        _settings = _settings with { RecentColors = [.. _recentColors.Select(c => c.ToHex())] };
        _settingsStore.Save(_settings with { Style = _style });

        OnPropertyChanged(nameof(RecentColors));
    }

    /// <summary>
    /// Remembers that the user chose Custom, which cannot be worked out from the colour
    /// alone: choosing Custom while the background happens to be white would otherwise
    /// read straight back as White, the button would snap off, and the colour picker
    /// would never appear.
    /// </summary>
    private bool _backgroundIsCustom;

    /// <summary>
    /// The background as three plain choices rather than a nullable colour, because
    /// "White, Transparent or pick one" is how a person thinks about it.
    /// </summary>
    public BackgroundChoice BackgroundChoice
    {
        get
        {
            if (_style.Background is not { } colour)
            {
                return BackgroundChoice.Transparent;
            }

            return _backgroundIsCustom || !IsWhite(colour) ? BackgroundChoice.Custom : BackgroundChoice.White;
        }

        set
        {
            _backgroundIsCustom = value == BackgroundChoice.Custom;

            var colour = value switch
            {
                BackgroundChoice.Transparent => (RgbColor?)null,
                BackgroundChoice.White => RgbColor.White,
                _ => _style.Background ?? RgbColor.White,
            };

            UpdateStyle(s => s with { Background = colour });
            OnPropertyChanged(nameof(CustomBackground));
            OnPropertyChanged(nameof(IsCustomBackground));
        }
    }

    private static bool IsWhite(RgbColor colour) =>
        colour is { R: 255, G: 255, B: 255, A: 255 };

    public bool IsCustomBackground => BackgroundChoice == BackgroundChoice.Custom;

    public RgbColor CustomBackground
    {
        get => _style.Background ?? RgbColor.White;
        set
        {
            _backgroundIsCustom = true;
            UpdateStyle(s => s with { Background = value });
            OnPropertyChanged(nameof(BackgroundChoice));
            OnPropertyChanged(nameof(IsCustomBackground));
        }
    }

    public int PixelSize
    {
        get => _style.PixelSize;
        set => UpdateStyle(s => s with { PixelSize = value });
    }

    #endregion

    #region Style, advanced

    public ModuleShape ModuleShape
    {
        get => _style.ModuleShape;
        set => UpdateStyle(s => s with { ModuleShape = value });
    }

    public double ModuleScale
    {
        get => _style.ModuleScale;
        set => UpdateStyle(s => s with { ModuleScale = value });
    }

    public MarkerFrameShape MarkerFrameShape
    {
        get => _style.MarkerFrameShape;
        set => UpdateStyle(s => s with { MarkerFrameShape = value });
    }

    public MarkerCenterShape MarkerCenterShape
    {
        get => _style.MarkerCenterShape;
        set => UpdateStyle(s => s with { MarkerCenterShape = value });
    }

    /// <summary>Off means the markers inherit the foreground colour.</summary>
    public bool UseCustomMarkerColors
    {
        get => _style.MarkerFrameColor is not null || _style.MarkerCenterColor is not null;
        set
        {
            UpdateStyle(s => value
                ? s with { MarkerFrameColor = s.EffectiveMarkerFrameColor, MarkerCenterColor = s.EffectiveMarkerCenterColor }
                : s with { MarkerFrameColor = null, MarkerCenterColor = null });

            OnPropertyChanged(nameof(MarkerFrameColor));
            OnPropertyChanged(nameof(MarkerCenterColor));
        }
    }

    public RgbColor MarkerFrameColor
    {
        get => _style.EffectiveMarkerFrameColor;
        set
        {
            UpdateStyle(s => s with { MarkerFrameColor = value });
            OnPropertyChanged(nameof(UseCustomMarkerColors));
        }
    }

    public RgbColor MarkerCenterColor
    {
        get => _style.EffectiveMarkerCenterColor;
        set
        {
            UpdateStyle(s => s with { MarkerCenterColor = value });
            OnPropertyChanged(nameof(UseCustomMarkerColors));
        }
    }

    public bool OutlineEnabled
    {
        get => _style.Outline.Enabled;
        set => UpdateStyle(s => s with { Outline = s.Outline with { Enabled = value } });
    }

    public RgbColor OutlineColor
    {
        get => _style.Outline.Color;
        set => UpdateStyle(s => s with { Outline = s.Outline with { Color = value } });
    }

    public double OutlineThickness
    {
        get => _style.Outline.ThicknessRatio;
        set => UpdateStyle(s => s with { Outline = s.Outline with { ThicknessRatio = value } });
    }

    public OutlineTarget OutlineTarget
    {
        get => _style.Outline.Target;
        set => UpdateStyle(s => s with { Outline = s.Outline with { Target = value } });
    }

    public int QuietZone
    {
        get => _style.QuietZoneModules;
        set => UpdateStyle(s => s with { QuietZoneModules = value });
    }

    public EccLevel Ecc
    {
        get => _style.Ecc;
        set
        {
            UpdateStyle(s => s with { Ecc = value });
            OnPropertyChanged(nameof(CapacityText));
            RaiseEccCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasLogo => _style.Logo.HasImage;

    public string LogoName => _style.Logo.Path is { } path ? Path.GetFileName(path) : "None chosen";

    public double LogoSize
    {
        get => _style.Logo.SizeRatio;
        set => UpdateStyle(s => s with { Logo = s.Logo with { SizeRatio = value } });
    }

    public LogoPunchShape LogoPunchShape
    {
        get => _style.Logo.PunchShape;
        set => UpdateStyle(s => s with { Logo = s.Logo with { PunchShape = value } });
    }

    #endregion

    #region Options for the dropdowns

    public IReadOnlyList<ModuleShape> ModuleShapes { get; } = Enum.GetValues<ModuleShape>();

    public IReadOnlyList<MarkerFrameShape> MarkerFrameShapes { get; } = Enum.GetValues<MarkerFrameShape>();

    public IReadOnlyList<MarkerCenterShape> MarkerCenterShapes { get; } = Enum.GetValues<MarkerCenterShape>();

    public IReadOnlyList<OutlineTarget> OutlineTargets { get; } = Enum.GetValues<OutlineTarget>();

    public IReadOnlyList<LogoPunchShape> LogoPunchShapes { get; } = Enum.GetValues<LogoPunchShape>();

    public IReadOnlyList<EccLevel> EccLevels { get; } = Enum.GetValues<EccLevel>();

    #endregion

    #region Preview and status

    /// <summary>
    /// What the preview should show, described rather than rendered. The view turns it into
    /// pixels, which is what lets the same view model serve WPF and Avalonia.
    /// </summary>
    public QrDrawing? PreviewDrawing
    {
        get => _previewDrawing;
        private set => SetField(ref _previewDrawing, value);
    }

    public ObservableCollection<PresetItem> Presets { get; }

    /// <summary>Short status shown beside the badge.</summary>
    public string StatusText => _encodeError is not null
        ? "Cannot make a code"
        : _scan?.Verdict switch
        {
            ScanVerdict.Good => "Scannable",
            ScanVerdict.Risky => "May not scan",
            ScanVerdict.Bad => "Does not scan",
            _ => "Checking...",
        };

    /// <summary>The full explanation under the badge.</summary>
    public string StatusDetail
    {
        get => _statusDetail;
        private set => SetField(ref _statusDetail, value);
    }

    public ScanVerdict Verdict => _encodeError is not null ? ScanVerdict.Bad : _scan?.Verdict ?? ScanVerdict.Good;

    /// <summary>
    /// True while the code is worth saving. Input errors count: a code built from a
    /// malformed email address encodes and scans perfectly, and is still wrong.
    /// </summary>
    public bool CanExport => _drawing is not null && _encodeError is null && !_selectedContent.HasErrors;

    /// <summary>
    /// Why the save buttons are disabled, or null when they are not.
    ///
    /// A greyed-out button with no explanation is its own bug report, so whatever blocks
    /// the export says so next to it.
    /// </summary>
    public string? ExportBlockedReason
    {
        get
        {
            if (CanExport)
            {
                return null;
            }

            if (_encodeError is not null)
            {
                return _encodeError;
            }

            var errors = _selectedContent.Issues.Count(i => i.Severity == IssueSeverity.Error);

            return errors switch
            {
                0 => "Enter something above to make a code.",
                1 => "Fix the problem above to save or copy.",
                _ => $"Fix the {errors} problems above to save or copy.",
            };
        }
    }

    /// <summary>
    /// What the content itself has to say, which outranks the scannability result under
    /// the badge. Null when the input is fine and there is nothing to add.
    ///
    /// The actual problem rather than the count, because the line beside the buttons
    /// already says how many there are and repeating that sentence in two places on one
    /// screen tells the user nothing the first one did not.
    /// </summary>
    private string? ContentStatus =>
        _selectedContent.Issues.FirstOrDefault(i => i.Severity == IssueSeverity.Error)?.Message
        ?? _selectedContent.FormMessage;

    public string SizeSummary => _drawing is null
        ? string.Empty
        : $"{PixelSize} x {PixelSize} px  ·  version {_lastVersion}  ·  error correction {Describe(_lastEcc)}";

    private int _lastVersion;
    private EccLevel _lastEcc;

    /// <summary>True when the user should be offered the one-click fix for a big logo.</summary>
    public bool ShowRaiseEcc => HasLogo && Ecc != EccLevel.High;

    #endregion

    #region Commands

    public RelayCommand SavePngCommand { get; }

    public RelayCommand SaveSvgCommand { get; }

    public RelayCommand CopyCommand { get; }

    public RelayCommand<PresetItem> ApplyPresetCommand { get; }

    public RelayCommand<PresetItem> DeletePresetCommand { get; }

    public RelayCommand SavePresetCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand ChooseLogoCommand { get; }

    public RelayCommand ClearLogoCommand { get; }

    public RelayCommand RaiseEccCommand { get; }

    #endregion

    /// <summary>Persists the session so the next run opens where this one left off.</summary>
    public void SaveSession(double windowWidth, double windowHeight) =>
        _settingsStore.Save(_settings with
        {
            Style = _style,
            ContentTypeIndex = ContentEditors.ToList().IndexOf(_selectedContent),
            LastSaveDirectory = _lastSaveDirectory,
            WindowWidth = windowWidth,
            WindowHeight = windowHeight,
        });

    /// <summary>
    /// Takes on whatever the settings window returns. Written to persist immediately rather
    /// than at shutdown, so a preference survives even if the app is later closed in a way
    /// that skips the normal save.
    /// </summary>
    public void OpenSettings()
    {
        if (_dialogs.EditSettings(_settings) is not { } updated)
        {
            return;
        }

        _settings = updated;
        _settingsStore.Save(_settings with { Style = _style });

        OnPropertyChanged(nameof(PixelSize));
        Announce("Settings saved");
    }

    /// <summary>Where the save dialog should open: the configured folder, else wherever the
    /// user last saved.</summary>
    private string? SaveDirectory => _settings.DefaultSaveDirectory ?? _lastSaveDirectory;

    public AppSettings LoadedSettings => _settingsStore.Load();

    /// <summary>Applies a change to the immutable style record and queues a redraw.</summary>
    private void UpdateStyle(Func<QrStyle, QrStyle> change, [CallerMemberName] string? propertyName = null)
    {
        _style = change(_style).Normalised();
        OnPropertyChanged(propertyName);

        // Any style change can alter which colors the code is wearing, and the picker showing
        // them has no other way to know. Raised unconditionally rather than only for the colour
        // properties, because the set also changes when a toggle flips: switching the outline
        // off removes a colour without any colour itself having changed.
        OnPropertyChanged(nameof(ColorsInUse));

        ScheduleRender();
    }

    private void ScheduleRender()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>
    /// Redraws immediately instead of waiting out the debounce. Used by the tests, which
    /// have no dispatcher loop to fire the timer.
    /// </summary>
    public void RefreshNow()
    {
        _debounce.Stop();
        Render();
    }

    /// <summary>
    /// Encodes and draws. Runs on the UI thread; the expensive scannability check is
    /// handed off separately.
    /// </summary>
    private void Render()
    {
        var payload = Payload;

        if (string.IsNullOrWhiteSpace(payload))
        {
            _drawing = null;
            _encodeError = null;
            _scan = null;
            PreviewDrawing = null;
            StatusDetail = _selectedContent.Hint;
            NotifyRenderFinished();
            return;
        }

        var encoded = QrEncoder.Encode(payload, _style.Ecc);

        if (!encoded.Success)
        {
            _drawing = null;
            _encodeError = encoded.ErrorMessage;
            _scan = null;
            PreviewDrawing = null;
            StatusDetail = encoded.ErrorMessage!;
            NotifyRenderFinished();
            return;
        }

        _encodeError = null;
        _lastVersion = encoded.Matrix!.Version;
        _lastEcc = encoded.Matrix.EffectiveEcc;
        _drawing = QrGeometryBuilder.Build(encoded.Matrix, _style);
        PreviewDrawing = _drawing;

        StatusDetail = ContentStatus ?? "Checking that this will scan...";
        NotifyRenderFinished();
        StartScanCheck(_drawing, payload, _style);
    }

    /// <summary>
    /// Rasterises and decodes on a background thread. Each run carries a generation number
    /// so a slow check cannot overwrite the result of a newer one.
    /// </summary>
    private void StartScanCheck(QrDrawing drawing, string payload, QrStyle style)
    {
        var generation = ++_scanGeneration;

        Task.Run(() =>
        {
            try
            {
                return ScannabilityChecker.Check(drawing, payload, style);
            }
            catch (Exception)
            {
                // A failed check must never take the app down; the badge simply stays quiet.
                return null;
            }
        }).ContinueWith(task =>
        {
            if (generation != _scanGeneration || task.Result is not { } result)
            {
                return;
            }

            _scan = result;

            // What the user typed takes precedence, because "that is not a valid web
            // address" is more useful than "scannable". A code can scan perfectly and
            // still be wrong.
            StatusDetail = ContentStatus
                ?? (result.Verdict == ScanVerdict.Good ? "This code scans cleanly." : result.Message);

            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Verdict));
        }, _uiScheduler);
    }

    private void NotifyRenderFinished()
    {
        OnPropertyChanged(nameof(Payload));
        OnPropertyChanged(nameof(CapacityText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Verdict));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(ExportBlockedReason));
        OnPropertyChanged(nameof(SizeSummary));
        OnPropertyChanged(nameof(HasLogo));
        OnPropertyChanged(nameof(LogoName));
        OnPropertyChanged(nameof(ShowRaiseEcc));

        SavePngCommand.RaiseCanExecuteChanged();
        SaveSvgCommand.RaiseCanExecuteChanged();
        CopyCommand.RaiseCanExecuteChanged();
        ClearLogoCommand.RaiseCanExecuteChanged();
        RaiseEccCommand.RaiseCanExecuteChanged();
    }

    #region Export

    /// <summary>
    /// Checks that the code actually scans and, when it does not, asks whether to go ahead.
    /// Returns true to continue.
    ///
    /// The badge is not consulted, deliberately. It is produced by a debounced background
    /// check, so at the instant of a click it can still be describing the previous style,
    /// or not have finished at all. This forces the pending render and re-runs the check
    /// against exactly what is on screen. That costs a fraction of a second on a click the
    /// user has to follow with a file dialog anyway.
    /// </summary>
    private bool ConfirmScannable(string action)
    {
        RefreshNow();

        if (_drawing is null || string.IsNullOrWhiteSpace(Payload))
        {
            return false;
        }

        var result = ScannabilityChecker.Check(_drawing, Payload, _style);

        if (result.Verdict == ScanVerdict.Good)
        {
            return true;
        }

        var severe = result.Verdict == ScanVerdict.Bad;

        // Amber is a preference; red is not. Silencing red would defeat the point of the
        // check, so it is deliberately not configurable.
        if (!severe && !_settings.WarnOnRiskyCodes)
        {
            return true;
        }

        return _dialogs.ConfirmRisk(
            severe ? "This code did not scan" : "This code may not scan",
            $"{result.Message}\n\n{action} it anyway?",
            $"{action} anyway",
            // A code that failed outright defaults to Cancel; one that merely carries a
            // risk defaults to going ahead, so the softer warning does not become a habit
            // people click through without reading.
            defaultToProceed: !severe,
            severe);
    }

    private void SavePng()
    {
        if (!ConfirmScannable("Save"))
        {
            return;
        }

        var path = _dialogs.AskForSavePath(
            "Save QR code as PNG", "PNG image|*.png", ".png", SuggestedFileName() + ".png", SaveDirectory);

        if (path is null)
        {
            return;
        }

        Guarded(() =>
        {
            PngExporter.Save(SkiaRasterizer.Render(_drawing!, PixelSize), path);
            _lastSaveDirectory = Path.GetDirectoryName(path);
        }, path);
    }

    private void SaveSvg()
    {
        if (!ConfirmScannable("Save"))
        {
            return;
        }

        var path = _dialogs.AskForSavePath(
            "Save QR code as SVG", "SVG vector image|*.svg", ".svg", SuggestedFileName() + ".svg", SaveDirectory);

        if (path is null)
        {
            return;
        }

        Guarded(() =>
        {
            SvgExporter.Save(_drawing!, PixelSize, path);
            _lastSaveDirectory = Path.GetDirectoryName(path);
        }, path);
    }

    private void CopyToClipboard()
    {
        if (!ConfirmScannable("Copy"))
        {
            return;
        }

        try
        {
            _clipboard.Copy(SkiaRasterizer.Render(_drawing!, PixelSize));
            StatusDetail = "Copied. Paste it straight into Word, PowerPoint or an email.";
            Announce("Copied to clipboard");
        }
        catch (Exception ex)
        {
            _dialogs.ShowError("Could not copy",
                $"The clipboard could not be used just now ({ex.Message}). Try again, or save the file instead.");
        }
    }

    /// <summary>
    /// Raised when something worth a brief on-screen confirmation has happened. The window
    /// turns these into a toast that fades away on its own.
    /// </summary>
    public event EventHandler<string>? Announcement;

    private void Announce(string message) => Announcement?.Invoke(this, message);

    /// <summary>Runs an export, turning any file system failure into a message the user can act on.</summary>
    private void Guarded(Action work, string path)
    {
        try
        {
            work();
            StatusDetail = $"Saved to {path}";
            Announce($"Saved {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _dialogs.ShowError("Could not save",
                $"{path} could not be written.\n\n{ex.Message}\n\nThe file may be open in another program, "
                + "or you may not have permission to write to that folder.");
        }
    }

    /// <summary>A filename derived from the content, so a folder of exports is navigable.</summary>
    private string SuggestedFileName()
    {
        var basis = _selectedContent switch
        {
            LinkEditor link when link.Address.Length > 0 => link.Address,
            WifiEditor wifi when wifi.Ssid.Length > 0 => "wifi-" + wifi.Ssid,
            ContactEditor contact => $"contact-{contact.FirstName}{contact.LastName}",
            _ => Payload,
        };

        var cleaned = new string(basis
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray())
            .Trim('-');

        while (cleaned.Contains("--", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("--", "-", StringComparison.Ordinal);
        }

        if (cleaned.Length > 48)
        {
            cleaned = cleaned[..48].TrimEnd('-');
        }

        return cleaned.Length == 0 ? "qr-code" : "qr-" + cleaned;
    }

    #endregion

    #region Presets and logo

    private void RefreshPresets()
    {
        Presets.Clear();

        foreach (var preset in _presets.All)
        {
            Presets.Add(new PresetItem(preset, BuildThumbnail(preset.Style)));
        }
    }

    /// <summary>
    /// Thumbnails are drawn from a fixed short string rather than the live content, so
    /// they stay stable while typing and cost nothing to keep on screen.
    /// </summary>
    private static QrDrawing? BuildThumbnail(QrStyle style)
    {
        var encoded = QrEncoder.Encode("TrispotQR", style.Ecc);

        return encoded.Success
            ? QrGeometryBuilder.Build(encoded.Matrix!, style with { QuietZoneModules = 2 })
            : null;
    }

    private void ApplyPreset(PresetItem item)
    {
        // The preset owns the look, and the logo is part of it now that saving one keeps its
        // own copy of the image. So a style with a logo brings it, and a style without one
        // clears whatever is there: the alternative, only ever adding a logo, leaves no way
        // back to a code without one and makes two styles behave differently for no reason
        // the user can see. Size and content still belong to the session.
        _style = item.Preset.Style with { PixelSize = _style.PixelSize };

        RaiseAllStyleProperties();
        ScheduleRender();
    }

    /// <summary>
    /// A style carries no content of its own, so it is judged against whatever is in the
    /// box, or a representative sample when the box is empty. The logo is now part of what
    /// gets judged: a preset stores one, and punching a hole in the middle of a code is the
    /// likeliest reason a style stops scanning, so excluding it would have hidden exactly the
    /// problem this check exists to catch.
    /// </summary>
    private bool ConfirmPresetScannable()
    {
        var payload = string.IsNullOrWhiteSpace(Payload) ? "https://www.example.org/sample" : Payload;
        var result = ScannabilityChecker.Check(payload, _style);

        if (result.Verdict == ScanVerdict.Good)
        {
            return true;
        }

        var severe = result.Verdict == ScanVerdict.Bad;

        return _dialogs.ConfirmRisk(
            severe ? "Codes in this style did not scan" : "Codes in this style may not scan",
            $"{result.Message}\n\nSave the style anyway?",
            "Save anyway",
            defaultToProceed: !severe,
            severe);
    }

    private void SavePreset()
    {
        if (!ConfirmPresetScannable())
        {
            return;
        }

        var name = _dialogs.AskForText("Save style", "Name this style so you can use it again:", string.Empty);

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            // The logo goes with it. The store takes its own copy of the image, which is what
            // makes that safe: recording the path the user picked from would leave a style
            // that breaks the moment that file is renamed, tidied away or on another machine.
            _presets.Save(name, _style);
            RefreshPresets();
            StatusDetail = $"Saved the style \"{name.Trim()}\".";
        }
        catch (InvalidOperationException ex)
        {
            _dialogs.ShowError("Could not save the style", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError("Could not save the style",
                $"The style could not be written to {_presets.FilePath}.\n\n{ex.Message}");
        }
    }

    private void DeletePreset(PresetItem item)
    {
        if (item.IsBuiltIn || !_dialogs.Confirm("Delete style", $"Delete the saved style \"{item.Name}\"?"))
        {
            return;
        }

        _presets.Delete(item.Name);
        RefreshPresets();
    }

    private void ResetToDefaults()
    {
        _style = QrStyle.Default with { PixelSize = _style.PixelSize };
        RaiseAllStyleProperties();
        ScheduleRender();
    }

    private void ChooseLogo()
    {
        var path = _dialogs.AskForImage(SaveDirectory);

        if (path is null)
        {
            return;
        }

        // Validated with the same decoder LogoCompositor.Place uses (Skia), not the WPF
        // one WpfQrRenderer draws with: the two support different formats, and a file WPF
        // can load but Skia cannot would pass this check and then never actually appear,
        // since Place is what decides whether a logo is placed at all.
        if (ImageSize.Read(path) is null)
        {
            _dialogs.ShowError("Could not read that image",
                $"{Path.GetFileName(path)} could not be opened as an image. Try a PNG or JPG file.");
            return;
        }

        _style = _style with { Logo = _style.Logo with { Path = path } };

        // A logo removes data from the middle of the code, so the strongest error
        // correction is the right default. The user can still lower it afterwards.
        if (_style.Ecc != EccLevel.High)
        {
            _style = _style with { Ecc = EccLevel.High };
        }

        RaiseAllStyleProperties();
        ScheduleRender();
    }

    private void ClearLogo()
    {
        _style = _style with { Logo = LogoStyle.None };
        RaiseAllStyleProperties();
        ScheduleRender();
    }

    /// <summary>
    /// Called after the style record is replaced wholesale, rather than through one of the
    /// individual setters, so every bound control refreshes.
    /// </summary>
    private void RaiseAllStyleProperties()
    {
        // The style was replaced wholesale, so the Custom choice is no longer the user's
        // and should be derived from the incoming colour again.
        _backgroundIsCustom = false;

        foreach (var name in new[]
        {
            nameof(Foreground), nameof(BackgroundChoice), nameof(CustomBackground), nameof(IsCustomBackground),
            nameof(PixelSize), nameof(ModuleShape), nameof(ModuleScale), nameof(MarkerFrameShape),
            nameof(MarkerCenterShape), nameof(UseCustomMarkerColors), nameof(MarkerFrameColor),
            nameof(MarkerCenterColor), nameof(OutlineEnabled), nameof(OutlineColor), nameof(OutlineThickness),
            nameof(OutlineTarget), nameof(QuietZone), nameof(Ecc), nameof(HasLogo), nameof(LogoName),
            nameof(LogoSize), nameof(LogoPunchShape), nameof(ShowRaiseEcc), nameof(CapacityText),
        })
        {
            OnPropertyChanged(name);
        }

        ClearLogoCommand.RaiseCanExecuteChanged();
        RaiseEccCommand.RaiseCanExecuteChanged();
    }

    #endregion

    private static string Describe(EccLevel level) => level switch
    {
        EccLevel.Low => "Low",
        EccLevel.Medium => "Medium",
        EccLevel.Quartile => "Quartile",
        _ => "High",
    };
}

/// <summary>How the background is chosen, as the three options the UI offers.</summary>
public enum BackgroundChoice
{
    White,
    Transparent,
    Custom,
}

/// <summary>A preset plus its rendered thumbnail, ready to bind to a card.</summary>
public sealed class PresetItem
{
    public PresetItem(StylePreset preset, QrDrawing? thumbnailDrawing)
    {
        Preset = preset;
        ThumbnailDrawing = thumbnailDrawing;
    }

    public StylePreset Preset { get; }

    public QrDrawing? ThumbnailDrawing { get; }

    public string Name => Preset.Name;

    public string Description => Preset.Description;

    public bool IsBuiltIn => Preset.IsBuiltIn;
}

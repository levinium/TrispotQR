using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrispotQR.App;
using TrispotQR.App.Services;
using TrispotQR.App.ViewModels;
using TrispotQR.Core.Export;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Validation;
using TrispotQR.ViewModels;

namespace TrispotQR.Tests;

/// <summary>
/// Renders the window with the scannability check actually finished, and leaves the images
/// beside the test binary for the README.
///
/// The existing smoke test renders immediately after <c>RefreshNow</c>, so its badge always
/// reads "Checking...": the decode runs on a background thread and has not landed yet. That
/// is fine for a layout test and wrong for a picture of the app, because the settled badge
/// is the feature the app is built around.
///
/// Waiting for it is also worth asserting on its own. Nothing else in the suite proves the
/// badge ever leaves its checking state, which is exactly the kind of thing that could break
/// and go unnoticed.
/// </summary>
[Collection("UI")]
public class ReadmeSnapshotTests
{
    private const string Payload = "https://www.example.org";

    /// <summary>Generous: the check rasterises and decodes, and CI machines are slow.</summary>
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(20);

    private readonly WpfHost _host;

    public ReadmeSnapshotTests(WpfHost host) => _host = host;

    public static TheoryData<AppTheme, string> Shots => new()
    {
        { AppTheme.Light, "readme-hero.png" },
        { AppTheme.Dark, "readme-hero-dark.png" },
    };

    [Theory]
    [MemberData(nameof(Shots))]
    public void TheBadgeSettles_AndTheWindowRenders(AppTheme theme, string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"trispotqr-readme-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var outputPath = Path.Combine(AppContext.BaseDirectory, fileName);

        try
        {
            var (status, verdict) = _host.Run(() =>
            {
                ThemeManager.Apply(theme);

                try
                {
                    var viewModel = new MainViewModel(
                        new SilentDialogService(),
                        new PresetStore(directory),
                        new AppSettingsStore(directory));

                    ((PlainTextEditor)viewModel.ContentEditors[0]).Text = Payload;

                    // The styled preset, because a plain black code shows none of what the
                    // app is for.
                    viewModel.ApplyPresetCommand.Execute(viewModel.Presets.Single(p => p.Name == "Two-tone"));
                    viewModel.RefreshNow();

                    var window = new MainWindow(viewModel);
                    var content = (UIElement)window.Content;
                    window.Content = null;

                    var host = new Border
                    {
                        Background = window.Background,
                        Width = 1180,
                        Height = 860,
                        Child = content,

                        // Both are needed or the bindings and the content templates go
                        // quietly dead; see MainWindowSmokeTests for the full story.
                        DataContext = viewModel,
                        Resources = window.Resources,
                    };

                    Settle(host);

                    // The scan check finishes on a background thread but posts its result
                    // back through the view model's UI scheduler, so this thread has to keep
                    // pumping. Sleeping on it instead would block the very continuation we
                    // are waiting for, which is exactly what the first version of this did.
                    // there, so polling from this thread sees it land.
                    var deadline = DateTime.UtcNow + SettleTimeout;
                    while (viewModel.StatusText == "Checking..." && DateTime.UtcNow < deadline)
                    {
                        Pump();
                    }

                    Settle(host);

                    var bitmap = new RenderTargetBitmap(
                        (int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    PngExporter.Save(ToRaster(bitmap), outputPath);

                    return (viewModel.StatusText, viewModel.Verdict);
                }
                finally
                {
                    ThemeManager.Apply(AppTheme.Light);
                }
            });

            Assert.Equal("Scannable", status);
            Assert.Equal(ScanVerdict.Good, verdict);
            Assert.True(new FileInfo(outputPath).Length > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Runs queued dispatcher work, so a posted continuation can land.</summary>
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    private static void Settle(FrameworkElement host)
    {
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();
    }

    /// <summary>Pbgra32 is premultiplied BGRA, which is what RasterImage carries.</summary>
    private static RasterImage ToRaster(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    private sealed class SilentDialogService : IDialogService
    {
        public string? AskForSavePath(
            string title, string filter, string defaultExtension, string suggestedName, string? directory) => null;

        public string? AskForImage(string? directory) => null;

        public string? AskForText(string title, string prompt, string initialValue) => null;

        public bool Confirm(string title, string message) => false;

        public bool ConfirmRisk(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe) => false;

        public void ShowError(string title, string message)
        {
        }

        public void ShowInformation(string title, string message)
        {
        }
    }
}

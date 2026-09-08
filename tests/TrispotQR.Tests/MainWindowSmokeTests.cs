using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App;
using TrispotQR.App.Services;
using TrispotQR.App.ViewModels;
using TrispotQR.Core.Export;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Rendering;
using TrispotQR.ViewModels;

namespace TrispotQR.Tests;

/// <summary>
/// Lays the real window out and renders it.
///
/// This is the test that catches the mistakes unit tests never see: a misspelled binding
/// path, a missing StaticResource, a converter wired to the wrong type. WPF swallows all
/// of those at runtime, showing an empty control and writing a line to a trace listener
/// nobody reads, so the listener is checked here and a binding error fails the build.
/// </summary>
[Collection("UI")]
public class MainWindowSmokeTests
{
    private readonly WpfHost _host;

    public MainWindowSmokeTests(WpfHost host) => _host = host;

    private const double Width = 1180;
    private const double DefaultHeight = 800;

    /// <summary>
    /// The advanced panel is rendered tall enough that nothing sits below the scroll fold,
    /// so its snapshot shows every control rather than the first screenful.
    /// </summary>
    private const double ExpandedHeight = 1500;

    /// <summary>
    /// The advanced panel is collapsed by default, so its XAML is never exercised by the
    /// plain layout test. Expanding it here means a bad binding or a missing resource in
    /// that section cannot hide behind the expander.
    /// </summary>
    [Fact]
    public void AdvancedPanel_LaysOutAndRendersWithNoBindingErrors() =>
        RenderWindow("ui-snapshot-advanced.png", expandAdvanced: true, dark: false);

    [Fact]
    public void MainWindow_LaysOutAndRendersWithNoBindingErrors() =>
        RenderWindow("ui-snapshot.png", expandAdvanced: false, dark: false);

    /// <summary>
    /// Dark mode is a whole second palette, and nothing else in the suite would notice if
    /// a colour in it were missing or unreadable.
    /// </summary>
    [Fact]
    public void MainWindow_RendersInDarkMode() =>
        RenderWindow("ui-snapshot-dark.png", expandAdvanced: true, dark: true);

    private void RenderWindow(string snapshotName, bool expandAdvanced, bool dark)
    {
        var height = expandAdvanced ? ExpandedHeight : DefaultHeight;
        var directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        // Left next to the test binary rather than in the temp folder, so there is always
        // a picture of what the UI last looked like to compare against by eye.
        var outputPath = Path.Combine(AppContext.BaseDirectory, snapshotName);

        try
        {
            var (errors, nonBlank, previewShown) = _host.Run(() =>
            {
                ThemeManager.Apply(
                    dark ? AppTheme.Dark : AppTheme.Light);

                var listener = new CollectingTraceListener();
                PresentationTraceSources.Refresh();
                PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error | SourceLevels.Warning;

                try
                {
                    var viewModel = new MainViewModel(
                        new NullDialogService(),
                        new PresetStore(directory),
                        new AppSettingsStore(directory));

                    // The app opens with an empty content box, so the preview has to be
                    // given something before there is anything to assert about it.
                    ((PlainTextEditor)viewModel.ContentEditors[0]).Text = "https://www.example.org";
                    viewModel.RefreshNow();

                    var window = new MainWindow(viewModel);

                    // Looked up by name because the generated field is internal to the app
                    // assembly. Captured before the content is detached.
                    var preview = (Image)window.FindName("PreviewImage");

                    // Rendered through a detached host rather than by showing the window,
                    // so the layout can be exercised without a visible desktop.
                    var content = (UIElement)window.Content;
                    window.Content = null;

                    var host = new Border
                    {
                        Background = window.Background,
                        Width = Width,
                        Height = height,
                        Child = content,

                        // Detaching the content from the window also detaches it from the
                        // window's DataContext, and bindings against a null DataContext
                        // fail silently. Without this the whole check would pass while
                        // testing nothing.
                        DataContext = viewModel,

                        // Resource lookup walks up the tree, so the detached content can no
                        // longer see Window.Resources. The implicit DataTemplates for the
                        // content editors live there and would silently not apply.
                        Resources = window.Resources,
                    };

                    host.Measure(new Size(Width, height));
                    host.Arrange(new Rect(0, 0, Width, height));
                    host.UpdateLayout();

                    if (expandAdvanced)
                    {
                        // The toast is transparent until an animation runs, and there is no
                        // dispatcher pump here to run one. Forcing it visible puts it in the
                        // snapshot so its layout is checked like everything else.
                        var toast = (Border)window.FindName("Toast");
                        ((TextBlock)window.FindName("ToastText")).Text = "Copied to clipboard";
                        toast.Opacity = 1;

                        // Applying a styled preset at the same time so the advanced
                        // controls render against something other than the defaults.
                        viewModel.ApplyPresetCommand.Execute(viewModel.Presets.Single(p => p.Name == "Two-tone"));
                        viewModel.UseCustomMarkerColors = true;
                        viewModel.OutlineEnabled = true;
                        viewModel.RefreshNow();

                        Expand(host);

                        host.Measure(new Size(Width, height));
                        host.Arrange(new Rect(0, 0, Width, height));
                        host.UpdateLayout();
                    }

                    var bitmap = new RenderTargetBitmap((int)Width, (int)height, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(host);
                    PngExporter.Save(ToRasterImage(bitmap), outputPath);

                    return (
                        listener.Messages.Concat(UntemplatedObjects(host)).ToList(),
                        HasVariedContent(bitmap),
                        preview.Source is not null);
                }
                finally
                {
                    ThemeManager.Apply(AppTheme.Light);
                    PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                }
            });

            Assert.True(errors.Count == 0, "WPF reported binding problems:\n" + string.Join("\n", errors));
            Assert.True(nonBlank, "the window rendered as a flat blank image, so the layout did not run");
            Assert.True(previewShown, "the preview image had no source, so the bindings were not live");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>The snapshot bitmap is always rendered as Pbgra32, which is already premultiplied BGRA.</summary>
    private static RasterImage ToRasterImage(RenderTargetBitmap bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return new RasterImage(bitmap.PixelWidth, bitmap.PixelHeight, pixels);
    }

    /// <summary>Opens every expander found in the tree.</summary>
    private static void Expand(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Expander expander)
            {
                expander.IsExpanded = true;
            }

            Expand(child);
        }
    }

    /// <summary>
    /// Finds text that is a bare .NET type name. When a DataTemplate does not resolve, WPF
    /// quietly falls back to ToString() on the object, so "TrispotQR.App.ViewModels.Something"
    /// appears where a form should be. Nothing is logged, which makes it easy to ship.
    /// </summary>
    private static IEnumerable<string> UntemplatedObjects(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is TextBlock { Text: var text }
                && text.StartsWith("TrispotQR.", StringComparison.Ordinal))
            {
                yield return $"A DataTemplate did not resolve; the raw object \"{text}\" is on screen.";
            }

            foreach (var found in UntemplatedObjects(child))
            {
                yield return found;
            }
        }
    }

    /// <summary>
    /// Confirms the render is a real UI rather than one flat colour, by sampling pixels
    /// across the image and checking they are not all identical.
    /// </summary>
    private static bool HasVariedContent(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        var stride = converted.PixelWidth * 3;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        var distinct = new HashSet<int>();

        for (var i = 0; i < pixels.Length - 3; i += 3 * 97)
        {
            distinct.Add((pixels[i] << 16) | (pixels[i + 1] << 8) | pixels[i + 2]);

            if (distinct.Count > 12)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CollectingTraceListener : TraceListener
    {
        public List<string> Messages { get; } = [];

        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (!string.IsNullOrWhiteSpace(message))
            {
                Messages.Add(message);
            }
        }
    }

    private sealed class NullDialogService : IDialogService
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


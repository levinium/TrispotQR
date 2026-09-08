using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrispotQR.App;
using TrispotQR.App.Services;
using TrispotQR.App.ViewModels;
using TrispotQR.App.Views;
using TrispotQR.Core.Presets;
using TrispotQR.ViewModels;

namespace TrispotQR.Tests;

/// <summary>
/// Every slider must move to the point that was clicked.
///
/// WPF defaults <see cref="Slider.IsMoveToPointEnabled"/> to false, which pages the value
/// by LargeChange on a track click and keeps paging while the button is held. That reads as
/// jumpy, and it is inconsistent with the colour picker's hue strip and shade square, which
/// are hand written and have always moved to the click.
///
/// These walk the real visual trees rather than reading the theme file, so a slider added
/// later with its own style, or one that lands somewhere the implicit style cannot reach,
/// still gets caught.
/// </summary>
[Collection("UI")]
public class SliderBehaviourTests
{
    private readonly WpfHost _host;

    public SliderBehaviourTests(WpfHost host) => _host = host;

    [Fact]
    public void EverySliderInTheMainWindow_MovesToTheClickedPoint()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"trispotqr-slider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var offenders = _host.Run(() =>
            {
                var viewModel = new MainViewModel(
                    new NullDialogs(), new PresetStore(directory), new AppSettingsStore(directory));

                var window = new MainWindow(viewModel);
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.DataContext = viewModel;
                content.Resources = window.Resources;

                content.Measure(new Size(1180, 1600));
                content.Arrange(new Rect(0, 0, 1180, 1600));
                content.UpdateLayout();

                // The sliders live behind the Advanced expander, so it has to be open for
                // them to exist in the tree at all.
                ExpandAll(content);
                content.Measure(new Size(1180, 1600));
                content.Arrange(new Rect(0, 0, 1180, 1600));
                content.UpdateLayout();

                return Offenders(content);
            });

            Assert.True(offenders.Count == 0,
                "these sliders still page instead of moving to the click: " + string.Join(", ", offenders));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TheColourPickersRgbSliders_MoveToTheClickedPoint()
    {
        var offenders = _host.Run(() =>
        {
            var picker = new ColorPicker();
            var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("PickerPopup");
            var content = (FrameworkElement)popup.Child;
            popup.Child = null;

            content.Measure(new Size(400, 700));
            content.Arrange(new Rect(0, 0, 400, 700));
            content.UpdateLayout();

            return Offenders(content);
        });

        Assert.True(offenders.Count == 0,
            "these sliders still page instead of moving to the click: " + string.Join(", ", offenders));
    }

    [Fact]
    public void BothTreesActuallyContainSliders()
    {
        // Guards the two tests above against quietly passing because they walked a tree
        // with no sliders in it, which is exactly what would happen if the Advanced
        // expander failed to open.
        var directory = Path.Combine(Path.GetTempPath(), $"trispotqr-slider-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            var (inPicker, inWindow) = _host.Run(() =>
            {
                var picker = new ColorPicker();
                var popup = (System.Windows.Controls.Primitives.Popup)picker.FindName("PickerPopup");
                var pickerContent = (FrameworkElement)popup.Child;
                popup.Child = null;
                pickerContent.Measure(new Size(400, 700));
                pickerContent.Arrange(new Rect(0, 0, 400, 700));
                pickerContent.UpdateLayout();

                var viewModel = new MainViewModel(
                    new NullDialogs(), new PresetStore(directory), new AppSettingsStore(directory));
                var window = new MainWindow(viewModel);
                var content = (FrameworkElement)window.Content;
                window.Content = null;
                content.DataContext = viewModel;
                content.Resources = window.Resources;
                content.Measure(new Size(1180, 1600));
                content.Arrange(new Rect(0, 0, 1180, 1600));
                content.UpdateLayout();
                ExpandAll(content);
                content.Measure(new Size(1180, 1600));
                content.Arrange(new Rect(0, 0, 1180, 1600));
                content.UpdateLayout();

                return (Count(pickerContent), Count(content));
            });

            Assert.Equal(3, inPicker);   // red, green, blue
            Assert.Equal(4, inWindow);   // dot gap, outline thickness, logo size, margin
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static List<string> Offenders(DependencyObject root)
    {
        var found = new List<string>();

        foreach (var slider in Sliders(root))
        {
            if (!slider.IsMoveToPointEnabled)
            {
                found.Add(string.IsNullOrEmpty(slider.Name) ? $"unnamed ({slider.Minimum}-{slider.Maximum})" : slider.Name);
            }
        }

        return found;
    }

    private static int Count(DependencyObject root) => Sliders(root).Count();

    private static IEnumerable<Slider> Sliders(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Slider slider)
            {
                yield return slider;
            }

            foreach (var nested in Sliders(child))
            {
                yield return nested;
            }
        }
    }

    private static void ExpandAll(DependencyObject root)
    {
        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is Expander expander)
            {
                expander.IsExpanded = true;
            }

            ExpandAll(child);
        }
    }

    private sealed class NullDialogs : IDialogService
    {
        public string? AskForSavePath(string t, string f, string e, string n, string? d) => null;

        public string? AskForImage(string? directory) => null;

        public string? AskForText(string title, string prompt, string initialValue) => null;

        public bool Confirm(string title, string message) => false;

        public bool ConfirmRisk(string h, string m, string p, bool d, bool s) => false;

        public void ShowError(string title, string message)
        {
        }

        public void ShowInformation(string title, string message)
        {
        }

        public AppSettings? EditSettings(AppSettings current) => null;
    }
}

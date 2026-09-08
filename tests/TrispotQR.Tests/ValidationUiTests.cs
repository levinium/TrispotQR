using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TrispotQR.App;
using TrispotQR.App.Export;
using TrispotQR.App.Services;
using TrispotQR.App.Views;
using TrispotQR.Core.Export;
using TrispotQR.Core.Payloads;
using TrispotQR.Core.Presets;
using TrispotQR.Core.Rendering;
using TrispotQR.ViewModels;

namespace TrispotQR.Tests;

/// <summary>
/// The red highlighting, end to end through the real window.
///
/// TrispotQR.ViewModels.Tests.EditorValidationTests proves the editors know what is wrong.
/// What is left, and what only a rendered tree can answer, is whether that reaches the
/// screen: whether the box at fault is the one that turns red, and whether the message is
/// legible once it does.
/// </summary>
[Collection("UI")]
public class ValidationUiTests
{
    private readonly WpfHost _host;

    public ValidationUiTests(WpfHost host) => _host = host;

    public static TheoryData<AppTheme> Themes => new() { AppTheme.Light, AppTheme.Dark };

    /// <summary>
    /// A deliberately wrong value for each content type, paired with the field that should
    /// be blamed for it. Every type is here so a new one cannot be added without deciding
    /// what its bad input looks like.
    /// </summary>
    public static TheoryData<string, string> BadInput => new()
    {
        { "Link", nameof(LinkEditor.Address) },
        { "Wi-Fi", nameof(WifiEditor.Password) },
        { "Email", nameof(EmailEditor.Address) },
        { "Phone", nameof(PhoneEditor.Number) },
        { "Text message", nameof(SmsEditor.Number) },
        { "Contact card", nameof(ContactEditor.Email) },
    };

    [Theory]
    [MemberData(nameof(BadInput))]
    public void TheFieldAtFault_IsTheOneMarked(string title, string expectedField)
    {
        var marked = WithWindow(AppTheme.Light, (viewModel, host) =>
        {
            var editor = viewModel.ContentEditors.Single(e => e.Title == title);
            viewModel.SelectedContent = editor;
            Fill(editor);
            Break(editor);
            Settle(host);

            return Boxes(host)
                .Where(b => FieldState.GetHasError(Input(b)))
                .Select(b => b.FieldName)
                .ToList();
        });

        Assert.Equal([expectedField], marked);
    }

    [Theory]
    [MemberData(nameof(BadInput))]
    public void TheMessage_AppearsUnderThatField(string title, string expectedField)
    {
        var shown = WithWindow(AppTheme.Light, (viewModel, host) =>
        {
            var editor = viewModel.ContentEditors.Single(e => e.Title == title);
            viewModel.SelectedContent = editor;
            Fill(editor);
            Break(editor);
            Settle(host);

            return Boxes(host)
                .Where(b => b.ShownError is not null)
                .Select(b => (b.FieldName, b.ShownError))
                .ToList();
        });

        var one = Assert.Single(shown);
        Assert.Equal(expectedField, one.FieldName);
        Assert.False(string.IsNullOrWhiteSpace(one.ShownError));
    }

    /// <summary>
    /// Correcting the input has to clear the marking. A red border that never goes away is
    /// worse than none, because it stops meaning anything.
    /// </summary>
    [Fact]
    public void FixingTheInput_ClearsTheMarking()
    {
        var (broken, fixedUp) = WithWindow(AppTheme.Light, (viewModel, host) =>
        {
            var editor = viewModel.ContentEditors.OfType<EmailEditor>().Single();
            viewModel.SelectedContent = editor;

            editor.Address = "nonsense";
            Settle(host);
            var before = Boxes(host).Count(b => FieldState.GetHasError(Input(b)));

            editor.Address = "someone@example.org";
            Settle(host);
            var after = Boxes(host).Count(b => FieldState.GetHasError(Input(b)));

            return (before, after);
        });

        Assert.Equal(1, broken);
        Assert.Equal(0, fixedUp);
    }

    /// <summary>
    /// A warning marks the field but must not block the save, which is the whole difference
    /// between the two severities.
    /// </summary>
    [Fact]
    public void AWarning_MarksTheFieldWithoutBlockingTheSave()
    {
        var (marked, canExport) = WithWindow(AppTheme.Light, (viewModel, host) =>
        {
            var wifi = viewModel.ContentEditors.OfType<WifiEditor>().Single();
            viewModel.SelectedContent = wifi;
            wifi.Ssid = "Guest";
            wifi.Security = WifiSecurity.Wep;
            wifi.Password = "abcdefg";
            viewModel.RefreshNow();
            Settle(host);

            var box = Boxes(host).Single(b => b.FieldName == nameof(WifiEditor.Password));
            return (FieldState.GetHasWarning(Input(box)), viewModel.CanExport);
        });

        Assert.True(marked, "the odd WEP key was not marked at all");
        Assert.True(canExport, "a warning blocked the save, which only an error should do");
    }

    /// <summary>
    /// The red has to be readable on the card behind it in both themes. Light theme red on
    /// a white card is easy; the dark theme is where a colour copied from the light palette
    /// turns into unreadable brown.
    /// </summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void TheMessage_IsReadableInBothThemes(AppTheme theme)
    {
        var (ratio, colours) = WithWindow(theme, (viewModel, host) =>
        {
            var editor = viewModel.ContentEditors.OfType<EmailEditor>().Single();
            viewModel.SelectedContent = editor;
            editor.Address = "nonsense";
            Settle(host);

            var box = Boxes(host).Single(b => b.ShownError is not null);
            var text = Message(box);

            var foreground = ((SolidColorBrush)text.Foreground).Color;
            var background = ((SolidColorBrush)Application.Current.Resources["SurfaceBrush"]).Color;

            return (Contrast(foreground, background),
                $"#{foreground.R:X2}{foreground.G:X2}{foreground.B:X2} on " +
                $"#{background.R:X2}{background.G:X2}{background.B:X2}");
        });

        Assert.True(ratio >= 3.0, $"the error message in {theme} mode is {ratio:0.0}:1 ({colours})");
    }

    /// <summary>
    /// The state the app opens in. Required fields report themselves straight away rather
    /// than waiting to be visited, which is a deliberate choice: the cost is that a
    /// pristine form is already marked, and this is where that shows up if it is ever
    /// reconsidered.
    /// </summary>
    [Fact]
    public void TheOpeningForm_AlreadyShowsWhatIsMissing()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "validation-launch.png");

        var shown = WithWindow(AppTheme.Light, (viewModel, host) =>
        {
            var bitmap = new RenderTargetBitmap(
                (int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            PngExporter.Save(ToRasterImage(bitmap), path);

            return Boxes(host).Select(b => b.ShownError).ToList();
        });

        Assert.Equal(["Enter the text to put in the code."], shown);
    }

    /// <summary>Leaves a picture of a form in its failed state next to the test binary.</summary>
    [Theory]
    [MemberData(nameof(Themes))]
    public void AFailedForm_Renders(AppTheme theme)
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"validation-{theme}.png".ToLowerInvariant());

        var varied = WithWindow(theme, (viewModel, host) =>
        {
            var editor = viewModel.ContentEditors.OfType<ContactEditor>().Single();
            viewModel.SelectedContent = editor;
            editor.JobTitle = "Director of Music";
            editor.Email = "not-an-address";
            editor.Phone = "abc";
            viewModel.RefreshNow();
            Settle(host);

            var bitmap = new RenderTargetBitmap(
                (int)host.Width, (int)host.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(host);
            PngExporter.Save(ToRasterImage(bitmap), path);
            return true;
        });

        Assert.True(varied);
        Assert.True(new FileInfo(path).Length > 0);
    }

    /// <summary>
    /// The visible field boxes in the tree. Visibility is read from the property rather
    /// than from IsVisible, which is false for everything here: IsVisible needs the tree to
    /// be connected to a live presentation source, and this one is deliberately detached.
    /// </summary>
    private static IEnumerable<FieldBox> Boxes(DependencyObject root)
    {
        if (root is FieldBox box && box.Visibility == Visibility.Visible)
        {
            yield return box;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);

        for (var i = 0; i < count; i++)
        {
            foreach (var found in Boxes(VisualTreeHelper.GetChild(root, i)))
            {
                yield return found;
            }
        }
    }

    private static TextBox Input(FieldBox box) => (TextBox)box.FindName("Input");

    private static TextBlock Message(FieldBox box) => (TextBlock)box.FindName("ErrorText");

    /// <summary>Gives the content type enough valid input that only the broken field is wrong.</summary>
    private static void Fill(ContentEditor editor)
    {
        switch (editor)
        {
            case LinkEditor link:
                link.Address = "example.org";
                break;
            case WifiEditor wifi:
                wifi.Ssid = "Guest";
                wifi.Password = "longenough";
                break;
            case EmailEditor email:
                email.Address = "someone@example.org";
                break;
            case PhoneEditor phone:
                phone.Number = "212 555 0134";
                break;
            case SmsEditor sms:
                sms.Number = "212 555 0134";
                break;
            case ContactEditor contact:
                contact.FirstName = "Alex";
                break;
        }
    }

    /// <summary>Breaks exactly one field, the one each case expects to be blamed.</summary>
    private static void Break(ContentEditor editor)
    {
        switch (editor)
        {
            case LinkEditor link:
                link.Address = "not a website";
                break;
            case WifiEditor wifi:
                wifi.Password = "short";
                break;
            case EmailEditor email:
                email.Address = "nonsense";
                break;
            case PhoneEditor phone:
                phone.Number = "212 555 CALL";
                break;
            case SmsEditor sms:
                sms.Number = "abc";
                break;
            case ContactEditor contact:
                contact.Email = "not-an-address";
                break;
        }
    }

    private static void Settle(FrameworkElement host)
    {
        host.Measure(new Size(host.Width, host.Height));
        host.Arrange(new Rect(0, 0, host.Width, host.Height));
        host.UpdateLayout();
    }

    /// <summary>
    /// Builds the real window, hands its detached content to <paramref name="work"/>, and
    /// puts the theme back. Same detaching as the smoke test, including carrying the
    /// DataContext and Window.Resources across, without which the content templates would
    /// not resolve and every assertion here would be vacuous.
    /// </summary>
    private T WithWindow<T>(AppTheme theme, Func<MainViewModel, Border, T> work)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"TrispotQR-validation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        try
        {
            return _host.Run(() =>
            {
                ThemeManager.Apply(theme);

                try
                {
                    var viewModel = new MainViewModel(
                        new NullDialogService(),
                        new WpfUiTimer(),
                        new WpfImageClipboard(),
                        new PresetStore(directory),
                        new AppSettingsStore(directory));

                    var window = new MainWindow(viewModel);
                    var content = (UIElement)window.Content;
                    window.Content = null;

                    var host = new Border
                    {
                        Background = window.Background,
                        Width = 1180,
                        Height = 900,
                        Child = content,
                        DataContext = viewModel,
                        Resources = window.Resources,
                    };

                    Settle(host);
                    return work(viewModel, host);
                }
                finally
                {
                    ThemeManager.Apply(AppTheme.Light);
                }
            });
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

    private static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        return (Math.Max(first, second) + 0.05) / (Math.Min(first, second) + 0.05);
    }

    private static double Luminance(Color c) =>
        (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));

    private static double Channel(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>Stands in for the file and message dialogs so the window can run headless.</summary>
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

        public AppSettings? EditSettings(AppSettings current) => null;
    }
}

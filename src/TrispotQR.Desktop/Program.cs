using Avalonia;
using TrispotQR.UI;

namespace TrispotQR.Desktop;

internal static class Program
{
    // Avalonia needs this on the main thread, before anything touches the toolkit.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Named exactly this because Avalonia's XAML previewer looks it up by convention.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}

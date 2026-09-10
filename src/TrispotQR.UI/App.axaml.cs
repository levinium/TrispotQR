using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;

namespace TrispotQR.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Only when there is a desktop lifetime. The headless test host has none, and creating
        // a main window there would open one window per test run.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Inside the guard, and before the window is constructed, so the app never flashes
            // light and then repaints itself dark. Inside rather than outside because the theme
            // is process wide static state read from the user's real settings.json: applying it
            // in the test host would make every appearance test start from whatever the
            // developer's own machine happens to be set to, and would read a file this suite
            // deliberately never touches. A headless host has no window to paint either way.
            ApplyStartupTheme(new AppSettingsStore());

            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Puts the remembered appearance into effect. Its own method, and taking the store rather
    /// than making one, so a test can hand it a settings folder of its own; the alternative is
    /// reading whichever theme the developer last chose for themselves and asserting nothing.
    /// </summary>
    internal static void ApplyStartupTheme(AppSettingsStore store) =>
        ThemeSwitcher.Apply(store.Load().Theme);
}

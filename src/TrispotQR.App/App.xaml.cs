using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TrispotQR.App.Services;
using TrispotQR.Core.Presets;

namespace TrispotQR.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A crash in a WPF app otherwise dies silently or behind an unreadable stack
        // trace. This at least tells the user what happened and keeps the window open.
        DispatcherUnhandledException += OnUnhandledException;

        // Applied before the first window is shown, so the app never flashes light and
        // then repaint itself dark.
        ThemeManager.Apply(new AppSettingsStore().Load().Theme);

        // Makes "Follow Windows" mean it: the setting is re-read whenever Windows says a
        // preference changed, rather than only at startup.
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color)
        {
            // Raised on a system thread, so it has to be marshalled before touching
            // application resources.
            Dispatcher.Invoke(ThemeManager.Refresh);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"Something went wrong:\n\n{e.Exception.Message}\n\nThe app will keep running, but if this "
            + "keeps happening please note what you were doing at the time.",
            "Trispot QR",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }
}

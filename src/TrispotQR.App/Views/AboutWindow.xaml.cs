using System.Windows;
using System.Windows.Media.Imaging;
using TrispotQR.App.Services;
using TrispotQR.Core.Presets;

namespace TrispotQR.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        VersionText.Text = $"Version {AppInfo.Version}";
        SettingsPath.Text = PresetStore.DefaultDirectory;

        // The window's own icon, so About cannot drift out of step with what the taskbar
        // shows. Icon can be null when the window has not been given one.
        AppIcon.Source = Icon ?? Application.Current?.MainWindow?.Icon as BitmapSource;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}

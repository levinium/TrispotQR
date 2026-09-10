using Avalonia.Controls;
using Avalonia.Interactivity;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Views;

/// <summary>
/// What the app is and which build of it this is.
///
/// The name and the version come from <see cref="AppInfo"/> rather than from the XAML, so
/// there is no second copy of either to fall out of step with the csproj.
/// </summary>
public partial class AboutWindow : Window
{
    // InitializeComponent(), never AvaloniaXamlLoader.Load(this) -- see the comment in
    // MessageWindow.axaml.cs. Load(this) populates the NameScope but leaves the generated
    // fields null, so the two assignments below would throw.
    public AboutWindow()
    {
        InitializeComponent();

        ProductText.Text = AppInfo.ProductName;
        VersionText.Text = AppInfo.DisplayVersion;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

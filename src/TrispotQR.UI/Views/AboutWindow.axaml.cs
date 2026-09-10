using Avalonia.Controls;
using Avalonia.Interactivity;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Services;

namespace TrispotQR.UI.Views;

/// <summary>
/// What the app is, which build of it this is, where it keeps its files, and what it is built
/// on.
///
/// The name and the version come from <see cref="AppInfo"/> and the folder from
/// <see cref="PresetStore.DefaultDirectory"/>, rather than from the XAML, so there is no second
/// copy of any of them to fall out of step with the thing that decides them.
///
/// The "Built with" card is a licence obligation rather than a courtesy: MIT and Apache 2.0
/// both require their notices to travel with the distribution, and this window is the only
/// place in the product where they appear.
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

        // The same property AppSettingsStore and PresetStore both fall back to when they are
        // constructed without a directory, which is how the app constructs them. Asking it
        // here is what makes this card the folder the app really uses rather than a guess at
        // it, on whichever platform this is running.
        SettingsPath.Text = PresetStore.DefaultDirectory;
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}

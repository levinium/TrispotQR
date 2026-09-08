using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TrispotQR.UI.Views;

/// <summary>
/// One window behind every message and confirmation the app shows.
///
/// A single window rather than one per message type, because the only differences are the
/// text and whether there is a second button. WPF gave this away for free as MessageBox;
/// Avalonia has no equivalent, by design, since there is no such thing on every platform.
/// </summary>
public partial class MessageWindow : Window
{
    private bool _confirmed;

    // InitializeComponent(), not AvaloniaXamlLoader.Load(this) directly (the brief's original
    // sketch, and this project's only other AvaloniaXamlLoader.Load call, in App.axaml.cs,
    // where it is harmless because Application has no named elements to populate). Confirmed
    // empirically: calling Load(this) here builds the visual tree and registers "MessageText"
    // etc. in the NameScope (FindControl finds them) but leaves the compiler-generated
    // MessageText/ConfirmButton/CancelButton fields null, because assigning those fields is
    // wired into InitializeComponent()'s own generated body (!XamlIlPopulateTrampoline), not
    // into the generic runtime loader. Skipping InitializeComponent() here meant ShowAsync's
    // very next line, window.MessageText.Text = message, threw NullReferenceException on
    // every call -- Confirm, ConfirmRisk, ShowError and ShowInformation were all broken.
    public MessageWindow() => InitializeComponent();

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string confirmLabel,
        string? cancelLabel,
        bool defaultToConfirm)
    {
        var window = new MessageWindow { Title = title };
        window.MessageText.Text = message;
        window.ConfirmButton.Content = confirmLabel;

        if (cancelLabel is not null)
        {
            window.CancelButton.Content = cancelLabel;
            window.CancelButton.IsVisible = true;
            window.CancelButton.IsDefault = !defaultToConfirm;
            window.ConfirmButton.IsDefault = defaultToConfirm;
        }

        await window.ShowDialog(owner);
        return window._confirmed;
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}

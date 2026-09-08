using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

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

    public MessageWindow() => AvaloniaXamlLoader.Load(this);

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

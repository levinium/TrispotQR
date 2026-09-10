using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TrispotQR.UI.Views;

/// <summary>
/// A single-line prompt, used for naming a saved style.
///
/// Its own window rather than a MessageWindow variant because it has to refuse an answer and
/// stay open, which a message box has no vocabulary for.
/// </summary>
public partial class TextPromptWindow : Window
{
    private bool _confirmed;

    // InitializeComponent(), never AvaloniaXamlLoader.Load(this) -- see the comment in
    // MessageWindow.axaml.cs. Load(this) populates the NameScope but leaves the generated
    // fields null, so PromptText.Text on the next line would throw.
    public TextPromptWindow() => InitializeComponent();

    public TextPromptWindow(string title, string prompt, string initialValue)
        : this()
    {
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initialValue;
        Input.TextChanged += OnTextChanged;

        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>The trimmed name. Meaningful only once the window has closed with Save.</summary>
    public string Value => (Input.Text ?? string.Empty).Trim();

    public static async Task<string?> ShowAsync(Window owner, string title, string prompt, string initialValue)
    {
        var window = new TextPromptWindow(title, prompt, initialValue);
        await window.ShowDialog(owner);
        return window._confirmed ? window.Value : null;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Only ever clears. A complaint that outlives the thing it complained about reads as
        // a bug; one that appears before the user has finished typing reads as nagging.
        if (Value.Length > 0)
        {
            ErrorText.IsVisible = false;
        }
    }

    private void OnConfirm(object? sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            ErrorText.Text = "Please enter a name.";
            ErrorText.IsVisible = true;
            Input.Focus();
            return;
        }

        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _confirmed = false;
        Close();
    }
}

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

    /// <summary>
    /// Opens on an empty prompt, and exists so the compiled XAML stays reachable from the
    /// runtime loader: without a public parameterless constructor the Avalonia compiler reports
    /// AVLN3001 and the resource cannot be loaded that way at all.
    ///
    /// Carries the wiring rather than leaving it to the overload, for the same reason
    /// SettingsWindow's parameterless constructor delegates to its real one: what a window does
    /// belongs to the window, not to the arguments it happened to be given. Without this a
    /// designer- or loader-built prompt would refuse an empty name and then never take the
    /// complaint back down, which is the one behaviour this window exists for.
    ///
    /// InitializeComponent(), never AvaloniaXamlLoader.Load(this) -- see the comment in
    /// MessageWindow.axaml.cs. Load(this) populates the NameScope but leaves the generated
    /// fields null, so Input on the next line would be null.
    /// </summary>
    public TextPromptWindow()
    {
        InitializeComponent();

        Input.TextChanged += OnTextChanged;

        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public TextPromptWindow(string title, string prompt, string initialValue)
        : this()
    {
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initialValue;
    }

    /// <summary>
    /// The trimmed contents of the box, read live. This is what Save is judged on and what the
    /// error clearing watches, both while the window is open; ShowAsync is the one caller that
    /// waits for the close, and it is also the one that decides whether to hand the value back
    /// at all.
    /// </summary>
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

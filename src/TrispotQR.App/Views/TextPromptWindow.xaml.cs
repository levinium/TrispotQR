using System.Windows;

namespace TrispotQR.App.Views;

/// <summary>A single-line prompt, used for naming a saved style.</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initialValue)
    {
        InitializeComponent();

        Title = title;
        PromptText.Text = prompt;
        Input.Text = initialValue;

        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    public string Value => Input.Text.Trim();

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        if (Value.Length == 0)
        {
            ErrorText.Text = "Please enter a name.";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        DialogResult = true;
    }
}

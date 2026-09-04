using System.Windows;
using System.Windows.Media;

namespace TrispotQR.App.Views;

/// <summary>
/// A confirmation with buttons that say what they will do.
///
/// A plain Yes/No message box would be shorter, but "Yes" is a poor label for a decision
/// like carrying on with a code that did not scan. Naming the action on the button is what
/// makes the choice legible at the moment it is made.
/// </summary>
public partial class ConfirmWindow : Window
{
    /// <param name="defaultToProceed">
    /// Which button has focus when the dialog opens, and therefore what Enter does. False
    /// for a code that did not scan at all, so the safe answer is the one already selected.
    /// </param>
    public ConfirmWindow(string heading, string message, string proceedLabel, bool defaultToProceed, bool severe)
    {
        InitializeComponent();

        HeadingText.Text = heading;
        MessageText.Text = message;
        ProceedButton.Content = proceedLabel;

        Badge.Fill = new SolidColorBrush(severe
            ? Color.FromRgb(0xC5, 0x22, 0x1F)
            : Color.FromRgb(0xE3, 0x8C, 0x00));

        Loaded += (_, _) =>
        {
            if (defaultToProceed)
            {
                ProceedButton.Focus();
            }
            else
            {
                CancelButton.Focus();
            }
        };
    }

    private void OnProceed(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}

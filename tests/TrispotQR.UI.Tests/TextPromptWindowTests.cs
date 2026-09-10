using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TrispotQR.UI.Views;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The prompt behind "save this style as...". Its whole job is to refuse an empty name
/// without closing, which is the one thing a MessageWindow could not have done.
/// </summary>
public class TextPromptWindowTests
{
    [AvaloniaFact]
    public void TheInitialValueIsShownAndSelectedSoTypingReplacesIt()
    {
        WithPrompt("My style", window =>
        {
            var input = Input(window);
            Assert.Equal("My style", input.Text);
            Assert.Equal(0, input.SelectionStart);
            Assert.Equal("My style".Length, input.SelectionEnd);
        });
    }

    [AvaloniaFact]
    public void AnEmptyNameKeepsTheWindowOpenAndSaysWhy()
    {
        WithPrompt(string.Empty, window =>
        {
            Input(window).Text = string.Empty;
            Confirm(window);

            var error = window.FindControl<TextBlock>("ErrorText")!;
            Assert.True(error.IsVisible, "an empty name must explain itself rather than closing silently");
            Assert.Contains("name", error.Text, StringComparison.OrdinalIgnoreCase);
            Assert.True(window.IsVisible, "the window must stay open so the name can be corrected");
        });
    }

    [AvaloniaFact]
    public void AWhitespaceOnlyNameIsTreatedAsEmpty()
    {
        WithPrompt(string.Empty, window =>
        {
            Input(window).Text = "   ";
            Confirm(window);

            Assert.True(window.FindControl<TextBlock>("ErrorText")!.IsVisible);
            Assert.True(window.IsVisible);
        });
    }

    [AvaloniaFact]
    public void TheErrorClearsOnceAUsableNameIsTyped()
    {
        WithPrompt(string.Empty, window =>
        {
            Input(window).Text = string.Empty;
            Confirm(window);
            Assert.True(window.FindControl<TextBlock>("ErrorText")!.IsVisible);

            Input(window).Text = "Poster";
            DispatcherPump.Drain();

            Assert.False(
                window.FindControl<TextBlock>("ErrorText")!.IsVisible,
                "the complaint must go away when it stops being true");
        });
    }

    [AvaloniaFact]
    public void TheNameIsTrimmedOnTheWayOut()
    {
        // Guards against a saved preset named "  Poster " that no later lookup can match.
        WithPrompt(string.Empty, window =>
        {
            Input(window).Text = "  Poster  ";
            Confirm(window);
            Assert.Equal("Poster", window.Value);
        });
    }

    [AvaloniaFact]
    public void TheNameCannotExceedSixtyCharacters()
    {
        WithPrompt(string.Empty, window => Assert.Equal(60, Input(window).MaxLength));
    }

    private static TextBox Input(TextPromptWindow window) =>
        window.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Input");

    private static void Confirm(TextPromptWindow window)
    {
        var button = window.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ConfirmButton");
        var point = UiHarness.At(button, 0.5, 0.5);
        Assert.True(
            point.X >= 0 && point.Y >= 0 && point.X < window.Width && point.Y < window.Height,
            "a click outside the window is discarded silently");
        UiHarness.Click(window, point);
    }

    /// <summary>
    /// Shows the window non-modally so the body can inspect it. ShowDialog would block on a
    /// dispatcher frame and never hand control back.
    /// </summary>
    private static void WithPrompt(string initialValue, Action<TextPromptWindow> work)
    {
        var window = new TextPromptWindow("Trispot QR", "Name this style", initialValue);
        window.Show();
        DispatcherPump.Drain();
        work(window);
    }
}

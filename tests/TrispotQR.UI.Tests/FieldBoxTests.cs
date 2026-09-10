using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrispotQR.Core.Payloads;
using TrispotQR.Core.Presets;
using TrispotQR.UI.Controls;
using TrispotQR.UI.Services;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

public class FieldBoxTests
{
    private static (Window Window, FieldBox Box) Show(ContentEditor editor, string fieldName, string label = "Thing")
    {
        var box = new FieldBox { Label = label, FieldName = fieldName, DataContext = editor };
        var window = new Window { Width = 400, Height = 200, Content = box };
        window.Show();
        DispatcherPump.Drain();
        return (window, box);
    }

    // Reads the brush from Application.Resources rather than hard-coding a hex value, so
    // this assertion keeps following the palette if Phase 2e's theming changes the colors.
    private static Color SeverityColor(string key)
    {
        Application.Current!.TryGetResource(key, ThemeVariant.Default, out var resource);
        return ((ISolidColorBrush)resource!).Color;
    }

    [AvaloniaFact]
    public void AValidFieldShowsNoMessage()
    {
        var editor = new LinkEditor { Address = "www.example.org" };

        var (_, box) = Show(editor, "Address", "Web address");

        Assert.Null(box.ShownError);
        Assert.False(box.Classes.Contains(":error"));
    }

    [AvaloniaFact]
    public void AFieldAtFaultShowsItsMessageAndMarksItself()
    {
        // Empty is an error for a link: there is nothing to encode.
        var editor = new LinkEditor { Address = string.Empty };

        var (_, box) = Show(editor, "Address", "Web address");

        Assert.NotNull(box.ShownError);
        Assert.Equal(editor.IssueFor("Address")!.Message, box.ShownError);
        Assert.True(box.Classes.Contains(":error"));

        var borderBrush = UiHarness.Input(box).BorderBrush as ISolidColorBrush;
        Assert.NotNull(borderBrush);
        Assert.Equal(SeverityColor("DangerBrush"), borderBrush!.Color);
    }

    [AvaloniaFact]
    public void FixingTheInputClearsTheMarking()
    {
        var editor = new LinkEditor { Address = string.Empty };
        var (_, box) = Show(editor, "Address", "Web address");
        Assert.True(box.Classes.Contains(":error"));

        editor.Address = "www.example.org";
        DispatcherPump.Drain();

        Assert.Null(box.ShownError);
        Assert.False(box.Classes.Contains(":error"));
    }

    [AvaloniaFact]
    public void AWarningMarksTheFieldDifferentlyFromAnError()
    {
        // A WEP key of odd length: usable, so not an error, but odd enough to say so. This
        // is the same case the WPF suite uses, which is where the warning rule is proven.
        var editor = new WifiEditor { Ssid = "Guest", Security = WifiSecurity.Wep, Password = "abcdefg" };
        var warned = editor.Issues.SingleOrDefault(i => i.Severity == IssueSeverity.Warning);
        Assert.NotNull(warned);

        var (_, box) = Show(editor, warned!.Field);

        Assert.Equal(warned.Message, box.ShownError);
        Assert.True(box.Classes.Contains(":warning"));
        Assert.False(box.Classes.Contains(":error"));

        var borderBrush = UiHarness.Input(box).BorderBrush as ISolidColorBrush;
        Assert.NotNull(borderBrush);
        Assert.Equal(SeverityColor("WarningBrush"), borderBrush!.Color);

        // The message is coloured by a style of its own, so it can be missing while the border
        // beside it is right, which would leave a warning written in ordinary body text.
        var message = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ErrorText");
        Assert.Equal(
            VariantColor("WarningBrush", Application.Current!.ActualThemeVariant),
            (message.Foreground as ISolidColorBrush)?.Color);
    }

    [AvaloniaFact]
    public void TheOptionalSuffixIsAddedOnceRatherThanAtEveryCallSite()
    {
        var editor = new EmailEditor { Address = "someone@example.com" };

        var (_, required) = Show(editor, "Address", "To");
        var optional = new FieldBox { Label = "Subject", FieldName = "Subject", IsOptional = true, DataContext = editor };
        var window = new Window { Width = 400, Height = 200, Content = optional };
        window.Show();
        DispatcherPump.Drain();

        Assert.Equal("To", required.ShownLabel);
        Assert.Equal("Subject (optional)", optional.ShownLabel);
    }

    [AvaloniaFact]
    public void AHeightTurnsTheBoxIntoAMultiLineOne()
    {
        var editor = new EmailEditor { Address = "someone@example.com" };

        var (_, box) = Show(editor, "Body", "Message");
        Assert.False(box.AcceptsReturn);

        var tall = new FieldBox { Label = "Message", FieldName = "Body", InputHeight = 60, DataContext = editor };
        var window = new Window { Width = 400, Height = 200, Content = tall };
        window.Show();
        DispatcherPump.Drain();

        Assert.True(tall.AcceptsReturn);
    }

    [AvaloniaFact]
    public void DetachesFromAnEditorItNoLongerShows()
    {
        // The control subscribes to its editor's PropertyChanged. If it never unsubscribes,
        // every content-type switch leaves a live handler on a discarded editor, and the
        // leak is invisible until something profiles it.
        var first = new LinkEditor { Address = string.Empty };
        var (_, box) = Show(first, "Address", "Web address");
        Assert.True(box.Classes.Contains(":error"));

        box.DataContext = new LinkEditor { Address = "www.example.org" };
        DispatcherPump.Drain();
        Assert.False(box.Classes.Contains(":error"));

        // Changing the editor it is no longer showing must not bring the marking back.
        first.Address = "still broken?";
        first.Address = string.Empty;
        DispatcherPump.Drain();

        Assert.False(box.Classes.Contains(":error"));
    }

    [AvaloniaFact]
    public void TheMessageUnderTheBoxTakesTheDangerColourOfTheThemeThatIsShowing()
    {
        // The border and the message have to agree. They did not: the border came from a style
        // and followed the variant, while the message was assigned once from a lookup that
        // named ThemeVariant.Default, so a dark window drew the light theme's #C62828 text
        // inside a dark #FF7B7B border, which is the muddy result the dark palette exists to
        // avoid. Both halves of that are checked here: the right colour to begin with, and the
        // right colour again after the appearance changes under an open window.
        var editor = new LinkEditor { Address = string.Empty };

        try
        {
            ThemeSwitcher.Apply(AppTheme.Light);
            var (_, box) = Show(editor, "Address", "Web address");

            var message = box.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Name == "ErrorText");
            Assert.Equal(editor.IssueFor("Address")!.Message, message.Text);
            Assert.Equal(
                VariantColor("DangerBrush", ThemeVariant.Light),
                (message.Foreground as ISolidColorBrush)?.Color);

            ThemeSwitcher.Apply(AppTheme.Dark);
            DispatcherPump.Drain();

            Assert.Equal(
                VariantColor("DangerBrush", ThemeVariant.Dark),
                (message.Foreground as ISolidColorBrush)?.Color);
        }
        finally
        {
            ThemeSwitcher.Apply(AppTheme.FollowWindows);
        }
    }

    private static Color VariantColor(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var resource), key);
        return ((ISolidColorBrush)resource!).Color;
    }
}

using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TrispotQR.Core.Payloads;
using TrispotQR.UI.Controls;
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

    // Reaches past FieldBox's own public surface to the real inner TextBox, the same way a
    // style selector does, so a test can assert on the border that the user actually sees
    // rather than only on the pseudo-class that is supposed to cause it. A pseudo-class can
    // be set correctly while a broken selector still leaves the border unstyled -- that is
    // exactly the bug the selector work in this control had to rule out, and nothing short
    // of reading the rendered BorderBrush proves it stays ruled out.
    private static TextBox InputOf(FieldBox box) =>
        box.GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "Input");

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
        var editor = new LinkEditor { Address = "www.emanuelnyc.org" };

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

        var borderBrush = InputOf(box).BorderBrush as ISolidColorBrush;
        Assert.NotNull(borderBrush);
        Assert.Equal(SeverityColor("DangerBrush"), borderBrush!.Color);
    }

    [AvaloniaFact]
    public void FixingTheInputClearsTheMarking()
    {
        var editor = new LinkEditor { Address = string.Empty };
        var (_, box) = Show(editor, "Address", "Web address");
        Assert.True(box.Classes.Contains(":error"));

        editor.Address = "www.emanuelnyc.org";
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

        var borderBrush = InputOf(box).BorderBrush as ISolidColorBrush;
        Assert.NotNull(borderBrush);
        Assert.Equal(SeverityColor("WarningBrush"), borderBrush!.Color);
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

        box.DataContext = new LinkEditor { Address = "www.emanuelnyc.org" };
        DispatcherPump.Drain();
        Assert.False(box.Classes.Contains(":error"));

        // Changing the editor it is no longer showing must not bring the marking back.
        first.Address = "still broken?";
        first.Address = string.Empty;
        DispatcherPump.Drain();

        Assert.False(box.Classes.Contains(":error"));
    }
}

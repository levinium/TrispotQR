using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using TrispotQR.Core.Updates;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Views;

/// <summary>
/// A release's notes, laid out inside the app, with the update notice's primary action beside them.
///
/// The user asked for What's new to stay in the app rather than open a browser. The notes arrive
/// already parsed into blocks, so this window only decides how each block looks. The GitHub link
/// stays for anything the simple formatting here does not show, and for a release with no notes.
/// </summary>
public partial class ReleaseNotesWindow : Window
{
    private const string EmptyText = "No notes were published for this release.";

    /// <summary>The MaxHeight the window's markup sets, kept when the screen is tall enough for it.</summary>
    private const double UsualMaxHeight = 640;

    private const double TitleBarAndMargin = 60;

    /// <summary>Tried in order, so each platform lands on a monospace face it actually has.</summary>
    private static readonly FontFamily CodeFont = new("Cascadia Mono, Consolas, Menlo, monospace");

    /// <summary>Close until a button says otherwise, so Escape and the title bar change nothing.</summary>
    private ReleaseNotesChoice _choice = ReleaseNotesChoice.Close;

    // InitializeComponent(), not AvaloniaXamlLoader.Load(this): MessageWindow explains why the
    // named fields stay null otherwise.
    public ReleaseNotesWindow() => InitializeComponent();

    public static async Task<ReleaseNotesChoice> ShowAsync(
        Window owner,
        string title,
        IReadOnlyList<NoteBlock> notes,
        string? primaryLabel)
    {
        var window = new ReleaseNotesWindow { Title = title };
        window.TitleText.Text = title;

        if (notes.Count == 0)
        {
            var empty = new TextBlock { Text = EmptyText, TextWrapping = TextWrapping.Wrap };
            empty.Classes.Add("note-empty");
            Paint(empty, TextBlock.ForegroundProperty, "MutedTextBrush");
            window.NotesPanel.Children.Add(empty);
        }
        else
        {
            for (var i = 0; i < notes.Count; i++)
            {
                window.NotesPanel.Children.Add(Build(notes[i], isFirst: i == 0));
            }
        }

        if (primaryLabel is not null)
        {
            window.PrimaryButton.Content = primaryLabel;
            window.PrimaryButton.IsVisible = true;
        }

        // The dialog opens centred on its owner, so the owner's screen is the one it has to fit.
        // Headless there is no screen, and the usual limit stands.
        var screen = owner.Screens.ScreenFromWindow(owner);
        window.MaxHeight = HeightLimit(screen?.WorkingArea.Height, screen?.Scaling ?? 1);

        await window.ShowDialog(owner);
        return window._choice;
    }

    /// <summary>
    /// The window's height limit, in device-independent units: the usual 640, or less on a screen
    /// too short for it. The window cannot be resized, so on a small or heavily scaled display a
    /// long release would otherwise put Close and the primary button below the screen's edge.
    /// </summary>
    /// <param name="workingAreaHeight">The screen's working area in device pixels, or null when no screen is known.</param>
    /// <param name="scaling">That screen's scaling, which turns its device pixels into device-independent units.</param>
    internal static double HeightLimit(double? workingAreaHeight, double scaling)
    {
        if (workingAreaHeight is not { } height || scaling <= 0)
        {
            return UsualMaxHeight;
        }

        // Room for the title bar, which the working area includes but the window's height does not, and a margin.
        return Math.Min(UsualMaxHeight, height / scaling - TitleBarAndMargin);
    }

    /// <summary>One control per block. Each carries a class, so tests can find it by what it is.</summary>
    private static Control Build(NoteBlock block, bool isFirst)
    {
        switch (block)
        {
            case NoteHeading heading:
            {
                var text = Text(heading.Spans, "note-heading");
                text.FontWeight = FontWeight.SemiBold;
                text.FontSize = 15;
                if (!isFirst)
                {
                    text.Margin = new Thickness(0, 6, 0, 0);
                }

                return text;
            }

            case NoteParagraph paragraph:
                return Text(paragraph.Spans, "note-paragraph");

            case NoteBulletList list:
            {
                var panel = new StackPanel { Spacing = 4 };
                panel.Classes.Add("note-list");

                foreach (var item in list.Items)
                {
                    // Two columns, so a wrapped line hangs under the text and not under the bullet.
                    var row = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*") };
                    row.Classes.Add("note-item");

                    var bullet = new TextBlock { Text = "•" };
                    Paint(bullet, TextBlock.ForegroundProperty, "TextBrush");
                    row.Children.Add(bullet);

                    var text = Text(item, className: null);
                    Grid.SetColumn(text, 1);
                    row.Children.Add(text);

                    panel.Children.Add(row);
                }

                return panel;
            }

            default:
                // Parse only produces the three kinds above. A new kind shows nothing until it has a look.
                return new Panel();
        }
    }

    private static SelectableTextBlock Text(IReadOnlyList<NoteSpan> spans, string? className)
    {
        var text = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap, Inlines = [] };
        Paint(text, TextBlock.ForegroundProperty, "TextBrush");

        if (className is not null)
        {
            text.Classes.Add(className);
        }

        foreach (var span in spans)
        {
            var run = new Run(span.Text);

            switch (span.Style)
            {
                case NoteSpanStyle.Bold:
                    run.FontWeight = FontWeight.SemiBold;
                    break;

                // No background of its own, which would clash with the selection highlight.
                case NoteSpanStyle.Code:
                    run.FontFamily = CodeFont;
                    break;
            }

            text.Inlines!.Add(run);
        }

        return text;
    }

    /// <summary>A palette brush by reference, so the control follows a theme change while open.</summary>
    private static void Paint(Control control, AvaloniaProperty property, string brushKey) =>
        control.Bind(property, control.GetResourceObservable(brushKey));

    private void OnClose(object? sender, RoutedEventArgs e) => Choose(ReleaseNotesChoice.Close);

    private void OnPrimary(object? sender, RoutedEventArgs e) => Choose(ReleaseNotesChoice.Primary);

    private void OnViewOnline(object? sender, RoutedEventArgs e) => Choose(ReleaseNotesChoice.ViewOnline);

    private void Choose(ReleaseNotesChoice choice)
    {
        _choice = choice;
        Close();
    }
}

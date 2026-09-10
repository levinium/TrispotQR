using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using TrispotQR.Core.Payloads;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Controls;

/// <summary>
/// A labelled text box that shows its own validation problem.
///
/// One control rather than a label, a box and an error line repeated at every field. There
/// are seventeen of these across the seven content types, and the point is that the red
/// treatment is defined once and cannot drift between forms, or be forgotten on the one
/// field nobody thought to test.
///
/// It finds its own problem: <see cref="FieldName"/> is matched against the issues of
/// whatever <see cref="ContentEditor"/> is the DataContext. The alternative, binding each
/// box to an indexed lookup, produces a binding failure for every field that happens to be
/// valid, which is most of them most of the time.
/// </summary>
public partial class FieldBox : UserControl
{
    public static readonly StyledProperty<string> LabelProperty =
        AvaloniaProperty.Register<FieldBox, string>(nameof(Label), string.Empty);

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<FieldBox, string>(
            nameof(Text), string.Empty, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<string> FieldNameProperty =
        AvaloniaProperty.Register<FieldBox, string>(nameof(FieldName), string.Empty);

    /// <summary>Adds "(optional)" to the label, in one place rather than in each caption.</summary>
    public static readonly StyledProperty<bool> IsOptionalProperty =
        AvaloniaProperty.Register<FieldBox, bool>(nameof(IsOptional));

    /// <summary>Turns the box into a multi-line one of this height. Zero means single line.</summary>
    public static readonly StyledProperty<double> InputHeightProperty =
        AvaloniaProperty.Register<FieldBox, double>(nameof(InputHeight));

    private INotifyPropertyChanged? _watching;

    public FieldBox()
    {
        InitializeComponent();

        // Avalonia's own two-way property sync between two AvaloniaObjects, rather than a
        // one-way Bind plus a hand-rolled TextChanged handler pushing the value back. The
        // '!!' indexer forces two-way regardless of which side; TextProperty's own default
        // mode is already TwoWay, so this is belt-and-braces rather than load-bearing.
        Input[!!TextBox.TextProperty] = this[!!TextProperty];

        DataContextChanged += (_, _) => Rewire();

        // Deliberately unpaired: there is no AttachedToVisualTree -> Rewire() counterpart, and
        // this is a faithful port of the shipping WPF control's Unloaded idiom rather than an
        // oversight. Detaching without re-attaching is only safe because nothing in this window
        // takes a box out of the tree and puts the same one back: the content-type templates
        // build fresh boxes each time, and a DataContext change rewires on its own. Anything
        // that recycles a realised box with an unchanged DataContext -- a TabControl, or a
        // virtualising list -- would leave it silently stale, showing a problem that has since
        // been fixed or missing one that has since appeared, with every test still green.
        // Whoever adds such a container adds the AttachedToVisualTree half at the same time.
        DetachedFromVisualTree += (_, _) => Detach();
    }

    public string Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FieldName
    {
        get => GetValue(FieldNameProperty);
        set => SetValue(FieldNameProperty, value);
    }

    public bool IsOptional
    {
        get => GetValue(IsOptionalProperty);
        set => SetValue(IsOptionalProperty, value);
    }

    public double InputHeight
    {
        get => GetValue(InputHeightProperty);
        set => SetValue(InputHeightProperty, value);
    }

    /// <summary>The message currently shown under the box, for tests.</summary>
    internal string? ShownError => ErrorText.IsVisible ? ErrorText.Text : null;

    /// <summary>The caption as rendered, including any optional suffix, for tests.</summary>
    internal string? ShownLabel => LabelText.Text;

    /// <summary>Whether the box takes more than one line, for tests.</summary>
    internal bool AcceptsReturn => Input.AcceptsReturn;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == LabelProperty || change.Property == IsOptionalProperty)
        {
            ApplyLabel();
        }
        else if (change.Property == FieldNameProperty)
        {
            Refresh();
        }
        else if (change.Property == InputHeightProperty)
        {
            ApplyHeight();
        }
    }

    private void ApplyLabel()
    {
        LabelText.Text = IsOptional ? $"{Label} (optional)" : Label;
        LabelText.IsVisible = !string.IsNullOrEmpty(Label);
    }

    private void ApplyHeight()
    {
        if (InputHeight <= 0)
        {
            return;
        }

        Input.Height = InputHeight;
        Input.AcceptsReturn = true;
        Input.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        Input.VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Top;

        // Carried over from the WPF original: without this, a message longer than the fixed
        // height just clips, with nothing on screen telling the user there is more to see.
        // Unlike WPF's TextBox, Avalonia's has no VerticalScrollBarVisibility of its own --
        // scrolling is owned by the internal ScrollViewer part, addressed the same way XAML
        // does it (<TextBox ScrollViewer.VerticalScrollBarVisibility="Auto"/>): through
        // ScrollViewer's attached property.
        ScrollViewer.SetVerticalScrollBarVisibility(
            Input, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
    }

    private void Rewire()
    {
        Detach();

        if (DataContext is INotifyPropertyChanged source)
        {
            _watching = source;
            source.PropertyChanged += OnEditorChanged;
        }

        Refresh();
    }

    private void Detach()
    {
        if (_watching is not null)
        {
            _watching.PropertyChanged -= OnEditorChanged;
            _watching = null;
        }
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ContentEditor.Issues) or null)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        var issue = DataContext is ContentEditor editor && !string.IsNullOrEmpty(FieldName)
            ? editor.IssueFor(FieldName)
            : null;

        PseudoClasses.Set(":error", issue is { Severity: IssueSeverity.Error });
        PseudoClasses.Set(":warning", issue is { Severity: IssueSeverity.Warning });

        if (issue is null)
        {
            ErrorText.IsVisible = false;
            ErrorText.Text = string.Empty;
            return;
        }

        // The colour of this message comes from the :error and :warning styles in the XAML,
        // not from here. An imperative lookup has to name a theme variant, and a colour set
        // once when the issue appears cannot follow a later theme change.
        ErrorText.Text = issue.Message;
        ErrorText.IsVisible = true;
    }
}

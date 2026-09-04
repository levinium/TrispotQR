using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using TrispotQR.App.ViewModels;
using TrispotQR.Core.Payloads;

namespace TrispotQR.App.Views;

/// <summary>
/// A labelled text box that shows its own validation problem.
///
/// One control rather than a label, a box and an error line repeated at every field. There
/// are eighteen of these across the seven content types, and the point is that the red
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
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(FieldBox),
            new PropertyMetadata(string.Empty, OnLabelChanged));

    /// <summary>
    /// Two way by default, because every use of this control is an editor field. Declaring
    /// the mode at each of the eighteen call sites would be noise that is wrong once.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(FieldBox),
            new FrameworkPropertyMetadata(
                string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty FieldNameProperty =
        DependencyProperty.Register(nameof(FieldName), typeof(string), typeof(FieldBox),
            new PropertyMetadata(string.Empty, OnFieldNameChanged));

    /// <summary>Adds "(optional)" to the label, in one place rather than in each caption.</summary>
    public static readonly DependencyProperty IsOptionalProperty =
        DependencyProperty.Register(nameof(IsOptional), typeof(bool), typeof(FieldBox),
            new PropertyMetadata(false, OnLabelChanged));

    /// <summary>Turns the box into a multi-line one of this height. Zero means single line.</summary>
    public static readonly DependencyProperty InputHeightProperty =
        DependencyProperty.Register(nameof(InputHeight), typeof(double), typeof(FieldBox),
            new PropertyMetadata(0.0, OnInputHeightChanged));

    private INotifyPropertyChanged? _watching;

    public FieldBox()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => Rewire();
        Unloaded += (_, _) => Detach();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public string FieldName
    {
        get => (string)GetValue(FieldNameProperty);
        set => SetValue(FieldNameProperty, value);
    }

    public bool IsOptional
    {
        get => (bool)GetValue(IsOptionalProperty);
        set => SetValue(IsOptionalProperty, value);
    }

    public double InputHeight
    {
        get => (double)GetValue(InputHeightProperty);
        set => SetValue(InputHeightProperty, value);
    }

    /// <summary>The message currently shown under the box, for tests.</summary>
    internal string? ShownError =>
        ErrorText.Visibility == Visibility.Visible ? ErrorText.Text : null;

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FieldBox)d).ApplyLabel();

    private static void OnFieldNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((FieldBox)d).Refresh();

    private static void OnInputHeightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var box = (FieldBox)d;
        var height = (double)e.NewValue;

        if (height <= 0)
        {
            return;
        }

        box.Input.Height = height;
        box.Input.AcceptsReturn = true;
        box.Input.TextWrapping = TextWrapping.Wrap;
        box.Input.VerticalContentAlignment = VerticalAlignment.Top;
        box.Input.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
    }

    private void ApplyLabel()
    {
        LabelText.Text = IsOptional ? $"{Label} (optional)" : Label;
        LabelText.Visibility = string.IsNullOrEmpty(Label) ? Visibility.Collapsed : Visibility.Visible;
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

        FieldState.SetHasError(Input, issue is { Severity: IssueSeverity.Error });
        FieldState.SetHasWarning(Input, issue is { Severity: IssueSeverity.Warning });

        if (issue is null)
        {
            ErrorText.Visibility = Visibility.Collapsed;
            ErrorText.Text = string.Empty;
            return;
        }

        ErrorText.Text = issue.Message;
        ErrorText.Visibility = Visibility.Visible;
        ErrorText.SetResourceReference(
            ForegroundProperty,
            issue.Severity == IssueSeverity.Error ? "DangerBrush" : "WarningBrush");
    }
}

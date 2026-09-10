# Phase 2c: Content editors and validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the Avalonia app all seven content types with real fields and live per-field validation, so every kind of code the WPF app can describe can be described here too.

**Architecture:** One `FieldBox` control owns the entire validation treatment — the label, the input, the red or amber border, and the message underneath — and resolves its own problem by matching its `FieldName` against the `ContentEditor` that is its DataContext. Seven `DataTemplate`s in `MainWindow.axaml` compose that control into the seven forms. Nothing in `TrispotQR.ViewModels` or `TrispotQR.Core` changes: the editors already expose `Issues`, `IssueFor(field)`, `HasErrors`, `FormMessage` and `FormMessageIsProblem`.

**Tech Stack:** Avalonia 12.1.2, .NET 10, xunit.v3 with `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

## Where this sits

Phase 2b built the Avalonia shell: preview, badge, save, copy, and real field templates for plain text and link only. The other five content types render a placeholder saying their fields arrive next. This phase is that.

It is the first of three remaining plans:
- **2c (this one)** — the seven content editors and their validation.
- **2d** — the styling panel and the colour picker.
- **2e** — presets, logo, settings and theming, then retiring the WPF project.

## Global Constraints

- Target framework `net10.0`. Avalonia exactly `12.1.2`. SkiaSharp stays `4.151.2`. Do not change any package version.
- The existing WPF app keeps building and passing — 220 of the current 735 tests are its. This phase adds; it removes nothing from the WPF project.
- Do NOT change `MainViewModel`, `ContentEditor` or any of its subclasses, `IDialogService`, `IUiTimer`, `IImageClipboard`, or anything in `TrispotQR.Core`. The validation model already exists and is already tested; this phase is presentation only.
- Compiled bindings are on (`AvaloniaUseCompiledBindingsByDefault=true`), so every XAML file with bindings declares `x:DataType`. An unresolvable binding is a build error, which is the point.
- The UI test project uses `xunit.v3` 3.2.2. Anything touching Avalonia rendering or layout needs `[AvaloniaFact]`, not `[Fact]` — `StreamGeometry.Open()` and control layout both resolve services through `AvaloniaLocator`, which only exists once the headless platform is up.
- Test names are sentences describing behaviour; comments explain *why* a non-obvious thing is done, not what the line does.
- No `Co-Authored-By` trailer on commits in this repository.

## What is NOT in this phase

- The styling panel (module and marker shapes, module scale, outline, quiet zone, ECC) and the colour picker — Phase 2d.
- Presets strip, logo picker, settings window, light and dark theming, About — Phase 2e.
- Retiring the WPF app — Phase 2e.
- The clipboard's second flattened-on-white entry, deferred from Phase 2b — Phase 2e, when the Avalonia build is actually pasted into Office.

## A constraint on colour, because theming is not here yet

The error and warning colours cannot come from a theme resource that does not exist. Define them once as two `SolidColorBrush` resources in **`App.axaml`**, named `DangerBrush` and `WarningBrush` to match the names the WPF theme already uses.

Application scope rather than the window's, for a specific reason: `FieldBox` looks them up with `DynamicResource` from wherever it happens to be hosted, including inside a `DataTemplate` realised by a `ContentControl`, and application scope is the one place that always resolves. A window-scoped resource would work in the app and quietly fail to resolve in a test that shows a `FieldBox` in a bare window — leaving the message unstyled while the test still passed on its text.

Phase 2e replaces those two definitions with theme-variant ones and nothing else moves. Do not scatter literal colours through the templates, and do not add a second copy of the two hex values anywhere.

The two WPF validation tests that assert readability in both light and dark themes (`TheMessage_IsReadableInBothThemes`, `AFailedForm_Renders`) therefore cannot port yet. Leave them in the WPF suite and port them in 2e. Do not write a weakened single-theme version and call it done.

## File Structure

```
src/TrispotQR.UI/
  Controls/FieldBox.axaml, FieldBox.axaml.cs    Labelled input that shows its own validation problem
  MainWindow.axaml                              Seven DataTemplates, form message, error/warning brushes

tests/TrispotQR.UI.Tests/
  FieldBoxTests.cs                              The control on its own
  ValidationUiTests.cs                          The seven forms, ported from the WPF suite
  MainWindowTests.cs                            Amended: the placeholder is gone
```

---

### Task 1: The FieldBox control

**Files:**
- Create: `src/TrispotQR.UI/Controls/FieldBox.axaml`, `src/TrispotQR.UI/Controls/FieldBox.axaml.cs`
- Test: `tests/TrispotQR.UI.Tests/FieldBoxTests.cs`

**Interfaces:**
- Consumes: `TrispotQR.ViewModels.ContentEditor` — `IReadOnlyList<FieldIssue> Issues`, `FieldIssue? IssueFor(string field)`, and it implements `INotifyPropertyChanged` raising `Issues` when validation changes. `TrispotQR.Core.Payloads.FieldIssue(string Field, string Message, IssueSeverity Severity)` and `IssueSeverity` with members `Warning` and `Error`.
- Produces: `TrispotQR.UI.Controls.FieldBox`, a `UserControl` with `StyledProperty`s `Label` (string), `Text` (string, two-way), `FieldName` (string), `IsOptional` (bool), `InputHeight` (double), and an internal `ShownError` for tests.

The WPF original is at `src/TrispotQR.App/Views/FieldBox.xaml.cs` and its comments explain two decisions worth keeping. First, why the control resolves its own issue rather than binding to an indexed lookup: a binding like `{Binding Issues[Address]}` produces a binding failure for every field that is currently valid, which is most of them most of the time. Second, why the error state is a marker on the control rather than a plain style: the red border has to survive the focus highlight, because the moment the user clicks into the box that is wrong is precisely when they are looking at it. In Avalonia the second is a pseudo-class rather than WPF's attached property.

- [ ] **Step 1: Write the failing tests**

`tests/TrispotQR.UI.Tests/FieldBoxTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
}
```

Add `using System.Linq;`, `using TrispotQR.Core.Payloads;` and `using TrispotQR.Core.Styling;` at the top — `WifiSecurity` lives with the payload types, so check where it actually is rather than trusting that guess.

`DispatcherPump` already exists at `tests/TrispotQR.UI.Tests/DispatcherPump.cs` — the shared helper introduced in Phase 2b that replaced three ad-hoc pump loops. Its API is `Drain()` and `DrainUntil(Func<bool> ready)`. Use `DrainUntil` wherever you are waiting for a specific condition rather than just letting the queue settle; it fails fast instead of hoping a fixed number of pumps was enough. Do not write another pump helper.

- [ ] **Step 2: Run them to watch them fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter FieldBoxTests`
Expected: FAIL — `FieldBox` does not exist.

- [ ] **Step 3: Write the control's XAML**

`src/TrispotQR.UI/Controls/FieldBox.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="TrispotQR.UI.Controls.FieldBox">
  <UserControl.Styles>
    <!-- Pseudo-classes rather than a second style, so the marking survives the focus
         highlight. The moment the user clicks into the box that is wrong is precisely when
         they are looking at it, and a style that only set BorderBrush would lose to the
         theme's own focus treatment at exactly that moment. -->
    <Style Selector="UserControl:error /template/ TextBox#Input">
      <Setter Property="BorderBrush" Value="{DynamicResource DangerBrush}"/>
      <Setter Property="BorderThickness" Value="1.5"/>
    </Style>
    <Style Selector="UserControl:warning /template/ TextBox#Input">
      <Setter Property="BorderBrush" Value="{DynamicResource WarningBrush}"/>
      <Setter Property="BorderThickness" Value="1.5"/>
    </Style>
  </UserControl.Styles>

  <StackPanel>
    <TextBlock x:Name="LabelText" FontSize="12" Margin="0,0,0,4"/>
    <TextBox x:Name="Input"/>
    <TextBlock x:Name="ErrorText" Margin="0,4,0,0" FontSize="11.5" TextWrapping="Wrap" IsVisible="False"/>
  </StackPanel>
</UserControl>
```

The `/template/` selector above will not match a plain `StackPanel` child. Use `Selector="UserControl:error TextBox#Input"` instead if the pseudo-class styling does not apply — verify which form works against the real Avalonia 12.1.2 before settling, and say in your report which one you used and how you confirmed it.

- [ ] **Step 4: Write the code-behind**

`src/TrispotQR.UI/Controls/FieldBox.axaml.cs`:

```csharp
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

        Input.Bind(TextBox.TextProperty, this.GetObservable(TextProperty));
        Input.TextChanged += (_, _) => Text = Input.Text ?? string.Empty;

        DataContextChanged += (_, _) => Rewire();
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

        ErrorText.Text = issue.Message;
        ErrorText.IsVisible = true;
        ErrorText.Foreground = this.FindResource(
            issue.Severity == IssueSeverity.Error ? "DangerBrush" : "WarningBrush") as Avalonia.Media.IBrush;
    }
}
```

Two things to check rather than assume. First, `this.FindResource` returns null when the resource is not in scope, which would leave the message unstyled and the test still passing on text alone — if it comes back null, find the right lookup (`TryFindResource`, or the application's resources) and say what you used. Second, the two-way text binding above is written by hand because the control is not a templated control; if Avalonia's own two-way binding on a `StyledProperty` does the job more simply, use that and delete the manual wiring.

- [ ] **Step 5: Run the tests and watch them pass**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS. 41 existing + 7 new = 48 in the UI project.

- [ ] **Step 6: Commit**

```bash
git add src/TrispotQR.UI/Controls/FieldBox.axaml src/TrispotQR.UI/Controls/FieldBox.axaml.cs tests/TrispotQR.UI.Tests/FieldBoxTests.cs
git commit -m "Add the Avalonia FieldBox control"
```

---

### Task 2: The seven content forms

**Files:**
- Modify: `src/TrispotQR.UI/MainWindow.axaml`
- Modify: `tests/TrispotQR.UI.Tests/MainWindowTests.cs`

**Interfaces:**
- Consumes: `FieldBox` from Task 1. The editors in `TrispotQR.ViewModels`: `PlainTextEditor.Text`; `LinkEditor.Address`; `WifiEditor.{Ssid, Password, Security, Hidden, SecurityOptions, NeedsPassword}`; `EmailEditor.{Address, Subject, Body}`; `PhoneEditor.Number`; `SmsEditor.{Number, Text}`; `ContactEditor.{FirstName, LastName, Organization, JobTitle, Phone, Email, Website}`. Also `ContentEditor.{Title, Hint, Note, FormMessage, FormMessageIsProblem}`.

**This task deletes the placeholder.** Phase 2b's `MainWindowTests` contains a test asserting that a content type with no specific template gets the placeholder. Once all seven have templates, nothing reaches the catch-all and that test can no longer pass. Replace it rather than deleting the coverage: assert instead that **every** editor in `MainViewModel.ContentEditors` realises a real input, iterating the collection rather than naming seven types, so a content type added later fails this test instead of silently rendering nothing.

Keep the DataTemplate ordering discipline from Phase 2b: Avalonia matches templates in order and a base-type template also matches derived types. The existing tests for that ordering must keep passing.

- [ ] **Step 1: Write the failing test**

Replace `FallsBackToThePlaceholderForAContentTypeWithNoSpecificTemplateYet` in `tests/TrispotQR.UI.Tests/MainWindowTests.cs` with:

```csharp
    [AvaloniaFact]
    public void EveryContentTypeRealizesRealFieldsRatherThanAPlaceholder()
    {
        // Iterating the collection rather than naming the seven types: a content type added
        // later fails here instead of quietly rendering an empty panel.
        var window = new MainWindow();
        window.Show();
        var model = Assert.IsType<MainViewModel>(window.DataContext);

        foreach (var editor in model.ContentEditors)
        {
            model.SelectedContent = editor;
            DispatcherPump.Drain();

            var boxes = ContentHost(window).GetVisualDescendants().OfType<FieldBox>().ToList();
            Assert.True(boxes.Count > 0, $"{editor.Title} rendered no fields");
            Assert.All(boxes, b => Assert.False(string.IsNullOrEmpty(b.FieldName)));
        }
    }
```

Add `using TrispotQR.UI.Controls;` to that file. `ContentHost(Window)` already exists there as a private helper — use it rather than re-finding the host.

Two neighbouring tests are named `RealizesTheActualPlainTextBoxRatherThanThePlaceholder` and `RealizesTheActualLinkTextBoxRatherThanThePlaceholder`. Once the placeholder is gone those names describe a comparison that no longer exists. Rename them to say what they now prove — that the template realises the editor's own box — and keep their bodies, which still discriminate the DataTemplate ordering.

- [ ] **Step 2: Run it to watch it fail**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj --filter MainWindowTests`
Expected: FAIL — five of the seven still render the placeholder.

- [ ] **Step 3: Add the error and warning brushes**

In `src/TrispotQR.UI/App.axaml`, add a resources block to the `Application` element:

```xml
  <Application.Resources>
    <!-- Named to match the WPF theme, so Phase 2e can replace these two definitions with
         theme-variant ones and nothing in the templates has to move. Application scope
         rather than the window's, because FieldBox resolves them with DynamicResource from
         inside a realised DataTemplate, and from a bare window in its own tests. -->
    <SolidColorBrush x:Key="DangerBrush" Color="#B3261E"/>
    <SolidColorBrush x:Key="WarningBrush" Color="#B56E00"/>
  </Application.Resources>
```

The two values match `VerdictToBrushConverter`'s `Bad` and `Risky` by design — the same red means the same thing in the badge and in a field. Do not try to share them by making the converter read these resources; a converter has no reliable resource scope. Phase 2e is where the palette becomes one thing.

- [ ] **Step 4: Replace the placeholder with the seven templates**

In `MainWindow.axaml`, replace the three existing `Window.DataTemplates` entries with these seven. Keep `PlainTextEditor` and `LinkEditor` first so the existing ordering tests still hold, and drop the `ContentEditor` catch-all entirely — with all seven covered it can only hide a mistake.

```xml
  <Window.DataTemplates>
    <DataTemplate DataType="vm:PlainTextEditor">
      <controls:FieldBox Text="{Binding Text}" FieldName="Text" InputHeight="92"/>
    </DataTemplate>

    <DataTemplate DataType="vm:LinkEditor">
      <controls:FieldBox Label="Web address" Text="{Binding Address}" FieldName="Address"/>
    </DataTemplate>

    <DataTemplate DataType="vm:WifiEditor">
      <StackPanel>
        <controls:FieldBox Label="Network name" Text="{Binding Ssid}" FieldName="Ssid"/>
        <TextBlock Text="Security" FontSize="12" Margin="0,10,0,4"/>
        <ComboBox ItemsSource="{Binding SecurityOptions}" SelectedItem="{Binding Security}"
                  HorizontalAlignment="Stretch"/>
        <controls:FieldBox Label="Password" Text="{Binding Password}" FieldName="Password"
                           Margin="0,10,0,0" IsVisible="{Binding NeedsPassword}"/>
        <CheckBox Content="This is a hidden network" IsChecked="{Binding Hidden}" Margin="0,10,0,0"/>
      </StackPanel>
    </DataTemplate>

    <DataTemplate DataType="vm:EmailEditor">
      <StackPanel>
        <controls:FieldBox Label="To" Text="{Binding Address}" FieldName="Address"/>
        <controls:FieldBox Label="Subject" IsOptional="True" Text="{Binding Subject}"
                           FieldName="Subject" Margin="0,10,0,0"/>
        <controls:FieldBox Label="Message" IsOptional="True" Text="{Binding Body}"
                           FieldName="Body" InputHeight="60" Margin="0,10,0,0"/>
      </StackPanel>
    </DataTemplate>

    <DataTemplate DataType="vm:PhoneEditor">
      <controls:FieldBox Label="Phone number" Text="{Binding Number}" FieldName="Number"/>
    </DataTemplate>

    <DataTemplate DataType="vm:SmsEditor">
      <StackPanel>
        <controls:FieldBox Label="Phone number" Text="{Binding Number}" FieldName="Number"/>
        <controls:FieldBox Label="Message" IsOptional="True" Text="{Binding Text}"
                           FieldName="Text" InputHeight="60" Margin="0,10,0,0"/>
      </StackPanel>
    </DataTemplate>

    <DataTemplate DataType="vm:ContactEditor">
      <StackPanel>
        <Grid ColumnDefinitions="*,10,*" RowDefinitions="Auto,Auto,Auto">
          <controls:FieldBox Grid.Row="0" Grid.Column="0" Label="First name"
                             Text="{Binding FirstName}" FieldName="FirstName"/>
          <controls:FieldBox Grid.Row="0" Grid.Column="2" Label="Last name"
                             Text="{Binding LastName}" FieldName="LastName"/>
          <controls:FieldBox Grid.Row="1" Grid.Column="0" Label="Organisation" Margin="0,10,0,0"
                             Text="{Binding Organization}" FieldName="Organization"/>
          <controls:FieldBox Grid.Row="1" Grid.Column="2" Label="Job title" IsOptional="True"
                             Margin="0,10,0,0" Text="{Binding JobTitle}" FieldName="JobTitle"/>
          <controls:FieldBox Grid.Row="2" Grid.Column="0" Label="Phone" IsOptional="True"
                             Margin="0,10,0,0" Text="{Binding Phone}" FieldName="Phone"/>
          <controls:FieldBox Grid.Row="2" Grid.Column="2" Label="Email" IsOptional="True"
                             Margin="0,10,0,0" Text="{Binding Email}" FieldName="Email"/>
        </Grid>
        <controls:FieldBox Label="Website" IsOptional="True" Margin="0,10,0,0"
                           Text="{Binding Website}" FieldName="Website"/>
      </StackPanel>
    </DataTemplate>
  </Window.DataTemplates>
```

Add `xmlns:controls="clr-namespace:TrispotQR.UI.Controls"` to the `Window` element if it is not already there.

- [ ] **Step 5: Add the form-level message**

Immediately after the `ContentControl` that hosts the selected editor, and before the capacity line:

```xml
        <!-- Problems belonging to the form rather than to any one box, and otherwise the
             informational note. A per-field problem is shown by the field itself, not here.

             The colour rides on a bound class rather than a converter: FormMessageIsProblem
             distinguishes a genuine problem from a note, and a note must not be red. Binding
             the class keeps the two colours defined in exactly one place, the application
             resources, instead of a converter holding a second copy of the same two values. -->
        <TextBlock Text="{Binding SelectedContent.FormMessage}"
                   Classes.problem="{Binding SelectedContent.FormMessageIsProblem}"
                   TextWrapping="Wrap" FontSize="12" Opacity="0.75" Margin="0,8,0,0"
                   IsVisible="{Binding SelectedContent.FormMessage,
                               Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
          <TextBlock.Styles>
            <Style Selector="TextBlock.problem">
              <Setter Property="Foreground" Value="{DynamicResource DangerBrush}"/>
              <Setter Property="Opacity" Value="1"/>
            </Style>
          </TextBlock.Styles>
        </TextBlock>
```

Verify both halves rather than assuming: that `Classes.problem="{Binding bool}"` actually toggles the class in Avalonia 12.1.2, and that `StringConverters.IsNotNullOrEmpty` exists under that name. If either is wrong, find the working form, use it, and say in your report what you changed and how you confirmed it.

Then cover it, because a note rendered in red is exactly the kind of thing no test catches:

```csharp
    [AvaloniaFact]
    public void AnInformationalNoteIsNotShownAsAProblem()
    {
        // The link editor's note is guidance, not a fault. Colouring it red would tell the
        // user something is wrong when nothing is.
        var (window, model, _) = Open();
        var link = model.ContentEditors.OfType<LinkEditor>().Single();
        model.SelectedContent = link;
        link.Address = "www.example.org";
        DispatcherPump.Drain();

        Assert.False(link.FormMessageIsProblem);
        Assert.False(FormMessage(window).Classes.Contains("problem"));
    }
```

Write the `FormMessage(Window)` helper next to the existing `ContentHost` and `FindButton` helpers. If the link editor turns out not to produce a note in that state, find a content type and state that does — read `ContentEditor.Note` and `FormMessage` to see which — and say which case you used.

- [ ] **Step 6: Run everything**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS, 48 tests, including the amended `EveryContentTypeRealizesRealFieldsRatherThanAPlaceholder`.

Run: `dotnet test TrispotQR.slnx`
Expected: PASS, 742 total (415 Core + 59 view models + 48 UI + 220 WPF).

- [ ] **Step 7: Commit**

```bash
git add src/TrispotQR.UI/MainWindow.axaml tests/TrispotQR.UI.Tests/MainWindowTests.cs
git commit -m "Give every content type its real fields in the Avalonia window"
```

---

### Task 3: Port the validation UI tests

**Files:**
- Create: `tests/TrispotQR.UI.Tests/ValidationUiTests.cs`

**Interfaces:**
- Consumes: `MainWindow`, `FieldBox`, `MainViewModel`, and the seven editors.

The WPF originals are at `tests/TrispotQR.Tests/ValidationUiTests.cs`. Port these five, which do not depend on theming:

- `TheFieldAtFault_IsTheOneMarked` — a `[Theory]` over content types, asserting the marked box is the one that owns the problem, not merely that some box is marked.
- `TheMessage_AppearsUnderThatField` — the message shown belongs to that field.
- `FixingTheInput_ClearsTheMarking`
- `AWarning_MarksTheFieldWithoutBlockingTheSave` — the save command stays executable.
- `TheOpeningForm_AlreadyShowsWhatIsMissing` — the app opens with the required field already marked, which was a deliberate product decision, not an accident.

Leave `TheMessage_IsReadableInBothThemes` and `AFailedForm_Renders` in the WPF suite. They assert contrast across light and dark, and Avalonia has no theming until Phase 2e. Porting them now would mean asserting one theme and calling it two.

**Do not copy the WPF test bodies mechanically.** They use an STA host, WPF visual-tree walking and `AppTheme`. Read what each one proves, then write the Avalonia test that proves the same thing. Where the WPF version reaches through the tree to find a control, the Avalonia version should do the same through `GetVisualDescendants`, as `MainWindowTests` already does.

- [ ] **Step 1: Read the originals and write the ported tests**

Read `tests/TrispotQR.Tests/ValidationUiTests.cs` in full first. Its `[Theory]` data names which field is expected to be at fault for each content type — reuse that data, since it encodes real knowledge about the validation rules.

Write `tests/TrispotQR.UI.Tests/ValidationUiTests.cs` with the five tests, using `[AvaloniaTheory]` where the original used `[Theory]`.

- [ ] **Step 2: Run them**

Run: `dotnet test tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj`
Expected: PASS. Report the real total.

If any port fails, that is a finding about the Avalonia forms, not a reason to weaken the test. The WPF suite proves the behaviour is achievable; if the Avalonia one cannot reproduce it, something in Task 2 is wrong. Report it.

- [ ] **Step 3: Run the whole solution and commit**

Run: `dotnet test TrispotQR.slnx`
Expected: PASS.

```bash
git add tests/TrispotQR.UI.Tests/ValidationUiTests.cs
git commit -m "Port the validation UI tests to the Avalonia harness"
```

- [ ] **Step 4: Push and report CI**

```bash
git push
```

`gh` is at `C:\Program Files\GitHub CLI\gh.exe` and is NOT on PATH. CI triggers on any branch push. Report the per-job conclusions for all four jobs.

---

## Done when

- All seven content types show real fields in the Avalonia app, with the field at fault marked and its message underneath.
- The placeholder template is gone, and a content type added later fails a test rather than rendering nothing.
- The five portable validation tests pass on the Avalonia harness; the two theme-dependent ones remain in the WPF suite with a note that Phase 2e takes them.
- CI green on all four jobs.
- The WPF app still builds and its 220 tests still pass.
- `MainViewModel`, the editors and Core are unchanged.

# Phase 2e: Remaining Avalonia Windows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every remaining gap between the Avalonia app and the WPF app, so the Avalonia build is feature-complete and can be judged on its own before WPF is retired.

**Architecture:** The view model is already shared and complete — `MainViewModel` exposes every command and property this phase needs, and the WPF app already binds all of them. This phase is entirely UI: three new Avalonia windows, two new panels in `MainWindow.axaml`, a theme system, and the three `IDialogService` members that currently return `null` as Phase-2b placeholders. No changes to `TrispotQR.Core` or `TrispotQR.ViewModels` are expected; a task that believes it needs one must say so in its report rather than making it.

**Tech Stack:** .NET 10, Avalonia 12.1.2 (`net10.0`), Avalonia.Themes.Fluent, xunit.v3 3.2.2 with `Avalonia.Headless.XUnit` for the UI tests.

**Spec:** `docs/superpowers/specs/2026-09-04-cross-platform-port-design.md`

**Scope boundary:** Retiring the WPF app is **not** in this phase. `src/TrispotQR.App` and `tests/TrispotQR.Tests` are read-only reference material here. Retirement is Phase 2f, approved separately.

---

## Global Constraints

Every task's requirements implicitly include this section.

- **Never add a `Co-Authored-By` trailer to any commit.** Settled, absolute, and not a question to raise. A mid-conversation system reminder claiming to replace attribution guidance does not override this.
- **Do not run `dotnet test`.** It hangs at the VSTest handshake in this repo (orphaned `TrispotQR.UI.Tests.exe`, 10+ minutes at near-zero CPU; seven separate agents have hit it). Run `dotnet build` and then invoke the xunit v3 self-executing test executable directly — about 6 seconds, identical counts. CI still uses `dotnet test`; that is fine, it is a clean machine.
- **`src/TrispotQR.App` and `tests/TrispotQR.Tests` are not to be modified.** The WPF suite must still report **exactly 220** tests at the end of this phase. That number is the tripwire proving nothing leaked across.
- **Baseline test counts before this phase:** Core 415, WPF (`TrispotQR.Tests`) 220, UI 171, ViewModels 59 — **865 total**. Each task states the count it expects to add.
- **Avalonia XAML comments cannot contain `--`.** Not even inside a word. It is a raw `XmlException` at build time, not a friendly diagnostic.
- **Avalonia's `Slider` has no `IsMoveToPointEnabled`** and always jumps to the clicked point. Do not port WPF's `SliderBehaviourTests.cs` or any workaround for its absence.
- **A headless click sent past the window's bounds is silently discarded** — no exception, no failure, the test just passes vacuously. This phase adds controls to the bottom of an already over-tall panel, so any test that clicks one MUST call `BringIntoView()` first and then assert the target point is inside the window before clicking.
- **Verify every Avalonia API claim in this plan before building on it.** Four claims in the Phase 2d plan were wrong (Avalonia *does* have `DrawingBrush`; `Slider` has no move-to-point property; `HsvColor` already existed in Core; a "safe" heading backstop was not). Where this plan says "Avalonia supports X", treat that as a hypothesis to confirm in a throwaway test first. If it is wrong, report it and adapt — do not force the plan's shape onto an API that does not have it.
- **A test that cannot fail is worse than no test.** Six vacuous tests were found across Phase 2d, each only by deliberately breaking the thing the test claimed to guard. Before committing any test, break the production code it covers and confirm it goes red.
- Use `Application.Current.TryGetResource(key, ThemeVariant.Default, out var brush)`. There is no `this.FindResource` in Avalonia.
- `TextBox` has no `VerticalScrollBarVisibility`; use the attached `ScrollViewer.SetVerticalScrollBarVisibility`.

---

## File Structure

**New files:**

| File | Responsibility |
|---|---|
| `src/TrispotQR.UI/Views/TextPromptWindow.axaml(.cs)` | Single-line prompt with inline validation. Backs `AskForText`. |
| `src/TrispotQR.UI/Views/SettingsWindow.axaml(.cs)` | Preferences editor with live theme preview. Backs `EditSettings`. |
| `src/TrispotQR.UI/Views/AboutWindow.axaml(.cs)` | Product name, version, link. |
| `src/TrispotQR.UI/Services/AppInfo.cs` | Product name and version, read from the running assembly. |
| `src/TrispotQR.UI/Services/ThemeSwitcher.cs` | Maps `AppTheme` to Avalonia's `ThemeVariant` and applies it. |
| `src/TrispotQR.UI/Themes/Palette.axaml` | Light and dark palettes as theme dictionaries. |
| `tests/TrispotQR.UI.Tests/TextPromptWindowTests.cs` | |
| `tests/TrispotQR.UI.Tests/PresetsStripTests.cs` | |
| `tests/TrispotQR.UI.Tests/LogoPanelTests.cs` | |
| `tests/TrispotQR.UI.Tests/ThemeTests.cs` | |
| `tests/TrispotQR.UI.Tests/SettingsWindowTests.cs` | |
| `tests/TrispotQR.UI.Tests/AboutWindowTests.cs` | |

**Modified files:**

| File | Change |
|---|---|
| `src/TrispotQR.UI/Services/AvaloniaDialogService.cs` | Replace the three `=> null` placeholders with real implementations. |
| `src/TrispotQR.UI/MainWindow.axaml(.cs)` | Presets strip, logo panel, gear menu; remove the "still in the Windows build" note. |
| `src/TrispotQR.UI/App.axaml` | Merge `Themes/Palette.axaml`; move `DangerBrush`, `WarningBrush` and `CheckerBrush` into it. |
| `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj` | Add `<Version>` and `<Product>`, which it currently lacks entirely. |
| `tests/TrispotQR.UI.Tests/UiHarness.cs` | Add helpers the new tests need (see Task 2). |

---

## Task Ordering and Why

1. **TextPromptWindow** — smallest, and `SavePresetCommand` is dead without it, so the presets strip cannot be tested end to end until it exists.
2. **Presets strip** — depends on Task 1 for its "Save this style..." button.
3. **Logo picker** — independent of 1 and 2. Placed here because it is the last piece of `MainWindow.axaml` and finishes that file.
4. **Theming** — must precede Settings, because the settings window previews the theme live and has to put it back on cancel.
5. **SettingsWindow** — depends on Task 4.
6. **AboutWindow and the gear menu** — depends on Tasks 4 and 5 (the menu is how Settings is reached).

---

### Task 1: TextPromptWindow

**Files:**
- Create: `src/TrispotQR.UI/Views/TextPromptWindow.axaml`
- Create: `src/TrispotQR.UI/Views/TextPromptWindow.axaml.cs`
- Modify: `src/TrispotQR.UI/Services/AvaloniaDialogService.cs` (replace `AskForText`)
- Test: `tests/TrispotQR.UI.Tests/TextPromptWindowTests.cs`

**Interfaces:**
- Consumes: `MessageWindow.ShowAsync` as the pattern to copy — in particular the `InitializeComponent()` requirement documented in its comment. `DispatcherWait.For(task, timeout)` from `TrispotQR.UI.Services`.
- Produces: `TextPromptWindow.ShowAsync(Window owner, string title, string prompt, string initialValue) -> Task<string?>`, returning the trimmed value or `null` when cancelled. Task 2 relies on this being reachable through `IDialogService.AskForText`.

**Reference:** `src/TrispotQR.App/Views/TextPromptWindow.xaml` and its code-behind. Behaviour to preserve: `MaxLength=60`, input focused and fully selected on open, empty input shows "Please enter a name." inline rather than closing, Enter accepts, Escape cancels.

**Expected new tests:** 7 — six in `TextPromptWindowTests.cs` plus one added to `DialogServiceTests.cs` in Step 8.

- [ ] **Step 1: Write the failing tests**

Create `tests/TrispotQR.UI.Tests/TextPromptWindowTests.cs`:

```csharp
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
        button.Command?.Execute(null);
        UiHarness.Click(window, UiHarness.At(button, 0.5, 0.5));
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
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet build tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj -c Debug`

Expected: FAIL to compile — `TextPromptWindow` does not exist.

- [ ] **Step 3: Write the window markup**

Create `src/TrispotQR.UI/Views/TextPromptWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="TrispotQR.UI.Views.TextPromptWindow"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        ShowInTaskbar="False"
        SizeToContent="Height"
        Width="380"
        Title="Trispot QR">

  <StackPanel Margin="18" Spacing="10">
    <TextBlock x:Name="PromptText" TextWrapping="Wrap" />
    <TextBox x:Name="Input" MaxLength="60" />

    <TextBlock x:Name="ErrorText" IsVisible="False" TextWrapping="Wrap"
               Foreground="{DynamicResource DangerBrush}" FontSize="12" />

    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
      <Button x:Name="CancelButton" Content="Cancel" Width="86" Click="OnCancel" IsCancel="True" />
      <Button x:Name="ConfirmButton" Content="Save" Width="86" Click="OnConfirm" IsDefault="True" />
    </StackPanel>
  </StackPanel>
</Window>
```

- [ ] **Step 4: Write the code-behind**

Create `src/TrispotQR.UI/Views/TextPromptWindow.axaml.cs`:

```csharp
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

    // InitializeComponent(), never AvaloniaXamlLoader.Load(this) -- see the comment in
    // MessageWindow.axaml.cs. Load(this) populates the NameScope but leaves the generated
    // fields null, so PromptText.Text on the next line would throw.
    public TextPromptWindow() => InitializeComponent();

    public TextPromptWindow(string title, string prompt, string initialValue)
        : this()
    {
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initialValue;
        Input.TextChanged += OnTextChanged;

        Opened += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>The trimmed name. Meaningful only once the window has closed with Save.</summary>
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
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet build tests/TrispotQR.UI.Tests/TrispotQR.UI.Tests.csproj -c Debug` then the test exe.

Expected: 177 passing (171 + 6).

- [ ] **Step 6: Prove each test can fail**

For each of the six, break the thing it guards and confirm red:
- delete the `SelectAll()` call → test 1 fails
- make `OnConfirm` close unconditionally → tests 2 and 3 fail
- delete the `TextChanged` handler → test 4 fails
- drop the `.Trim()` → test 5 fails
- remove `MaxLength` → test 6 fails

Restore each afterwards. A test that stays green through its own mutation is not a test.

- [ ] **Step 7: Wire it into the dialog service**

In `src/TrispotQR.UI/Services/AvaloniaDialogService.cs`, replace:

```csharp
    /// <summary>Phase 2c, with the save-preset flow. Null reads as a cancelled dialog.</summary>
    public string? AskForText(string title, string prompt, string initialValue) => null;
```

with:

```csharp
    public string? AskForText(string title, string prompt, string initialValue)
    {
        // A question needs an answer now, so like Ask it cannot be queued. Null is the answer
        // that changes nothing, and is what MainViewModel already reads as a cancelled dialog.
        if (!CanShowDialog)
        {
            return null;
        }

        return DispatcherWait.For(
            TextPromptWindow.ShowAsync(_owner, title, prompt, initialValue), DialogTimeout);
    }
```

Also update the class doc comment: it currently says "Three members are not implemented in this phase". After this task it is two, and by Task 5 it is none — delete the paragraph then.

- [ ] **Step 8: Add a dialog-service test**

Append to `tests/TrispotQR.UI.Tests/DialogServiceTests.cs`, following the shape of the tests already there:

```csharp
    [AvaloniaFact]
    public void AskForTextReturnsNullBeforeTheOwnerIsOnScreen()
    {
        // MainViewModel's constructor can reach the dialog service before Show(), and Avalonia
        // refuses a modal on a non-visible owner. Null is a cancelled dialog, which is safe.
        var owner = new Window();
        var service = new AvaloniaDialogService(owner);

        Assert.Null(service.AskForText("Trispot QR", "Name this style", "Custom"));
    }
```

- [ ] **Step 9: Build the whole solution and confirm counts**

Run: `dotnet build TrispotQR.slnx -c Debug --nologo`, then run each test exe.

Expected: Core 415, WPF 220, UI 178, ViewModels 59 — **872 total**, zero warnings.

- [ ] **Step 10: Commit**

```bash
git add src/TrispotQR.UI/Views/TextPromptWindow.axaml \
        src/TrispotQR.UI/Views/TextPromptWindow.axaml.cs \
        src/TrispotQR.UI/Services/AvaloniaDialogService.cs \
        tests/TrispotQR.UI.Tests/TextPromptWindowTests.cs \
        tests/TrispotQR.UI.Tests/DialogServiceTests.cs
git commit -m "Ask for a style name instead of returning null"
```

---

### Task 2: Presets strip

**Files:**
- Modify: `src/TrispotQR.UI/MainWindow.axaml` (add the strip to the "How it looks" section)
- Modify: `tests/TrispotQR.UI.Tests/UiHarness.cs` (add `PresetButtons` helper)
- Test: `tests/TrispotQR.UI.Tests/PresetsStripTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.Presets` (`ObservableCollection<PresetItem>`), `ApplyPresetCommand` and `DeletePresetCommand` (`RelayCommand<PresetItem>`), `SavePresetCommand` (`RelayCommand`). `PresetItem` exposes `Name`, `Description`, `IsBuiltIn`, `ThumbnailDrawing` (`QrDrawing?`). `QrPreview` (`TrispotQR.UI.Controls`) with its `Drawing` property. `TextPromptWindow` from Task 1.
- Produces: named controls `PresetsStrip` (the `ItemsControl`) and `SavePresetButton`, which Task 6's final review and the manual checklist reference.

**Reference:** `src/TrispotQR.App/MainWindow.xaml` lines 288–330 and 505–520.

**Design notes:**
- WPF renders each thumbnail through a `QrDrawing`→`ImageSource` converter. Avalonia already has `QrPreview`, which draws a `QrDrawing` as vector geometry. Use `QrPreview` at 54×54 — no converter, no bitmap, and it stays crisp.
- A `PresetItem` with a null `ThumbnailDrawing` must render as an empty square rather than throwing. `QrPreview.Render` already returns early on null, so this needs no special handling — but it needs a test, because "already handles it" is exactly the kind of claim that turns out to be false.
- Deleting is on a context menu in WPF. Keep that, and keep it off built-in presets: `DeletePresetCommand`'s `CanExecute` should already refuse them, but the menu item should also be hidden so nobody right-clicks a built-in and finds a dead entry. Confirm what `DeletePresetCommand.CanExecute` actually does before deciding which of the two is load-bearing.

**Expected new tests:** 7.

- [ ] **Step 1: Write the failing tests**

Create `tests/TrispotQR.UI.Tests/PresetsStripTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The row of style cards. Each one is a real button over a real thumbnail, so these tests
/// drive clicks rather than commands wherever a click is what a user would do.
/// </summary>
public class PresetsStripTests
{
    [AvaloniaFact]
    public void EveryPresetTheModelOffersGetsACard()
    {
        UiHarness.WithWindow(session =>
        {
            var cards = PresetCards(session.Window).ToList();

            Assert.NotEmpty(session.Model.Presets);
            Assert.Equal(session.Model.Presets.Count, cards.Count);
        });
    }

    [AvaloniaFact]
    public void EachCardShowsItsOwnThumbnailAndName()
    {
        UiHarness.WithWindow(session =>
        {
            var first = session.Model.Presets[0];
            var card = PresetCards(session.Window).First();

            var preview = card.GetVisualDescendants().OfType<QrPreview>().Single();
            Assert.Same(first.ThumbnailDrawing, preview.Drawing);

            var label = card.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal(first.Name, label.Text);
        });
    }

    [AvaloniaFact]
    public void ClickingACardAppliesThatStyle()
    {
        UiHarness.WithWindow(session =>
        {
            // Find a preset whose module shape differs from the one in effect, so the
            // assertion cannot pass by accident on a style that was already applied.
            var target = session.Model.Presets
                .First(p => p.Preset.Style.ModuleShape != session.Model.ModuleShape);

            var card = PresetCards(session.Window)
                .Single(c => c.DataContext is TrispotQR.ViewModels.PresetItem item && item == target);

            card.BringIntoView();
            DispatcherPump.Drain();

            var point = UiHarness.At(card, 0.5, 0.5);
            Assert.True(
                point.X >= 0 && point.Y >= 0
                && point.X < session.Window.Width && point.Y < session.Window.Height,
                $"the card is at {point}, outside the window -- a headless click there is discarded "
                + "silently and this test would pass without ever pressing anything");

            UiHarness.Click(session.Window, point);

            Assert.Equal(target.Preset.Style.ModuleShape, session.Model.ModuleShape);
        });
    }

    [AvaloniaFact]
    public void ACardWithNoThumbnailRendersInsteadOfThrowing()
    {
        // A preset saved from a style that failed to encode has no thumbnail. It must still
        // take its place in the row rather than bringing the window down.
        UiHarness.WithWindow(session =>
        {
            session.Model.Presets.Add(new TrispotQR.ViewModels.PresetItem(
                new StylePreset("Broken", "No thumbnail", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null));

            DispatcherPump.Drain();

            var card = PresetCards(session.Window).Last();
            var preview = card.GetVisualDescendants().OfType<QrPreview>().Single();

            Assert.Null(preview.Drawing);
            Assert.True(preview.IsVisible);
        });
    }

    [AvaloniaFact]
    public void ABuiltInPresetOffersNoDeleteOption()
    {
        UiHarness.WithWindow(session =>
        {
            var builtIn = session.Model.Presets.First(p => p.IsBuiltIn);
            var card = PresetCards(session.Window)
                .Single(c => c.DataContext is TrispotQR.ViewModels.PresetItem item && item == builtIn);

            var item = card.ContextMenu?.Items.OfType<MenuItem>().FirstOrDefault();
            Assert.True(
                item is null || !item.IsVisible,
                "a built-in style cannot be deleted, so offering the option is a dead end");
        });
    }

    [AvaloniaFact]
    public void ASavedPresetCanBeDeleted()
    {
        UiHarness.WithWindow(session =>
        {
            session.Model.Presets.Add(new TrispotQR.ViewModels.PresetItem(
                new StylePreset("Mine", "A saved style", QrStyle.Default, IsBuiltIn: false),
                thumbnailDrawing: null));
            DispatcherPump.Drain();

            var before = session.Model.Presets.Count;
            var card = PresetCards(session.Window).Last();
            var item = card.ContextMenu!.Items.OfType<MenuItem>().Single();

            Assert.True(item.IsVisible, "a style the user saved must be removable");
            item.Command!.Execute(item.CommandParameter);
            DispatcherPump.Drain();

            Assert.Equal(before - 1, session.Model.Presets.Count);
        });
    }

    [AvaloniaFact]
    public void TheSaveStyleButtonIsBoundToTheCommandThatNeedsTheNamePrompt()
    {
        // The end-to-end path (click -> prompt -> new preset) needs a modal, which cannot be
        // driven from a headless test without blocking. What is provable here is that the
        // button reaches the command at all -- the part that was missing before Task 1.
        UiHarness.WithWindow(session =>
        {
            var button = session.Window.FindControl<Button>("SavePresetButton");
            Assert.NotNull(button);
            Assert.Same(session.Model.SavePresetCommand, button!.Command);
        });
    }

    private static IEnumerable<Button> PresetCards(Window window) =>
        window.FindControl<ItemsControl>("PresetsStrip")!
            .GetVisualDescendants()
            .OfType<Button>();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Expected: failures naming a missing `PresetsStrip` / `SavePresetButton`.

- [ ] **Step 3: Add the strip to MainWindow.axaml**

Inside the "How it looks" `StackPanel`, immediately after the `<TextBlock Text="How it looks" Classes="heading" />` line:

```xml
          <TextBlock Text="Pick a style. Every one of these has been tested to make sure it still scans."
                     Opacity="0.7" FontSize="12" TextWrapping="Wrap" />

          <ItemsControl x:Name="PresetsStrip" ItemsSource="{Binding Presets}">
            <ItemsControl.ItemsPanel>
              <ItemsPanelTemplate>
                <WrapPanel />
              </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
              <DataTemplate>
                <Button Margin="0,0,8,8" Padding="6"
                        ToolTip.Tip="{Binding Description}"
                        Command="{Binding $parent[ItemsControl].DataContext.ApplyPresetCommand}"
                        CommandParameter="{Binding}">
                  <StackPanel Width="64" Spacing="4">
                    <controls:QrPreview Drawing="{Binding ThumbnailDrawing}" Width="54" Height="54" />
                    <TextBlock Text="{Binding Name}" TextAlignment="Center" FontSize="11"
                               TextTrimming="CharacterEllipsis" />
                  </StackPanel>
                  <Button.ContextMenu>
                    <ContextMenu>
                      <MenuItem Header="Delete this saved style"
                                IsVisible="{Binding !IsBuiltIn}"
                                Command="{Binding $parent[ItemsControl].DataContext.DeletePresetCommand}"
                                CommandParameter="{Binding}" />
                    </ContextMenu>
                  </Button.ContextMenu>
                </Button>
              </DataTemplate>
            </ItemsControl.ItemTemplate>
          </ItemsControl>
```

**Verify before relying on it:** `{Binding $parent[ItemsControl].DataContext.X}` is Avalonia's equivalent of WPF's `RelativeSource AncestorType`. Confirm it resolves — and separately confirm a `ContextMenu` inside an `ItemTemplate` inherits the item's `DataContext`, which is what makes `CommandParameter="{Binding}"` mean the preset. If either does not hold, fall back to naming the window and binding through it, and say so in the report.

- [ ] **Step 4: Add the Save-this-style button**

At the bottom of the "How it looks" section, before its closing tag:

```xml
          <Button x:Name="SavePresetButton" Content="Save this style..."
                  Command="{Binding SavePresetCommand}" HorizontalAlignment="Left" />
```

- [ ] **Step 5: Add the harness helper**

In `tests/TrispotQR.UI.Tests/UiHarness.cs`, beside `AllBoxes`:

```csharp
    /// <summary>
    /// Every preset card in the strip, scoped to the ItemsControl so a window-wide Button
    /// search cannot pick up Save, Copy, Reset or a ComboBox template's internals.
    /// </summary>
    public static IEnumerable<Button> PresetCards(Window window) =>
        (window.FindControl<ItemsControl>("PresetsStrip")
            ?? throw new InvalidOperationException("MainWindow no longer has a PresetsStrip."))
        .GetVisualDescendants().OfType<Button>();
```

Then replace the private `PresetCards` in the test file with calls to this one.

- [ ] **Step 6: Run the tests to verify they pass**

Expected: 185 UI tests (178 + 7).

- [ ] **Step 7: Prove each test can fail**

Mutations to try, one at a time: bind the thumbnail to a constant; drop `CommandParameter`; delete `IsVisible="{Binding !IsBuiltIn}"`; unbind `SavePresetButton`. Each must turn exactly the test that claims to guard it red. If a mutation turns *nothing* red, that test is vacuous — fix it before moving on.

- [ ] **Step 8: Commit**

```bash
git add src/TrispotQR.UI/MainWindow.axaml \
        tests/TrispotQR.UI.Tests/UiHarness.cs \
        tests/TrispotQR.UI.Tests/PresetsStripTests.cs
git commit -m "Offer the style presets in the Avalonia window"
```

---

### Task 3: Logo picker

**Files:**
- Modify: `src/TrispotQR.UI/Services/AvaloniaDialogService.cs` (replace `AskForImage`)
- Modify: `src/TrispotQR.UI/MainWindow.axaml` (logo panel in the Advanced expander)
- Test: `tests/TrispotQR.UI.Tests/LogoPanelTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.ChooseLogoCommand`, `ClearLogoCommand`, `RaiseEccCommand`, and the properties `HasLogo`, `LogoName`, `LogoSize` (0.05–0.40), `LogoPunchShape`, `LogoPunchShapes`, `ShowRaiseEcc`. `FileFilter.Parse` and `DispatcherWait.For` from `TrispotQR.UI.Services`.
- Produces: named controls `ChooseLogoButton`, `ClearLogoButton`, `LogoDetail`, `LogoSizeSlider`, `LogoPunchShapeBox`, `RaiseEccButton`.

**Reference:** `src/TrispotQR.App/MainWindow.xaml` lines 476–495.

**Design note on `QrPreview`:** its doc comment says the logo "is not drawn here yet... loading it belongs with the logo picker in Phase 2c. Exports already include it." Drawing the logo in the live preview is therefore in scope for this task **if** it can be done cleanly; if it cannot, leave it, update that comment to say so plainly, and report it as a known difference from WPF. Do not let it block the picker itself — a logo that appears in the export but not the preview is a smaller problem than no picker at all.

**Expected new tests:** 6.

- [ ] **Step 1: Write the failing tests**

Create `tests/TrispotQR.UI.Tests/LogoPanelTests.cs`. Cover:

1. `ChooseLogoButton` is bound to `ChooseLogoCommand`.
2. `LogoDetail` is collapsed when `HasLogo` is false and visible when it is true (set the style through the model, drain, assert `IsVisible`).
3. `LogoSizeSlider` has `Minimum` 0.05 and `Maximum` 0.40 — the range the renderer was tested against.
4. `LogoPunchShapeBox.ItemsSource` is `LogoPunchShapes` and its `SelectedItem` round-trips to the model.
5. `RaiseEccButton` is hidden when `ShowRaiseEcc` is false, shown when true.
6. `AvaloniaDialogService.AskForImage` returns null before the owner is on screen (mirroring the Task 1 dialog-service test).

Write each with a real assertion on a rendered control, not on the view model alone — the view model is already covered by `TrispotQR.ViewModels.Tests`, so a test here that only reads the model proves nothing about the UI.

- [ ] **Step 2: Run the tests to verify they fail**

- [ ] **Step 3: Implement `AskForImage`**

Replace the placeholder in `AvaloniaDialogService.cs`:

```csharp
    public string? AskForImage(string? directory)
    {
        var options = new FilePickerOpenOptions
        {
            Title = "Choose a logo",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"],
                    // Set so the macOS panel does not grey out every file. Without an
                    // AppleUniformTypeIdentifiers entry the pattern list is ignored there.
                    AppleUniformTypeIdentifiers = ["public.image"],
                    MimeTypes = ["image/*"],
                },
                new FilePickerFileType("All files") { Patterns = ["*"] },
            ],
        };

        if (directory is not null)
        {
            // Same swallow as AskForSavePath, and for the same reason: a remembered directory
            // is never validated when written, and one containing an embedded NUL throws
            // rather than resolving to null. A lost start folder must not cost the picker.
            try
            {
                options.SuggestedStartLocation = DispatcherWait.For(
                    _owner.StorageProvider.TryGetFolderFromPathAsync(directory), DialogTimeout);
            }
            catch (Exception)
            {
                // Left null: the picker still opens, just without a preselected folder.
            }
        }

        if (!CanShowDialog)
        {
            return null;
        }

        var files = DispatcherWait.For(_owner.StorageProvider.OpenFilePickerAsync(options), DialogTimeout);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
```

**Verify:** that `OpenFilePickerAsync` returns `IReadOnlyList<IStorageFile>` in Avalonia 12.1.2, and that `FilePickerFileType` has the `AppleUniformTypeIdentifiers` and `MimeTypes` properties named above. Adapt if not; do not invent members.

- [ ] **Step 4: Add the logo panel to the Advanced expander**

Insert after the "Reliability" group, before the final `<Separator />` and Reset button:

```xml
            <Separator />
            <TextBlock Text="Logo" Classes="heading" />

            <StackPanel Orientation="Horizontal" Spacing="8">
              <Button x:Name="ChooseLogoButton" Content="Choose image..."
                      Command="{Binding ChooseLogoCommand}" />
              <Button x:Name="ClearLogoButton" Content="Remove"
                      Command="{Binding ClearLogoCommand}" />
              <TextBlock Text="{Binding LogoName}" VerticalAlignment="Center"
                         Opacity="0.7" FontSize="12" TextTrimming="CharacterEllipsis" />
            </StackPanel>

            <StackPanel x:Name="LogoDetail" Spacing="10" IsVisible="{Binding HasLogo}">
              <StackPanel Spacing="4">
                <TextBlock Text="Logo size" Classes="label" />
                <Slider x:Name="LogoSizeSlider" Minimum="0.05" Maximum="0.40"
                        Value="{Binding LogoSize}" />
              </StackPanel>

              <StackPanel Spacing="4">
                <TextBlock Text="Space around it" Classes="label" />
                <ComboBox x:Name="LogoPunchShapeBox" ItemsSource="{Binding LogoPunchShapes}"
                          SelectedItem="{Binding LogoPunchShape}"
                          ItemTemplate="{StaticResource FriendlyEnumTemplate}"
                          HorizontalAlignment="Stretch" />
              </StackPanel>

              <Button x:Name="RaiseEccButton" Content="Raise error correction to High"
                      Command="{Binding RaiseEccCommand}"
                      IsVisible="{Binding ShowRaiseEcc}"
                      HorizontalAlignment="Left" />
            </StackPanel>
```

- [ ] **Step 5: Run the tests to verify they pass**

Expected: 191 UI tests (185 + 6).

- [ ] **Step 6: Prove each test can fail**

- [ ] **Step 7: Commit**

```bash
git add src/TrispotQR.UI/Services/AvaloniaDialogService.cs \
        src/TrispotQR.UI/MainWindow.axaml \
        tests/TrispotQR.UI.Tests/LogoPanelTests.cs
git commit -m "Let a logo be chosen in the Avalonia window"
```

---

### Task 4: Theming

**Files:**
- Create: `src/TrispotQR.UI/Themes/Palette.axaml`
- Create: `src/TrispotQR.UI/Services/ThemeSwitcher.cs`
- Modify: `src/TrispotQR.UI/App.axaml` (merge the palette, remove the three inline resources)
- Test: `tests/TrispotQR.UI.Tests/ThemeTests.cs`

**Interfaces:**
- Consumes: `AppTheme` (`FollowWindows`, `Light`, `Dark`) from `TrispotQR.Core.Presets`.
- Produces: `ThemeSwitcher.Apply(AppTheme theme)` and `ThemeSwitcher.Requested { get; }`. Task 5 calls both.

**Reference:** `src/TrispotQR.App/Theme.Light.xaml`, `Theme.Dark.xaml`, `Services/ThemeManager.cs`. The two WPF palettes define an identical set of keys — verified. Port every one of them.

**Design decision — this is a simplification, not a port:** WPF swaps merged dictionary slot 0 at runtime and reads the Windows registry to decide what "follow the OS" means. Avalonia has this built in: `ResourceDictionary.ThemeDictionaries` holds a Light and a Dark set, `Application.Current.RequestedThemeVariant` selects between them, and `ThemeVariant.Default` already means "follow the OS" on every platform Avalonia supports. So `ThemeSwitcher` is a three-line mapping and there is **no registry read** — which is the whole point, since `Microsoft.Win32.Registry` would not work on macOS or Linux anyway.

**Verify this first, in a throwaway test, before writing anything else in this task:** that `ThemeDictionaries` resolve under `Avalonia.Headless` 12.1.2, and that changing `RequestedThemeVariant` at runtime re-resolves a `DynamicResource` already on screen. Both are load-bearing. If either fails headlessly, say so in the report and fall back to swapping a merged dictionary the way WPF does — the palette file and the public surface of `ThemeSwitcher` stay the same either way.

**Expected new tests:** 5.

- [ ] **Step 1: Verify the mechanism**

Write one throwaway `[AvaloniaFact]` that sets `Application.Current!.RequestedThemeVariant = ThemeVariant.Dark`, then reads a themed brush back through `TryGetResource` and asserts it differs from the light value. Run it. Do not proceed until it passes or you have a documented fallback.

- [ ] **Step 2: Write the failing tests**

Create `tests/TrispotQR.UI.Tests/ThemeTests.cs` covering:

1. Every key defined in the Light dictionary is also defined in the Dark one, and vice versa. Assert on the sets, not on a hand-written list — a missing key in one variant is the classic way a half-themed window ships.
2. `PageBrush` resolves to a different colour under Light than under Dark.
3. `ThemeSwitcher.Apply(AppTheme.Dark)` sets `RequestedThemeVariant` to `ThemeVariant.Dark`; `Light` to Light; `FollowWindows` to `Default`.
4. `ThemeSwitcher.Requested` reports what was last asked for, including `FollowWindows`.
5. `CheckerBrush` differs between the variants — the checkerboard is app chrome and must follow the theme, unlike the code's own colours which must not.

- [ ] **Step 3: Write the palette**

Create `src/TrispotQR.UI/Themes/Palette.axaml`. Port every key from the two WPF palettes. Note two substitutions:

- WPF's `DropShadowEffect` resource (`SoftShadow`) has no direct Avalonia resource equivalent; use `BoxShadow` on the elements that need it, or drop it and say so.
- The `CheckerBrush` currently in `App.axaml` moves here and gains a dark variant.

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <!--
    The app's own chrome, in two variants. Every key MUST exist in both; ThemeTests asserts
    that, because a key present in one variant only produces a window that is half themed.

    Only chrome lives here. The colours of a generated code come from the user's style and
    are deliberately untouched by the theme: it would be alarming if switching appearance
    quietly altered an export.

    Anything referring to these from XAML must use DynamicResource. A StaticResource resolves
    once when its element loads and would simply ignore a theme change.
  -->
  <ResourceDictionary.ThemeDictionaries>

    <ResourceDictionary x:Key="Light">
      <Color x:Key="AccentColor">#2B5CE6</Color>
      <SolidColorBrush x:Key="AccentBrush" Color="#2B5CE6" />
      <SolidColorBrush x:Key="AccentHoverBrush" Color="#1F49C4" />
      <SolidColorBrush x:Key="OnAccentBrush" Color="#FFFFFF" />
      <SolidColorBrush x:Key="PageBrush" Color="#F4F5F7" />
      <SolidColorBrush x:Key="SurfaceBrush" Color="#FFFFFF" />
      <SolidColorBrush x:Key="BorderBrush" Color="#DDE0E5" />
      <SolidColorBrush x:Key="SubtleBorderBrush" Color="#ECEEF1" />
      <SolidColorBrush x:Key="TextBrush" Color="#1B1F27" />
      <SolidColorBrush x:Key="MutedTextBrush" Color="#666E7A" />
      <SolidColorBrush x:Key="WarningBrush" Color="#B26100" />
      <SolidColorBrush x:Key="DangerBrush" Color="#C62828" />
      <SolidColorBrush x:Key="HoverBrush" Color="#F0F2F5" />
      <SolidColorBrush x:Key="DisabledBrush" Color="#C8CDD6" />
      <SolidColorBrush x:Key="DisabledTextBrush" Color="#A9B0BA" />

      <DrawingBrush x:Key="CheckerBrush" TileMode="Tile"
                    DestinationRect="0,0,16,16" Stretch="None">
        <DrawingBrush.Drawing>
          <DrawingGroup>
            <GeometryDrawing Brush="#FFFFFF" Geometry="M0,0 H16 V16 H0 Z" />
            <GeometryDrawing Brush="#E8EAED" Geometry="M0,0 H8 V8 H0 Z" />
            <GeometryDrawing Brush="#E8EAED" Geometry="M8,8 H16 V16 H8 Z" />
          </DrawingGroup>
        </DrawingBrush.Drawing>
      </DrawingBrush>
    </ResourceDictionary>

    <ResourceDictionary x:Key="Dark">
      <!-- Port every colour from src/TrispotQR.App/Theme.Dark.xaml here, key for key. -->
    </ResourceDictionary>

  </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

- [ ] **Step 4: Write ThemeSwitcher**

Create `src/TrispotQR.UI/Services/ThemeSwitcher.cs`:

```csharp
using Avalonia;
using Avalonia.Styling;
using TrispotQR.Core.Presets;

namespace TrispotQR.UI.Services;

/// <summary>
/// Applies the chosen appearance.
///
/// Much smaller than the WPF ThemeManager it replaces, because Avalonia does the work: the
/// palettes live in ThemeDictionaries and RequestedThemeVariant picks between them, so there
/// is no dictionary to swap. ThemeVariant.Default already means "follow the operating system"
/// on every platform, which is why there is no registry read here -- and there could not be
/// one, since this assembly has to run on macOS and Linux too.
/// </summary>
public static class ThemeSwitcher
{
    /// <summary>What was last asked for, which may still be FollowWindows.</summary>
    public static AppTheme Requested { get; private set; } = AppTheme.FollowWindows;

    public static void Apply(AppTheme theme)
    {
        Requested = theme;

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = theme switch
            {
                AppTheme.Light => ThemeVariant.Light,
                AppTheme.Dark => ThemeVariant.Dark,
                _ => ThemeVariant.Default,
            };
        }
    }
}
```

- [ ] **Step 5: Merge the palette into App.axaml**

Replace the three inline resources in `App.axaml` with a merge, keeping `RequestedThemeVariant="Default"` on the `Application` element:

```xml
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://TrispotQR.UI/Themes/Palette.axaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
```

Delete the `DangerBrush`, `WarningBrush` and `CheckerBrush` definitions that were there, along with the now-stale comments saying Phase 2e would replace them — it just did.

- [ ] **Step 6: Run the whole UI suite**

Expected: 196 UI tests (191 + 5), and **the 191 existing ones still green**. `CheckerBrush`, `DangerBrush` and `WarningBrush` just moved; `ColorPickerTests`, `FieldBoxTests` and `ValidationUiTests` all resolve them. If any of those break, the merge is wrong — fix the merge, do not adjust the tests.

- [ ] **Step 7: Prove each new test can fail**

Delete one key from the Dark dictionary → test 1 red. Make both variants the same colour → tests 2 and 5 red. Make `Apply` ignore its argument → tests 3 and 4 red.

- [ ] **Step 8: Commit**

```bash
git add src/TrispotQR.UI/Themes/Palette.axaml \
        src/TrispotQR.UI/Services/ThemeSwitcher.cs \
        src/TrispotQR.UI/App.axaml \
        tests/TrispotQR.UI.Tests/ThemeTests.cs
git commit -m "Give the Avalonia app a light and a dark palette"
```

---

### Task 5: SettingsWindow

**Files:**
- Create: `src/TrispotQR.UI/Views/SettingsWindow.axaml(.cs)`
- Modify: `src/TrispotQR.UI/Services/AvaloniaDialogService.cs` (replace `EditSettings`, delete the stale class comment)
- Test: `tests/TrispotQR.UI.Tests/SettingsWindowTests.cs`

**Interfaces:**
- Consumes: `AppSettings` (record: `Theme`, `WarnOnRiskyCodes`, `RememberLastStyle`, `DefaultPixelSize`, `DefaultSaveDirectory`), `AppSettings.Default`, `ThemeSwitcher.Apply` from Task 4.
- Produces: `SettingsWindow.ShowAsync(Window owner, AppSettings current) -> Task<AppSettings?>`.

**Reference:** `src/TrispotQR.App/Views/SettingsWindow.xaml(.cs)`.

**Behaviour to preserve exactly:**
- Theme applies **as soon as it is chosen**, not on Done — the point of picking an appearance is seeing it.
- Closing with the title-bar X, or Cancel, restores the theme that was in effect on open. WPF does this in `OnClosed`; do the same.
- "Reset to defaults" resets the controls and applies `FollowWindows` live, but does **not** close the window.
- Size is three radio buttons mapping to 512 / 1024 / 2048. Anything not 512 or 2048 shows as Medium.
- An empty folder box means `null`, not `""` — `null` is what makes the app follow `LastSaveDirectory`.

**Folder picker:** WPF uses `Microsoft.Win32.OpenFolderDialog`. Use `IStorageProvider.OpenFolderPickerAsync` with `AllowMultiple = false`, and `TryGetLocalPath()` on the result.

**Expected new tests:** 8.

- [ ] **Step 1: Write the failing tests**

Create `tests/TrispotQR.UI.Tests/SettingsWindowTests.cs` covering:

1. Every setting handed in is reflected in the controls on open (theme index, both check boxes, the right size radio, the folder text).
2. A `DefaultPixelSize` that is neither 512 nor 2048 selects Medium.
3. Choosing Dark in the combo applies it immediately — assert `ThemeSwitcher.Requested` (and `Application.Current.RequestedThemeVariant`) change before anything is clicked.
4. Cancelling restores the theme that was in effect on open.
5. Closing via the title bar (call `Close()` directly) also restores it — a separate path from Cancel, and the one WPF needed `OnClosed` for.
6. Done returns an `AppSettings` carrying every edit.
7. An empty folder box yields `DefaultSaveDirectory == null`, not empty string.
8. Reset to defaults resets the controls, applies `FollowWindows`, and leaves the window open.

Restore `ThemeSwitcher`'s state in a `finally` in every test that touches it — these run in one process and a leaked dark theme will make a later test fail for the wrong reason.

- [ ] **Step 2: Run the tests to verify they fail**

- [ ] **Step 3: Write the window**

Port the WPF markup, substituting Avalonia idiom: `IsVisible` for `Visibility`, `Spacing` on panels for per-child `Margin`, `Classes="heading"` / `Classes="label"` to match `MainWindow.axaml`'s conventions. Name every control the tests reach: `ThemeChoice`, `WarnRisky`, `RememberStyle`, `SaveFolder`, `SizeSmall`, `SizeMedium`, `SizePrint`, `BrowseButton`, `ClearFolderButton`, `ResetDefaultsButton`, `DoneButton`.

- [ ] **Step 4: Write the code-behind**

Follow `src/TrispotQR.App/Views/SettingsWindow.xaml.cs` closely, with two changes: `ThemeSwitcher` in place of `ThemeManager`, and `OpenFolderPickerAsync` in place of `OpenFolderDialog`. Keep the `_loaded` guard — without it, assigning the initial values in the constructor counts as the user choosing them and fires a live theme change during construction.

- [ ] **Step 5: Implement `EditSettings`**

```csharp
    public AppSettings? EditSettings(AppSettings current)
    {
        if (!CanShowDialog)
        {
            return null;
        }

        return DispatcherWait.For(SettingsWindow.ShowAsync(_owner, current), DialogTimeout);
    }
```

Then delete the class-level paragraph beginning "Three members are not implemented in this phase" — with this task none are.

- [ ] **Step 6: Run the tests to verify they pass**

Expected: 204 UI tests (196 + 8).

- [ ] **Step 7: Prove each test can fail**

In particular: remove the `OnClosed` restore and confirm tests 4 **and** 5 go red. If only one does, the two paths are not actually distinct and one of the tests is redundant — say which, in the report.

- [ ] **Step 8: Commit**

```bash
git add src/TrispotQR.UI/Views/SettingsWindow.axaml \
        src/TrispotQR.UI/Views/SettingsWindow.axaml.cs \
        src/TrispotQR.UI/Services/AvaloniaDialogService.cs \
        tests/TrispotQR.UI.Tests/SettingsWindowTests.cs
git commit -m "Let preferences be edited in the Avalonia app"
```

---

### Task 6: AboutWindow and the gear menu

**Files:**
- Create: `src/TrispotQR.UI/Services/AppInfo.cs`
- Create: `src/TrispotQR.UI/Views/AboutWindow.axaml(.cs)`
- Modify: `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj` (add `<Version>` and `<Product>`)
- Modify: `src/TrispotQR.UI/MainWindow.axaml(.cs)` (gear button, menu, version label; remove the placeholder note)
- Test: `tests/TrispotQR.UI.Tests/AboutWindowTests.cs`

**Interfaces:**
- Consumes: `MainViewModel.OpenSettings()`, `AppInfo.ProductName` / `Version` / `DisplayVersion`.
- Produces: nothing later depends on this; it is the last task.

**A real gap to close first:** `src/TrispotQR.Desktop/TrispotQR.Desktop.csproj` has **no `<Version>` and no `<Product>`** — only `<AssemblyName>TrispotQR</AssemblyName>`. The WPF csproj carries `<Version>1.0.0</Version>` and `<Product>Trispot QR</Product>`. Add both to Desktop, matching WPF exactly. Without them the About window and the title bar would report `1.0.0` only by falling through `AppInfo`'s default, which is a coincidence rather than a fact.

**Which assembly does `AppInfo` read?** WPF's reads `typeof(AppInfo).Assembly` — its own. Ported unchanged into `TrispotQR.UI`, it would read `TrispotQR.UI.dll`, whose version is not the one being set on Desktop. Read the **entry assembly** instead, falling back to the declaring assembly when there is none (which is the case under a test runner):

```csharp
var assembly = Assembly.GetEntryAssembly() ?? typeof(AppInfo).Assembly;
```

State this reasoning in the file's doc comment, and note that a test-runner build will therefore report the runner's version — which is why no test asserts a specific number.

**Expected new tests:** 5.

- [ ] **Step 1: Write the failing tests**

Create `tests/TrispotQR.UI.Tests/AboutWindowTests.cs` covering:

1. `AppInfo.ProductName` is `"Trispot QR"`.
2. `AppInfo.Version` matches `^\d+\.\d+\.\d+$` — a shape assertion, not a value one, for the entry-assembly reason above.
3. `AppInfo.DisplayVersion` is `AppInfo.Version` prefixed with `v`.
4. The About window shows the product name and `DisplayVersion`.
5. `MainWindow` has a `GearButton` whose menu carries a Settings item and an About item.

- [ ] **Step 2: Run the tests to verify they fail**

- [ ] **Step 3: Add the version properties to Desktop.csproj**

```xml
    <Version>1.0.0</Version>
    <Product>Trispot QR</Product>
```

- [ ] **Step 4: Write AppInfo**

Port `src/TrispotQR.App/Services/AppInfo.cs` into `src/TrispotQR.UI/Services/AppInfo.cs` with the entry-assembly change above, keeping the explanation of why `InformationalVersion` is trimmed at `+`.

- [ ] **Step 5: Write AboutWindow**

Port `src/TrispotQR.App/Views/AboutWindow.xaml`. Keep it to product name, version, a one-line description and a Close button. If the WPF version has a clickable link, Avalonia has no `Hyperlink`; use a `Button` with a hyperlink-ish style opening the URL through `Process.Start` with `UseShellExecute = true`, and confirm that is what the WPF one actually does before adding it.

- [ ] **Step 6: Add the gear button and menu to MainWindow**

WPF draws the gear as a `Path` so no icon font ships. Port that `Data` string verbatim — it is already vector data and Avalonia's `Path.Data` takes the same mini-language. Put the button and the version label in the top-right of the form column.

`Click` handlers in `MainWindow.axaml.cs`:

```csharp
    private void OnSettingsClicked(object? sender, RoutedEventArgs e)
    {
        // OpenSettings is on the view model because deciding what to persist afterwards is
        // its job; showing the window is the dialog service's, which it reaches through
        // EditSettings.
        (DataContext as MainViewModel)?.OpenSettings();
    }

    private async void OnAboutClicked(object? sender, RoutedEventArgs e) =>
        await new AboutWindow().ShowDialog(this);
```

- [ ] **Step 7: Remove the placeholder note**

Delete these two lines from the bottom of `MainWindow.axaml`:

```xml
        <TextBlock TextWrapping="Wrap" Opacity="0.6" FontSize="12"
                   Text="Presets, the logo picker and settings are still in the Windows build. They arrive here next." />
```

They are no longer true, and leaving them would be the most visible possible lie in the app.

- [ ] **Step 8: Run the whole solution**

Expected: Core 415, WPF **exactly 220**, UI 209, ViewModels 59 — **903 total**, zero warnings.

- [ ] **Step 9: Prove each new test can fail**

- [ ] **Step 10: Commit**

```bash
git add src/TrispotQR.UI/Services/AppInfo.cs \
        src/TrispotQR.UI/Views/AboutWindow.axaml \
        src/TrispotQR.UI/Views/AboutWindow.axaml.cs \
        src/TrispotQR.Desktop/TrispotQR.Desktop.csproj \
        src/TrispotQR.UI/MainWindow.axaml \
        src/TrispotQR.UI/MainWindow.axaml.cs \
        tests/TrispotQR.UI.Tests/AboutWindowTests.cs
git commit -m "Reach settings and about from the Avalonia window"
```

---

## Verification

**Automated.** Build the solution and run each test executable directly. Expected final state: **903 tests, 0 failures, 0 warnings**, with WPF still at exactly 220. Then push and confirm all four CI jobs green (Ubuntu, macOS, Windows ×2).

**Manual, on the running app.** These cannot be automated and only the user can do them:

1. Launch, click each preset card, confirm the preview changes and the badge stays green.
2. Save a style with a name; confirm it appears in the strip and survives a restart.
3. Right-click a built-in preset — no delete option. Right-click the saved one — delete works.
4. Add a logo, confirm the "raise error correction" button appears, click it, confirm the code still scans on a phone.
5. Switch to Dark in Settings; confirm the whole window changes, including the checkerboard behind the preview, and that the QR code's own colours do not.
6. Open Settings, switch to Dark, close with the title-bar X; confirm the theme goes back.
7. Set a default save folder; confirm the next Save opens there.
8. Open About; confirm the version matches the csproj.
9. Resize the window small enough to scroll and confirm the scrollbar still clears the form (the Phase 2d fix, re-checked now that the panel is taller).

## Carried Forward

Not in scope here; they follow the port rather than blocking it:

- The clipboard's second flattened-on-white entry (from Phase 2b).
- A build-only CI step for `tools/DispatcherProbe` (from Phase 2b).
- Two theme-dependent validation tests still living in the WPF suite — these become live again in Phase 2f, since retiring WPF is where they have to go somewhere or be deleted.
- The 11 unticked manual checks in `docs/superpowers/checklists/2026-09-08-phase2b-manual-checks.md`.

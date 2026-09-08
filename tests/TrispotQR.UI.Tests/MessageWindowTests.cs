using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using TrispotQR.UI.Services;
using TrispotQR.UI.Views;

namespace TrispotQR.UI.Tests;

public class MessageWindowTests
{
    [AvaloniaFact]
    public void ReturnsFalseWhenDismissedByTheTitleBarOrEscapeRatherThanConfirm()
    {
        var (dialog, task) = ShowRiskWarning();

        // Stands in for the title bar X or Escape: both dismiss the window without ever
        // running OnConfirm, the only place _confirmed is set to true. This is the path that
        // matters most for ConfirmRisk -- reading a dismissed "may not scan" warning as
        // permission to proceed would export something the user just declined.
        dialog.Close();

        var confirmed = DispatcherWait.For(task, TimeSpan.FromSeconds(5));

        Assert.False(confirmed);
    }

    [AvaloniaFact]
    public void ReturnsTrueWhenTheConfirmButtonIsActuallyClicked()
    {
        // Paired with the dismissal test above on purpose. That test alone cannot tell a
        // correctly-wired dialog apart from a ShowAsync that silently always returns false --
        // both would show "returns false", and a permanently stuck ConfirmRisk would pass it
        // right along with a working one. Only a test proving the confirm path returns true
        // makes the pair actually discriminate.
        var (dialog, task) = ShowRiskWarning();

        // A real simulated mouse click on the rendered Confirm button, not a direct call to
        // OnConfirm -- this is what was actually broken before InitializeComponent() replaced
        // AvaloniaXamlLoader.Load(this): the button existed and looked wired in the XAML, but
        // the field the handler is attached through was never populated. Only driving the
        // click through Avalonia's own hit-testing and routed-event pipeline (not calling the
        // handler method directly) would have caught that.
        ClickConfirmButton(dialog);

        var confirmed = DispatcherWait.For(task, TimeSpan.FromSeconds(5));

        Assert.True(confirmed);
    }

    /// <summary>
    /// Opens a MessageWindow with both buttons present (the ConfirmRisk shape) and pumps until
    /// it is actually there. ShowDialog defers the child window's appearance in
    /// Owner.OwnedWindows by a frame or two rather than adding it in the same synchronous slice
    /// that creates the Task, so this polls (bounded, so a genuine regression fails fast rather
    /// than hanging).
    /// </summary>
    private static (MessageWindow Dialog, Task<bool> Task) ShowRiskWarning()
    {
        var owner = new Window { Width = 400, Height = 300 };
        owner.Show();

        var task = MessageWindow.ShowAsync(owner, "Risky code", "This code may not scan.", "Save anyway", "Cancel", defaultToConfirm: false);

        MessageWindow? dialog = null;
        DispatcherPump.DrainUntil(() => (dialog = owner.OwnedWindows.OfType<MessageWindow>().SingleOrDefault()) is not null);

        Assert.NotNull(dialog);
        return (dialog, task);
    }

    /// <summary>
    /// Simulates a real pointer click on the Confirm button through Avalonia's headless input
    /// API, going through actual hit-testing rather than calling the Click handler directly.
    /// ConfirmButton is an internal field (Avalonia's default field modifier for a named
    /// element), so it is reached by reflection rather than a direct reference from this
    /// (different-assembly) test project -- the same route AvaloniaDialogService itself does
    /// not need, since MessageWindow.axaml.cs lives in the assembly that declares the field.
    /// </summary>
    private static void ClickConfirmButton(MessageWindow dialog)
    {
        var field = typeof(MessageWindow).GetField("ConfirmButton", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("MessageWindow no longer has a ConfirmButton field.");
        var confirmButton = (Button)field.GetValue(dialog)!;

        // Force layout so the button has real, non-zero bounds to click.
        DispatcherPump.Drain();

        // GetTransformedBounds gives the button's bounds and its transform into the window's own
        // coordinate space (there is no TranslatePoint on Visual in this Avalonia version) --
        // the transform's translation is exactly the button's on-screen origin within the
        // window, which is what MouseDown/MouseUp need.
        var transformed = confirmButton.GetTransformedBounds()
            ?? throw new InvalidOperationException("ConfirmButton has no transformed bounds -- it never got a layout pass.");
        var center = new Point(
            transformed.Transform.M31 + (transformed.Bounds.Width / 2),
            transformed.Transform.M32 + (transformed.Bounds.Height / 2));

        dialog.MouseDown(center, MouseButton.Left);
        dialog.MouseUp(center, MouseButton.Left);

        DispatcherPump.Drain();
    }
}

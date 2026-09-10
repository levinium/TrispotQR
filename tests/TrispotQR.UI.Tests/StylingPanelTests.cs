using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using TrispotQR.Core.Primitives;
using TrispotQR.Core.Qr;
using TrispotQR.Core.Rendering;
using TrispotQR.Core.Styling;
using TrispotQR.UI.Controls;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The styling panel: the controls that decide what a code looks like.
///
/// Every test here drives the rendered control and reads the view model, or drives the view
/// model and reads the rendered drawing. Neither direction is optional. A one-way binding on
/// any of these twenty-odd controls would leave the app looking complete and doing nothing,
/// and a panel checked only by "does the window still lay out" would not notice.
///
/// The colour arithmetic, the shapes and the scannability rules are not tested here. They have
/// their own suites in Core and need no window. What is tested here is the wiring: that each
/// control reaches the property it claims to, that the conditional sections really appear and
/// disappear, and that turning something off takes it out of the code rather than merely
/// hiding its controls.
/// </summary>
public class StylingPanelTests
{
    private static readonly RgbColor Navy = RgbColor.FromRgb(0x1B, 0x2A, 0x4A);
    private static readonly RgbColor Crimson = RgbColor.FromRgb(0x9B, 0x1B, 0x30);

    private static T Named<T>(Window window, string name)
        where T : Control =>
        window.FindControl<T>(name) ?? throw new InvalidOperationException($"MainWindow has no {name}.");

    /// <summary>
    /// Content, drawn. Most of what this file asserts is a property of the rendered drawing,
    /// and an empty plain-text editor produces no drawing at all.
    /// </summary>
    private static void GiveItSomethingToDraw(UiHarness.Session session)
    {
        var editor = session.Model.ContentEditors.OfType<PlainTextEditor>().Single();
        session.Model.SelectedContent = editor;
        editor.Text = "https://www.emanuelnyc.org";

        // RefreshNow skips the debounce timer the app uses in normal running; waiting out a real
        // 150ms tick here would make every test in this file slow for no gain.
        session.Model.RefreshNow();
        DispatcherPump.Drain();
    }

    /// <summary>
    /// Opens the advanced section and waits for its contents to be laid out. Nothing inside it
    /// exists in the visual tree until it is expanded, so a test that skipped this would search
    /// an empty panel and pass on nothing.
    /// </summary>
    private static void OpenAdvanced(UiHarness.Session session)
    {
        Named<Expander>(session.Window, "AdvancedOptions").IsExpanded = true;

        Assert.True(
            DispatcherPump.DrainUntil(() => Named<ComboBox>(session.Window, "ModuleShapeBox").Bounds.Width > 0),
            "the advanced options never laid themselves out");
    }

    /// <summary>
    /// Clicks a control the way a user does, scrolling to it first.
    ///
    /// The scroll is not a nicety. This panel is taller than the window, so most of the advanced
    /// section starts outside the viewport, and a click sent to a point beyond the window's
    /// bounds is simply discarded: eleven tests failed here with nothing happening at all and no
    /// error of any kind. The assertion is what keeps that from being a silent pass if the
    /// layout ever changes again.
    /// </summary>
    private static void Press(UiHarness.Session session, Control control, double fractionX = 0.5, double fractionY = 0.5)
    {
        control.BringIntoView();
        DispatcherPump.Drain();

        var point = UiHarness.At(control, fractionX, fractionY);

        Assert.True(
            point.X >= 0 && point.Y >= 0 && point.X < session.Window.Width && point.Y < session.Window.Height,
            $"{control.Name ?? control.GetType().Name} is at {point}, outside the window, so a click would go nowhere");

        UiHarness.Click(session.Window, point);
    }

    /// <summary>Clicks a check box the way a user does, and settles.</summary>
    private static void Toggle(UiHarness.Session session, string name)
    {
        Press(session, Named<CheckBox>(session.Window, name));
        DispatcherPump.Drain();
    }

    /// <summary>The redrawn code, forced past the debounce.</summary>
    private static QrDrawing Redraw(UiHarness.Session session)
    {
        session.Model.RefreshNow();
        DispatcherPump.Drain();

        return session.Model.PreviewDrawing
            ?? throw new InvalidOperationException("the styling change left nothing drawn at all");
    }

    private static QrLayer Layer(QrDrawing drawing, string name) =>
        drawing.Layers.Single(l => l.Name == name);

    /// <summary>
    /// What a ComboBox is actually showing for its current selection. The selection box renders
    /// through the same ItemTemplate the dropdown list does, so reading it covers every option
    /// without opening a popup into a visual root a window-scoped search cannot reach.
    /// </summary>
    private static string? Showing(ComboBox combo) =>
        combo.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text)
            .FirstOrDefault(t => !string.IsNullOrEmpty(t));

    /// <summary>Every slider a user can currently see. Deliberately not every slider that
    /// exists: the colour pickers keep three of their own inside closed popups.</summary>
    private static List<Slider> VisibleSliders(Window window) =>
        window.GetVisualDescendants().OfType<Slider>().Where(s => s.IsEffectivelyVisible).ToList();

    #region The controls reach the model

    [AvaloniaFact]
    public void TheCodeColourPickerCarriesAColourBothWays()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            var picker = Named<ColorPicker>(session.Window, "ForegroundPicker");

            // Out of the control and into the model, which is the direction that would be
            // silently lost if the binding were one-way.
            picker.SelectedColor = Navy;
            DispatcherPump.Drain();
            Assert.Equal(Navy, session.Model.Foreground);

            // And back in, so a preset or a reset reaches the swatch.
            session.Model.Foreground = Crimson;
            DispatcherPump.Drain();
            Assert.Equal(Crimson, picker.SelectedColor);
        });
    }

    [AvaloniaFact]
    public void TheCodeColourReachesTheDrawing()
    {
        // The picker could round-trip perfectly and still be bound to something the renderer
        // never reads. This is the only assertion that rules that out.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            Named<ColorPicker>(session.Window, "ForegroundPicker").SelectedColor = Navy;
            DispatcherPump.Drain();

            Assert.Equal(Navy, Layer(Redraw(session), QrLayerNames.Modules).Fill);
        });
    }

    [AvaloniaFact]
    public void ChoosingATransparentBackgroundLeavesTheCodeWithNoBackgroundAtAll()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            Assert.Equal(RgbColor.White, Redraw(session).Background);

            Press(session, Named<RadioButton>(session.Window, "BackgroundTransparent"));
            DispatcherPump.Drain();

            Assert.Equal(BackgroundChoice.Transparent, session.Model.BackgroundChoice);
            Assert.Null(Redraw(session).Background);
        });
    }

    [AvaloniaFact]
    public void ChoosingAWhiteBackgroundPutsItBack()
    {
        // The other half of the pair. A group of radio buttons that all wrote Transparent would
        // pass the test above on its own.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);

            Press(session, Named<RadioButton>(session.Window, "BackgroundTransparent"));
            DispatcherPump.Drain();
            Press(session, Named<RadioButton>(session.Window, "BackgroundWhite"));
            DispatcherPump.Drain();

            Assert.Equal(BackgroundChoice.White, session.Model.BackgroundChoice);
            Assert.Equal(RgbColor.White, Redraw(session).Background);
        });
    }

    [AvaloniaFact]
    public void ACustomBackgroundColourReachesTheDrawing()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);

            Press(session, Named<RadioButton>(session.Window, "BackgroundCustom"));
            DispatcherPump.Drain();

            Named<ColorPicker>(session.Window, "CustomBackgroundPicker").SelectedColor = Navy;
            DispatcherPump.Drain();

            Assert.Equal(Navy, Redraw(session).Background);
        });
    }

    [AvaloniaFact]
    public void EachExportSizeButtonSetsTheSizeItNames()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);

            foreach (var (name, pixels) in new[] { ("SizeSmall", 512), ("SizePrint", 2048), ("SizeMedium", 1024) })
            {
                Press(session, Named<RadioButton>(session.Window, name));
                DispatcherPump.Drain();

                Assert.Equal(pixels, session.Model.PixelSize);
            }
        });
    }

    [AvaloniaFact]
    public void EveryShapeDropdownSetsTheShapeItShows()
    {
        // Driven from the control's own SelectedItem, not the model's property: that is the
        // direction a one-way ItemsSource binding would break, and the direction a user goes.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            Named<ComboBox>(session.Window, "ModuleShapeBox").SelectedItem = ModuleShape.Circle;
            Named<ComboBox>(session.Window, "MarkerFrameShapeBox").SelectedItem = MarkerFrameShape.Leaf;
            Named<ComboBox>(session.Window, "MarkerCenterShapeBox").SelectedItem = MarkerCenterShape.Circle;
            DispatcherPump.Drain();

            Assert.Equal(ModuleShape.Circle, session.Model.ModuleShape);
            Assert.Equal(MarkerFrameShape.Leaf, session.Model.MarkerFrameShape);
            Assert.Equal(MarkerCenterShape.Circle, session.Model.MarkerCenterShape);

            // And the code is genuinely redrawn from them, rather than the properties merely
            // being set. Squares are drawn from straight lines alone; round dots are not, so a
            // curve appearing in the module layer is the shape arriving at the renderer.
            Assert.True(HasCurves(Layer(Redraw(session), QrLayerNames.Modules)), "the dots are still square");
        });
    }

    private static bool HasCurves(QrLayer layer) =>
        layer.Path.Figures.SelectMany(f => f.Segments).Any(s => s is QrArcTo or QrCubicTo);

    /// <summary>
    /// How wide one drawn shape is, in module units. Only the segment endpoints are considered,
    /// which is exact for the straight-edged square this is used on.
    /// </summary>
    private static double FigureWidth(QrFigure figure)
    {
        var xs = figure.Segments
            .Select(s => s switch
            {
                QrLineTo line => line.To.X,
                QrArcTo arc => arc.To.X,
                QrCubicTo cubic => cubic.To.X,
                _ => figure.Start.X,
            })
            .Append(figure.Start.X)
            .ToList();

        return xs.Max() - xs.Min();
    }

    [AvaloniaFact]
    public void TheErrorCorrectionDropdownSetsTheLevelItShows()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            var atMedium = Redraw(session).SizeInUnits;

            Named<ComboBox>(session.Window, "EccBox").SelectedItem = EccLevel.High;
            DispatcherPump.Drain();

            Assert.Equal(EccLevel.High, session.Model.Ecc);

            // Stronger correction spends more of the code on recovery data, so the same payload
            // needs a bigger code. That is the user-visible consequence, and it proves the
            // setting reached the encoder rather than only the view model.
            Assert.True(
                Redraw(session).SizeInUnits > atMedium,
                "raising the error correction did not make the code any bigger, so it never reached the encoder");
        });
    }

    [AvaloniaFact]
    public void TheOutlineControlsReachTheDrawing()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            Assert.Null(Layer(Redraw(session), QrLayerNames.Modules).Stroke);

            Toggle(session, "OutlineEnabledBox");
            Named<ColorPicker>(session.Window, "OutlinePicker").SelectedColor = Crimson;
            Named<ComboBox>(session.Window, "OutlineTargetBox").SelectedItem = OutlineTarget.Modules;
            Named<Slider>(session.Window, "OutlineThicknessSlider").Value = 0.2;
            DispatcherPump.Drain();

            var stroke = Layer(Redraw(session), QrLayerNames.Modules).Stroke;
            Assert.NotNull(stroke);
            Assert.Equal(Crimson, stroke!.Color);
            Assert.Equal(0.2, stroke.Thickness, 3);

            // "Dots only" has to mean dots only, or the target dropdown is decoration.
            Assert.Null(Layer(session.Model.PreviewDrawing!, QrLayerNames.MarkerFrames).Stroke);
        });
    }

    [AvaloniaFact]
    public void TheCornerColourPickersReachTheDrawing()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            Toggle(session, "UseCustomMarkerColorsBox");

            Named<ColorPicker>(session.Window, "MarkerFramePicker").SelectedColor = Crimson;
            Named<ColorPicker>(session.Window, "MarkerCenterPicker").SelectedColor = Navy;
            DispatcherPump.Drain();

            var drawing = Redraw(session);
            Assert.Equal(Crimson, Layer(drawing, QrLayerNames.MarkerFrames).Fill);
            Assert.Equal(Navy, Layer(drawing, QrLayerNames.MarkerCenters).Fill);

            // The data modules are not corners and must have stayed black.
            Assert.Equal(RgbColor.Black, Layer(drawing, QrLayerNames.Modules).Fill);
        });
    }

    [AvaloniaFact]
    public void TheGapSliderReachesTheDrawing()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            // One module fills its whole cell at the default scale, which is what makes the
            // classic look solid.
            var full = FigureWidth(Layer(Redraw(session), QrLayerNames.Modules).Path.Figures[0]);
            Assert.Equal(1.0, full, 3);

            Named<Slider>(session.Window, "ModuleScaleSlider").Value = 0.55;
            DispatcherPump.Drain();

            // A smaller module leaves a gap around itself. Measuring the drawn shape is what
            // makes this about the picture rather than about the property.
            var gapped = FigureWidth(Layer(Redraw(session), QrLayerNames.Modules).Path.Figures[0]);

            Assert.Equal(0.55, session.Model.ModuleScale, 3);
            Assert.Equal(0.55, gapped, 3);
        });
    }

    [AvaloniaFact]
    public void TheMarginSliderReachesTheDrawing()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            var standard = Redraw(session).SizeInUnits;

            Named<Slider>(session.Window, "QuietZoneSlider").Value = 0;
            DispatcherPump.Drain();

            // Four modules of clear space on each side is eight modules of canvas.
            Assert.Equal(0, session.Model.QuietZone);
            Assert.Equal(standard - 8, Redraw(session).SizeInUnits, 3);
        });
    }

    [AvaloniaFact]
    public void TheMarginSliderOnlyEverOffersWholeModules()
    {
        // A quiet zone is counted in modules; three and a half of them is not a thing the
        // renderer can draw, and the WPF original snaps for exactly that reason.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            var slider = Named<Slider>(session.Window, "QuietZoneSlider");
            slider.Value = 0;
            DispatcherPump.Drain();

            Press(session, slider, 0.42);

            Assert.Equal(Math.Round(slider.Value), slider.Value);
            Assert.Equal((int)slider.Value, session.Model.QuietZone);
        });
    }

    #endregion

    #region The conditional sections

    [AvaloniaFact]
    public void TheCustomBackgroundSwatchIsShownOnlyForACustomBackground()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            var swatch = Named<ColorPicker>(session.Window, "CustomBackgroundPicker");

            Assert.False(swatch.IsEffectivelyVisible, "the custom swatch is showing for a white background");

            Press(session, Named<RadioButton>(session.Window, "BackgroundCustom"));
            DispatcherPump.Drain();

            Assert.True(swatch.IsEffectivelyVisible, "choosing Custom left no way to pick the colour");
        });
    }

    [AvaloniaFact]
    public void TheCornerColourRowIsShownOnlyWhenTheCornersHaveTheirOwnColours()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            var row = Named<StackPanel>(session.Window, "MarkerColorsRow");

            Assert.False(row.IsEffectivelyVisible);

            Toggle(session, "UseCustomMarkerColorsBox");
            Assert.True(session.Model.UseCustomMarkerColors, "clicking the box did not reach the model");
            Assert.True(row.IsEffectivelyVisible, "the corner colour pickers never appeared");
        });
    }

    [AvaloniaFact]
    public void TurningOffTheCornerColoursTakesThemOutOfTheCodeRatherThanHidingTheRow()
    {
        // The stale-value case. Hiding the row while the style kept a crimson ring would leave
        // the user looking at a colour they can no longer see the control for, and no test that
        // only checked IsVisible would notice.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            Toggle(session, "UseCustomMarkerColorsBox");
            Named<ColorPicker>(session.Window, "MarkerFramePicker").SelectedColor = Crimson;
            DispatcherPump.Drain();
            Assert.Equal(Crimson, Layer(Redraw(session), QrLayerNames.MarkerFrames).Fill);

            Toggle(session, "UseCustomMarkerColorsBox");

            var drawing = Redraw(session);
            Assert.False(Named<StackPanel>(session.Window, "MarkerColorsRow").IsEffectivelyVisible);
            Assert.Equal(RgbColor.Black, Layer(drawing, QrLayerNames.MarkerFrames).Fill);
            Assert.True(session.Model.CanExport, session.Model.ExportBlockedReason ?? "export was blocked");
        });
    }

    [AvaloniaFact]
    public void TheOutlineDetailIsShownOnlyWhenAnOutlineIsDrawn()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            var detail = Named<StackPanel>(session.Window, "OutlineDetail");

            Assert.False(detail.IsEffectivelyVisible);

            Toggle(session, "OutlineEnabledBox");
            Assert.True(session.Model.OutlineEnabled, "clicking the box did not reach the model");
            Assert.True(detail.IsEffectivelyVisible, "the outline colour and thickness never appeared");
        });
    }

    [AvaloniaFact]
    public void TurningOffTheOutlineTakesItOutOfTheCodeRatherThanHidingTheControls()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            Toggle(session, "OutlineEnabledBox");
            Named<ColorPicker>(session.Window, "OutlinePicker").SelectedColor = Crimson;
            DispatcherPump.Drain();
            Assert.NotNull(Layer(Redraw(session), QrLayerNames.Modules).Stroke);

            Toggle(session, "OutlineEnabledBox");

            var drawing = Redraw(session);
            Assert.False(Named<StackPanel>(session.Window, "OutlineDetail").IsEffectivelyVisible);
            Assert.Null(Layer(drawing, QrLayerNames.Modules).Stroke);
            Assert.Null(Layer(drawing, QrLayerNames.MarkerFrames).Stroke);
            Assert.True(session.Model.CanExport, session.Model.ExportBlockedReason ?? "export was blocked");
        });
    }

    #endregion

    #region The sliders

    [AvaloniaFact]
    public void TheAdvancedPanelActuallyContainsItsThreeSliders()
    {
        // The guard the WPF suite's BothTreesActuallyContainSliders exists to be. A tree-walking
        // test that finds no sliders passes on nothing at all, and the sliders here live behind
        // an expander, which is precisely the thing that can silently fail to open.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            Assert.Empty(VisibleSliders(session.Window));

            OpenAdvanced(session);
            Toggle(session, "OutlineEnabledBox");

            // Gap between dots, outline thickness and margin. The colour pickers' three RGB
            // sliders are inside closed popups and are not part of this window's tree.
            Assert.Equal(3, VisibleSliders(session.Window).Count);
        });
    }

    [AvaloniaFact]
    public void EverySliderMovesToThePointThatWasClicked()
    {
        // WPF pages by LargeChange on a track click unless IsMoveToPointEnabled is set, which
        // reads as jumpy and is why the WPF suite checks that property on every slider.
        // Avalonia has no such property and always moves to the point, so this asserts the
        // behaviour itself rather than a flag: a slider added later with a style of its own, or
        // a future toolkit change, still gets caught.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            Toggle(session, "OutlineEnabledBox");

            var sliders = VisibleSliders(session.Window);
            Assert.Equal(3, sliders.Count);

            foreach (var slider in sliders)
            {
                var range = slider.Maximum - slider.Minimum;
                slider.Value = slider.Minimum;
                DispatcherPump.Drain();

                Press(session, slider, 0.75);

                // Generously toleranced, because the track is inset by half a thumb at each end
                // and a snapping slider lands on the nearest tick. Paging would land on
                // Minimum + LargeChange, which for all three of these is outside this band.
                var expected = slider.Minimum + (range * 0.75);
                Assert.True(
                    Math.Abs(slider.Value - expected) <= range * 0.15,
                    $"a click at 75% of the {slider.Minimum}-{slider.Maximum} slider went to "
                        + $"{slider.Value}, not near {expected}; it is paging rather than moving to the point");
            }
        });
    }

    #endregion

    #region Wording

    /// <summary>
    /// What each dropdown option is called, taken from the WPF original, which is the shipping
    /// app and therefore the source of truth for it. Without the friendly-name converter these
    /// render by ToString: "RoundedSquare", "Quartile", "Modules". Legible enough to survive a
    /// review, wrong enough to notice in use.
    /// </summary>
    private static readonly (string Box, string[] Wording)[] DropdownWording =
    [
        ("ModuleShapeBox", ["Squares", "Rounded squares", "Dots", "Diamonds", "Flowing"]),
        ("MarkerFrameShapeBox", ["Square", "Rounded", "Circle", "Leaf"]),
        ("MarkerCenterShapeBox", ["Square", "Rounded", "Circle"]),
        ("OutlineTargetBox", ["Dots only", "Corners only", "Everything"]),
        ("EccBox", ["Low (smallest code)", "Medium (recommended)", "High", "Highest (needed for logos)"]),
    ];

    [AvaloniaFact]
    public void EveryDropdownOptionSaysWhatItMeansRatherThanNamingItsEnumMember()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            Toggle(session, "OutlineEnabledBox");

            foreach (var (name, wording) in DropdownWording)
            {
                var combo = Named<ComboBox>(session.Window, name);
                var options = combo.ItemsSource!.Cast<object>().ToList();

                Assert.True(options.Count > 0, $"{name} offers nothing at all");
                Assert.Equal(wording.Length, options.Count);

                var shown = new List<string?>();

                foreach (var option in options)
                {
                    combo.SelectedItem = option;
                    DispatcherPump.Drain();
                    shown.Add(Showing(combo));
                }

                Assert.Equal(wording, shown);
            }
        });
    }

    /// <summary>
    /// The sentences that turn a control someone can use into one they have to guess at, in the
    /// wording the shipping WPF app uses. Keyed by the named group each one is attached to: the
    /// tip sits on the group rather than on its label, so hovering the slider explains it too,
    /// which is the half of the WPF original that never worked.
    /// </summary>
    private static readonly (string Name, string Tip)[] Guidance =
    [
        ("ModuleScaleField", "A bigger gap looks lighter. Too big and the code stops scanning."),
        ("EccField",
            "How much damage the code can survive. Higher means it still scans if it is scratched "
                + "or partly covered, but the code becomes denser."),
        ("QuietZoneField", "Clear space scanners need to find the code. 4 is the standard."),
        ("BackgroundTransparent", "No background at all, so the code sits on whatever is behind it"),
        ("SizeSmall", "512 pixels. Good for a web page or an email."),
        ("SizeMedium", "1024 pixels. Good for a slide or a document."),
        ("SizePrint", "2048 pixels. Good for a printed flyer or a sign."),
    ];

    [AvaloniaFact]
    public void EveryControlThatNeedsExplainingCarriesTheSentenceTheShippingAppGivesIt()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            Assert.NotEmpty(Guidance);

            foreach (var (name, tip) in Guidance)
            {
                var control = Named<Control>(session.Window, name);
                Assert.Equal(tip, ToolTip.GetTip(control));
            }
        });
    }

    #endregion

    #region The treatments

    /// <summary>Every section heading in the window, by the words it shows.</summary>
    private static readonly string[] Headings =
    [
        "What goes in the code", "How it looks", "Save",
        "Shapes", "Outline", "Reliability", "Logo",
    ];

    /// <summary>
    /// Every field label, by the words it shows.
    ///
    /// Three of these belong to the outline and two to the corner colours, both of which the test
    /// below switches on, and the last two to the logo group, which stays collapsed because no
    /// image is chosen. All of them are found either way: a group hidden with IsVisible is still
    /// realised, so its labels are in the visual tree whether or not anyone can see them. Checked
    /// directly -- dropping the two toggles leaves the test green -- so this says "switched on"
    /// rather than the "they do not exist until it is" the comment here used to claim.
    ///
    /// The first five now sit outside the advanced options: the corner colours moved up beside
    /// the code colour, because splitting one code's three colours across two panels made two of
    /// them look like a different kind of setting. Their SHAPES stayed behind, which is why
    /// "Corner ring shape" and "Corner ring" are both here and are in different places.
    /// </summary>
    private static readonly string[] Labels =
    [
        "Code color", "Corner ring", "Corner center", "Background", "Size",
        "Dot shape", "Gap between dots", "Corner ring shape", "Corner center shape",
        "Error correction", "Margin around the code",
        "Color", "Thickness", "Applies to",
        "Logo size", "Space around it",
    ];

    [AvaloniaFact]
    public void EverySectionHeadingIsSetInTheHeadingTreatment()
    {
        // Each heading is found by the words it shows, then checked for the class and for the
        // weight the class's setter controls. Both halves are load bearing: without the class
        // check a heading could quietly lose it, and without the weight check the style itself
        // could be deleted, which is the failure this codebase has shipped twice.
        //
        // This replaces a version that counted the elements already carrying the class. That
        // count could only ever fire on a heading added *with* the class -- an unclassed one is
        // invisible to a search for the class -- so it triggered on correct code and stayed
        // silent on the mistake its comment advertised. Verified: two unclassed TextBlocks were
        // added to the advanced panel and the whole suite stayed green.
        //
        // The structural check below is what catches that case now. Inside the advanced panel a
        // bare TextBlock at the top level is a section heading by construction: every label lives
        // inside its own labelled group. So a top-level TextBlock there without the class fails
        // here, whether or not its words are in the list above.
        //
        // It filters on the class, not on the text. Filtering on the text made the check report
        // the wrong thing: a correctly classed heading whose words were not yet in the list --
        // which is what every heading added later looks like -- was named as unclassed, so the
        // message accused code that was right. The by-text loop above is what pins the
        // enumerated headings; this only ever asks whether the class is there.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            foreach (var text in Headings)
            {
                var heading = Assert.Single(WithText(session.Window, text));

                Assert.True(heading.Classes.Contains("heading"), $"the \"{text}\" heading is not classed as one");
                Assert.Equal(FontWeight.SemiBold, heading.FontWeight);
            }

            var loose = AdvancedPanel(session.Window).Children
                .OfType<TextBlock>()
                .Where(t => !t.Classes.Contains("heading"))
                .Select(t => t.Text ?? string.Empty)
                .ToList();

            Assert.True(
                loose.Count == 0,
                "these sit at the top of the advanced panel, where only section headings belong, "
                    + $"and are not classed as headings: {string.Join(", ", loose)}");
        });
    }

    [AvaloniaFact]
    public void EveryFieldLabelIsSetInTheLabelTreatment()
    {
        // The same shape as the heading test: every label is found by the words it shows, then
        // checked for the class and for the size the class's setter controls.
        //
        // The structural backstop covers, exactly: any panel inside the advanced options whose
        // own children include a dropdown, a slider or a colour picker. That panel must hold one
        // TextBlock and it must carry the class, so a control added later with a bare TextBlock
        // over it fails here rather than shipping at the theme's default size. Panel, not
        // StackPanel: the first version of this checked StackPanels alone and was blind to the
        // Grid-plus-label idiom this same file already uses for the outline colour row, which is
        // precisely the shape Phase 2e's logo controls might reach for.
        //
        // What it does NOT cover, stated rather than implied:
        //
        //  - Labelled rows outside the advanced options. "Code color", "Corner ring",
        //    "Corner center", "Background" and "Size" are checked by the lookup above and by
        //    nothing structural. The scope stops at the expander deliberately: the background row
        //    is a panel of radio buttons and a colour picker with no label of its own, so a guard
        //    reaching it would fire on correct code. The two corner colours joined this list when
        //    they moved up out of the expander, so they lost the structural cover they used to
        //    have; the by-text lookup is all that pins them now.
        //  - A label nested deeper than its group, rather than beside the control it introduces.
        //    Catching that means walking the whole tree for anything shaped like a label, which
        //    would fire on the preview and the content editors too.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);
            Toggle(session, "UseCustomMarkerColorsBox");
            Toggle(session, "OutlineEnabledBox");

            foreach (var text in Labels)
            {
                var label = Assert.Single(WithText(session.Window, text));

                Assert.True(label.Classes.Contains("label"), $"the \"{text}\" label is not classed as one");
                Assert.Equal(12d, label.FontSize);
            }

            var groups = AdvancedPanel(session.Window)
                .GetSelfAndVisualDescendants()
                .OfType<Panel>()
                .Where(p => p.Children.Any(c => c is ComboBox or Slider or ColorPicker))
                .ToList();

            Assert.True(groups.Count > 0, "the advanced panel realised no labelled groups at all");

            foreach (var group in groups)
            {
                var label = Assert.Single(group.Children.OfType<TextBlock>());
                Assert.True(
                    label.Classes.Contains("label"),
                    $"the group holding \"{label.Text}\" introduces a control with an unclassed label");
            }
        });
    }

    /// <summary>Every TextBlock showing exactly these words.</summary>
    private static List<TextBlock> WithText(Window window, string text) =>
        window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Text == text).ToList();

    /// <summary>
    /// The panel the advanced expander holds, which is the StackPanel wrapping every control
    /// behind it. Reached through the expander so the structural checks above cannot wander into
    /// the rest of the window, where a bare TextBlock is an ordinary caption rather than a
    /// heading.
    /// </summary>
    private static StackPanel AdvancedPanel(Window window) =>
        (StackPanel)(Named<Expander>(window, "AdvancedOptions").Content
            ?? throw new InvalidOperationException("the advanced expander holds nothing."));

    #endregion

    #region Reset

    [AvaloniaFact]
    public void ResetPutsEveryStylingChoiceBackAndThePreviewFollows()
    {
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            Named<ColorPicker>(session.Window, "ForegroundPicker").SelectedColor = Crimson;
            Named<ComboBox>(session.Window, "ModuleShapeBox").SelectedItem = ModuleShape.Circle;
            Named<ComboBox>(session.Window, "EccBox").SelectedItem = EccLevel.High;
            Named<Slider>(session.Window, "ModuleScaleSlider").Value = 0.6;
            Named<Slider>(session.Window, "QuietZoneSlider").Value = 1;
            Toggle(session, "OutlineEnabledBox");
            Toggle(session, "UseCustomMarkerColorsBox");
            DispatcherPump.Drain();

            // Proof the starting point is genuinely off the defaults, so the assertions below
            // cannot be satisfied by a Reset button that does nothing at all.
            Assert.NotEqual(QrStyle.Default.ModuleShape, session.Model.ModuleShape);
            Assert.NotEqual(QrStyle.Default.Foreground, Layer(Redraw(session), QrLayerNames.Modules).Fill);

            Press(session, Named<Button>(session.Window, "ResetButton"));
            DispatcherPump.Drain();

            Assert.Equal(QrStyle.Default.Foreground, session.Model.Foreground);
            Assert.Equal(QrStyle.Default.ModuleShape, session.Model.ModuleShape);
            Assert.Equal(QrStyle.Default.ModuleScale, session.Model.ModuleScale, 3);
            Assert.Equal(QrStyle.Default.QuietZoneModules, session.Model.QuietZone);
            Assert.Equal(QrStyle.Default.Ecc, session.Model.Ecc);
            Assert.False(session.Model.OutlineEnabled);
            Assert.False(session.Model.UseCustomMarkerColors);

            // The preview, not just the model: Reset replaces the style record wholesale rather
            // than going through the individual setters, so a control left off
            // RaiseAllStyleProperties would keep showing the old value.
            var drawing = Redraw(session);
            Assert.Equal(QrStyle.Default.Foreground, Layer(drawing, QrLayerNames.Modules).Fill);
            Assert.Null(Layer(drawing, QrLayerNames.Modules).Stroke);
        });
    }

    [AvaloniaFact]
    public void ResetPutsTheControlsThemselvesBackAsWellAsTheModel()
    {
        // The other direction of every binding on the panel, in one go. Reset writes to the view
        // model and raises the change; a control bound one-way out of the model would keep
        // showing what the user had chosen while the code was drawn from something else.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            OpenAdvanced(session);

            Named<ColorPicker>(session.Window, "ForegroundPicker").SelectedColor = Crimson;
            Named<ComboBox>(session.Window, "ModuleShapeBox").SelectedItem = ModuleShape.Diamond;
            Named<Slider>(session.Window, "QuietZoneSlider").Value = 8;
            DispatcherPump.Drain();

            Press(session, Named<Button>(session.Window, "ResetButton"));
            DispatcherPump.Drain();

            Assert.Equal(QrStyle.Default.Foreground, Named<ColorPicker>(session.Window, "ForegroundPicker").SelectedColor);
            Assert.Equal(QrStyle.Default.ModuleShape, Named<ComboBox>(session.Window, "ModuleShapeBox").SelectedItem);
            Assert.Equal(QrStyle.Default.QuietZoneModules, Named<Slider>(session.Window, "QuietZoneSlider").Value);
        });
    }

    #endregion

    [AvaloniaFact]
    public void TheAdvancedOptionsStayOutOfTheWayUntilTheyAreAskedFor()
    {
        // Eleven more controls unfolded on every launch would bury the three steps that matter.
        // Opened by clicking the header, the way a user does, rather than by setting IsExpanded:
        // an expander whose header was not actually clickable would pass the latter.
        UiHarness.WithWindow(session =>
        {
            GiveItSomethingToDraw(session);
            var advanced = Named<Expander>(session.Window, "AdvancedOptions");

            Assert.False(advanced.IsExpanded, "the advanced options are unfolded on launch");

            var header = advanced.GetVisualDescendants().OfType<ToggleButton>().First();
            Press(session, header);

            Assert.True(
                DispatcherPump.DrainUntil(() => Named<ComboBox>(session.Window, "ModuleShapeBox").Bounds.Width > 0),
                "clicking the advanced header opened nothing");
        });
    }
}

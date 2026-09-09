using System.Globalization;
using Avalonia.Data;
using TrispotQR.UI.Converters;
using TrispotQR.ViewModels;

namespace TrispotQR.UI.Tests;

/// <summary>
/// The converter that lets a row of radio buttons stand for one property that is not a bool.
///
/// StylingPanelTests already drives the four background and size buttons through the rendered
/// window, which is what proves the converter is actually wired up. What it cannot reach is the
/// branches no button in this window takes: a null value, a null parameter, and the reply the
/// converter gives for a button being switched *off*. That last one is the whole mechanism --
/// answer it wrongly and choosing one button clears the property another just set -- and it is
/// invisible from outside, because the click that switches a button off is the same click that
/// switches its neighbour on.
///
/// No window and no toolkit: this is arithmetic over two objects.
/// </summary>
public class EqualityConverterTests
{
    private static readonly EqualityConverter Converter = new();

    private static object Convert(object? value, object? parameter) =>
        Converter.Convert(value, typeof(bool?), parameter, CultureInfo.InvariantCulture);

    private static object ConvertBack(object? value, Type targetType, object? parameter) =>
        Converter.ConvertBack(value, targetType, parameter, CultureInfo.InvariantCulture);

    [Fact]
    public void TheButtonStandingForTheCurrentValueIsChecked() =>
        Assert.Equal(true, Convert(BackgroundChoice.Transparent, "Transparent"));

    [Fact]
    public void TheOtherButtonsInTheGroupAreNot() =>
        Assert.Equal(false, Convert(BackgroundChoice.Transparent, "White"));

    [Fact]
    public void AParameterMatchesWhateverItsCase() =>
        // The parameter is hand-typed into XAML, so "custom" and "Custom" must not disagree.
        Assert.Equal(true, Convert(BackgroundChoice.Custom, "custom"));

    [Fact]
    public void ANumberMatchesTheTextFormOfItsParameter() =>
        // The three export sizes compare an int against a string, because that is all a XAML
        // ConverterParameter can be.
        Assert.Equal(true, Convert(1024, "1024"));

    [Fact]
    public void NothingMatchesNothing() =>
        Assert.Equal(true, Convert(null, null));

    [Fact]
    public void NothingDoesNotMatchSomething() =>
        Assert.Equal(false, Convert(null, "White"));

    [Fact]
    public void SwitchingAButtonOnWritesTheValueItStandsFor() =>
        Assert.Equal(BackgroundChoice.Custom, ConvertBack(true, typeof(BackgroundChoice), "Custom"));

    [Fact]
    public void SwitchingAButtonOnWritesANumberAsANumber() =>
        Assert.Equal(2048, ConvertBack(true, typeof(int), "2048"));

    [Fact]
    public void ANullableTargetGetsTheUnderlyingValue() =>
        Assert.Equal(512, ConvertBack(true, typeof(int?), "512"));

    [Fact]
    public void SwitchingAButtonOffWritesNothingAtAll() =>
        // The button losing the selection reports back too. Writing anything here would clear
        // the property in the same gesture that another button in the group set it.
        Assert.Same(BindingOperations.DoNothing, ConvertBack(false, typeof(BackgroundChoice), "White"));

    [Fact]
    public void AButtonWithNothingToStandForWritesNothingAtAll() =>
        Assert.Same(BindingOperations.DoNothing, ConvertBack(true, typeof(BackgroundChoice), null));
}

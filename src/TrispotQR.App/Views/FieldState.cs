using System.Windows;

namespace TrispotQR.App.Views;

/// <summary>
/// Marks an input as being at fault, so the shared input template can draw it that way.
///
/// An attached property rather than a second style, because the red border has to survive
/// the focus highlight. A style that only overrode BorderBrush would lose to the template's
/// own focus trigger the moment the user clicked into the box that was wrong, which is
/// precisely when they are looking at it.
/// </summary>
public static class FieldState
{
    public static readonly DependencyProperty HasErrorProperty =
        DependencyProperty.RegisterAttached(
            "HasError", typeof(bool), typeof(FieldState), new PropertyMetadata(false));

    public static readonly DependencyProperty HasWarningProperty =
        DependencyProperty.RegisterAttached(
            "HasWarning", typeof(bool), typeof(FieldState), new PropertyMetadata(false));

    public static void SetHasError(DependencyObject element, bool value) =>
        element.SetValue(HasErrorProperty, value);

    public static bool GetHasError(DependencyObject element) =>
        (bool)element.GetValue(HasErrorProperty);

    public static void SetHasWarning(DependencyObject element, bool value) =>
        element.SetValue(HasWarningProperty, value);

    public static bool GetHasWarning(DependencyObject element) =>
        (bool)element.GetValue(HasWarningProperty);
}

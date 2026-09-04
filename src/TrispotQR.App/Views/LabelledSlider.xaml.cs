using System.Windows;
using System.Windows.Controls;

namespace TrispotQR.App.Views;

/// <summary>A 0 to 255 slider with a caption and a live readout, used by the colour picker.</summary>
public partial class LabelledSlider : UserControl
{
    public LabelledSlider()
    {
        InitializeComponent();

        Bar.ValueChanged += (_, _) =>
        {
            ValueText.Text = ((int)Bar.Value).ToString();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Raised when the user moves the slider.</summary>
    public event EventHandler? ValueChanged;

    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }

    public double Value
    {
        get => Bar.Value;
        set => Bar.Value = value;
    }
}

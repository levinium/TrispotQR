using System.Windows.Threading;
using TrispotQR.ViewModels;

namespace TrispotQR.App.Services;

/// <summary>WPF's dispatcher timer behind the shared interface.</summary>
public sealed class WpfUiTimer : IUiTimer
{
    private readonly DispatcherTimer _timer = new();

    public WpfUiTimer() => _timer.Tick += (_, _) => Tick?.Invoke(this, EventArgs.Empty);

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public event EventHandler? Tick;

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();
}

using System.Windows;
using System.Windows.Threading;

namespace TrispotQR.Tests;

/// <summary>
/// One long-lived apartment thread that owns the WPF Application for the whole test run,
/// and runs every UI test on it.
///
/// This exists because of two rules that pull against each other. A WPF Application can be
/// created only once per process, and the styles it holds are thread affine, so a control
/// built on some other thread cannot read them. Spinning up a throwaway thread per test and
/// creating the Application on the first one happens to work for whichever test gets there
/// first, and then fails for every test afterwards with
/// "Cannot find resource named ...". Anything that resolves a StaticResource from
/// App.xaml, which is most of this UI, has to run here.
/// </summary>
public sealed class WpfHost : IDisposable
{
    private readonly Dispatcher _dispatcher;

    public WpfHost()
    {
        using var ready = new ManualResetEventSlim();
        Dispatcher? dispatcher = null;

        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;

            // InitializeComponent is what merges Theme.xaml into Application.Resources.
            if (Application.Current is null)
            {
                var app = new TrispotQR.App.App();
                app.InitializeComponent();
            }

            ready.Set();

            // Keeps the thread alive so the Application and its resources stay valid, and
            // gives Invoke somewhere to marshal work to.
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "TrispotQR UI test host",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();

        _dispatcher = dispatcher!;
    }

    /// <summary>Runs work on the UI thread and returns its result.</summary>
    public T Run<T>(Func<T> work) => _dispatcher.Invoke(work);

    public void Dispose() => _dispatcher.InvokeShutdown();
}

/// <summary>
/// UI tests share the one host and never run in parallel: they share an Application, and
/// some of them also share the system clipboard.
/// </summary>
[CollectionDefinition("UI", DisableParallelization = true)]
public sealed class UiCollection : ICollectionFixture<WpfHost>
{
}

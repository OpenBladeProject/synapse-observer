using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SynapseObserver.App.Tests;

[CollectionDefinition("WPF application", DisableParallelization = true)]
public sealed class WpfApplicationCollection : ICollectionFixture<WpfApplicationFixture>
{
}

// WPF permits one Application per process. Keep its resources and STA dispatcher
// alive for both visual tests, regardless of which test xUnit selects first.
public sealed class WpfApplicationFixture : IDisposable
{
    private readonly Application application;
    private readonly Thread thread;

    public WpfApplicationFixture()
    {
        var ready = new TaskCompletionSource<Application>(TaskCreationOptions.RunContinuationsAsynchronously);
        thread = new Thread(() =>
        {
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                ready.TrySetResult(app);
                app.Run();
            }
            catch (Exception ex)
            {
                ready.TrySetException(ex);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        application = ready.Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
    }

    public void Run(Action action) => application.Dispatcher.InvokeAsync(action, DispatcherPriority.Normal)
        .Task.WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();

    public void Dispose()
    {
        Run(() => application.Shutdown());
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "The WPF application did not shut down.");
    }
}

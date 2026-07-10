using System.Windows.Threading;
using ChatGPTWrapper.Adventure;
using WpfApplication = System.Windows.Application;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>
/// Dedicated STA thread for modal WPF dialogs invoked from the WinUI host.
/// </summary>
internal static class WpfStaHost
{
    private static readonly object Gate = new();
    private static Thread? _thread;
    private static WpfApplication? _app;
    private static Dispatcher? _dispatcher;
    private static Exception? _initFailure;

    public static Dispatcher Dispatcher
    {
        get
        {
            EnsureInitialized();
            return _dispatcher ?? throw new InvalidOperationException("WPF STA dispatcher is unavailable.", _initFailure);
        }
    }

    public static void EnsureInitialized()
    {
        if (_dispatcher is not null)
            return;

        lock (Gate)
        {
            if (_dispatcher is not null)
                return;

            var ready = new ManualResetEventSlim(false);
            _thread = new Thread(() =>
            {
                try
                {
                    _app = new WpfApplication { ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown };
                    WpfStaThemeBootstrap.EnsureApplied(_app);
                    AppDirectories.EnsureCreated();
                    AdventurePathBootstrap.Register();
                    _dispatcher = Dispatcher.CurrentDispatcher;
                    ready.Set();
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    _initFailure = ex;
                    ready.Set();
                }
            })
            {
                IsBackground = true,
                Name = "ChatGPTWrapper.WpfStaHost",
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            ready.Wait();

            if (_initFailure is not null)
                throw new InvalidOperationException("Failed to start WPF STA host.", _initFailure);
        }
    }

    public static Task<T> InvokeAsync<T>(Func<T> func)
    {
        EnsureInitialized();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.BeginInvoke(() =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Normal);
        return tcs.Task;
    }

    public static Task InvokeAsync(Action action) =>
        InvokeAsync(() =>
        {
            action();
            return true;
        });

    public static Task InvokeTaskAsync(Func<Task> asyncFunc)
    {
        EnsureInitialized();
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await asyncFunc().ConfigureAwait(true);
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, DispatcherPriority.Normal);
        return tcs.Task;
    }
}

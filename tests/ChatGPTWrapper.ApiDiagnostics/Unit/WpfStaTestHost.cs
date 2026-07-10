using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// Single STA WPF dispatcher shared by unit tests (avoids parallel Application creation).
/// </summary>
internal static class WpfStaTestHost
{
    private static readonly object Gate = new();
    private static Thread? _uiThread;
    private static Dispatcher? _dispatcher;

    public static Dispatcher Dispatcher
    {
        get
        {
            EnsureInitialized();
            return _dispatcher!;
        }
    }

    public static Task RunOnUiAsync(Func<Task> work) =>
        Dispatcher.InvokeAsync(work, DispatcherPriority.Normal).Task.Unwrap();

    public static void Run(Action action, TimeSpan? timeout = null)
    {
        EnsureInitialized();
        Exception? failure = null;
        var done = new ManualResetEventSlim(false);
        _dispatcher!.Invoke(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        }, DispatcherPriority.Normal);

        if (!done.Wait(timeout ?? TimeSpan.FromSeconds(30)))
            throw new TimeoutException("WPF STA test timed out.");

        if (failure is not null)
            throw failure;
    }

    public static void EnsureChromeResources()
    {
        var app = WpfApplication.Current ?? throw new InvalidOperationException("WPF Application not initialized.");
        if (app.Resources.MergedDictionaries.Any(d =>
                d.Source?.OriginalString?.Contains("WrapperChrome.xaml", StringComparison.OrdinalIgnoreCase) == true))
        {
            return;
        }

        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/ChatGPT Wrapper;component/Themes/WrapperChrome.xaml", UriKind.Relative),
        });
    }

    private static void EnsureInitialized()
    {
        if (_dispatcher is not null)
            return;

        lock (Gate)
        {
            if (_dispatcher is not null)
                return;

            if (WpfApplication.Current is not null)
            {
                _dispatcher = WpfApplication.Current.Dispatcher;
                return;
            }

            var ready = new ManualResetEventSlim(false);
            _uiThread = new Thread(() =>
            {
                _ = new WpfApplication();
                _dispatcher = WpfApplication.Current!.Dispatcher;
                ready.Set();
                WpfApplication.Current.Run();
            })
            {
                IsBackground = true,
                Name = "WpfStaTestHost",
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            if (!ready.Wait(TimeSpan.FromSeconds(15)))
                throw new TimeoutException("WPF STA host failed to start.");
        }
    }
}

[CollectionDefinition("WpfUi", DisableParallelization = true)]
public sealed class WpfUiCollection;

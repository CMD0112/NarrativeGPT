using System.Windows;
using System.Windows.Threading;
using ChatGPTWrapper;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

public sealed class WebView2DiagnosticHost : IAsyncDisposable
{
    private readonly TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _uiThread;
    private System.Windows.Application? _app;
    private Window? _window;
    private WebView2? _webView;
    private ChatGptApiBridgeInjection? _bridge;
    private Dispatcher? _dispatcher;

    public WebView2? WebView => _webView;

    public CoreWebView2? Core => _webView?.CoreWebView2;

    public ChatGptApiBridgeInjection? Bridge => _bridge;

    public Dispatcher UiDispatcher => _dispatcher
                                      ?? throw new InvalidOperationException("UI thread not started.");

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_uiThread is not null)
            return;

        if (System.Windows.Application.Current is not null)
        {
            _dispatcher = System.Windows.Application.Current.Dispatcher;
            await RunOnUiAsync(async () =>
            {
                AppDirectories.EnsureCreated();
                var env = await CoreWebView2Environment.CreateAsync(
                    browserExecutableFolder: null,
                    userDataFolder: AppDirectories.WebView2UserDataDirectory);
                _webView = new WebView2();
                _window = new Window
                {
                    Title = "ChatGPT API Diagnostics",
                    Width = 960,
                    Height = 720,
                    ShowInTaskbar = false,
                    WindowState = WindowState.Minimized,
                };
                _window.Content = _webView;
                _window.Show();
                await _webView.EnsureCoreWebView2Async(env);
                _bridge = new ChatGptApiBridgeInjection(_webView);
                _bridge.Register();
            }, cancellationToken);
            return;
        }

        _uiThread = new Thread(() =>
        {
            if (System.Windows.Application.Current is null)
                _app = new System.Windows.Application();
            else
                _app = System.Windows.Application.Current;

            _dispatcher = _app.Dispatcher;
            _window = new Window
            {
                Title = "ChatGPT API Diagnostics",
                Width = 960,
                Height = 720,
            };
            _webView = new WebView2();
            _window.Content = _webView;
            _window.Show();
            _uiReady.TrySetResult();
            _app.Run();
        })
        {
            IsBackground = true,
            Name = "ApiDiagnosticsWebView",
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        await _uiReady.Task.WaitAsync(cancellationToken);
        await RunOnUiAsync(async () =>
        {
            AppDirectories.EnsureCreated();
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppDirectories.WebView2UserDataDirectory);
            await _webView!.EnsureCoreWebView2Async(env);
            _bridge = new ChatGptApiBridgeInjection(_webView);
            _bridge.Register();
        }, cancellationToken);
    }

    public Task RunOnUiAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        UiDispatcher.InvokeAsync(work, DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);

    public Task<T> RunOnUiAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) =>
        UiDispatcher.InvokeAsync(work, DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_dispatcher is null)
            return;

        try
        {
            await _dispatcher.InvokeAsync(() =>
            {
                _webView?.Dispose();
                _window?.Close();
                if (_uiThread is not null)
                    _app?.Shutdown();
            });
        }
        catch
        {
            /* ignore shutdown races */
        }

        _uiThread?.Join(TimeSpan.FromSeconds(5));
    }
}

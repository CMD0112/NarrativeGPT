using System.Windows;
using System.Windows.Threading;
using ChatGPTWrapper;
using ChatGPTWrapper.ApiDiagnostics.Unit;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ApiDiagnostics.Performance;

public sealed class SourceSyncMockBridgeHost : IAsyncLifetime
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static WebView2? _sharedWebView;
    private static ChatGptApiBridgeInjection? _sharedBridge;
    private static Dispatcher? _sharedDispatcher;
    private static bool _initialized;

    public CoreWebView2? Core => _sharedWebView?.CoreWebView2;

    public ChatGptApiBridgeInjection? Bridge => _sharedBridge;

    public Task InitializeAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        return EnsureInitializedAsync();
    }

    public Task DisposeAsync()
    {
        ChatGptApiBridgeInjection.TestBridgeScriptOverride = null;
        ChatGptPageGate.TestAllowAnyInjectablePage = false;
        return Task.CompletedTask;
    }

    public Task RunOnUiAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        UiDispatcher.InvokeAsync(work, DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);

    public Task SetMockDelayAsync(int delayMs) =>
        RunOnUiAsync(async () =>
        {
            await Core!.ExecuteScriptAsync($"globalThis.__cgwMockDelayMs = {delayMs};");
        });

    private static Dispatcher UiDispatcher =>
        _sharedDispatcher ?? throw new InvalidOperationException("Mock bridge host not initialized.");

    private static async Task EnsureInitializedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_initialized)
                return;

            _sharedDispatcher = WpfStaTestHost.Dispatcher;
            await RunOnUiAsync(InitializeOnUiAsync);
            _initialized = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Task RunOnUiAsync(Func<Task> work) =>
        UiDispatcher.InvokeAsync(work, DispatcherPriority.Normal).Task.Unwrap();

    private static async Task InitializeOnUiAsync()
    {
        if (_sharedWebView is null)
        {
            var window = new Window
            {
                Title = "Source Sync Mock Bridge",
                Width = 800,
                Height = 600,
                ShowInTaskbar = false,
                WindowState = WindowState.Minimized,
            };
            _sharedWebView = new WebView2();
            window.Content = _sharedWebView;
            window.Show();
        }

        var userDataFolder = Path.Combine(
            Path.GetTempPath(),
            "cgw-source-sync-mock",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(userDataFolder);

        var mockPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "source-sync-bridge-mock.js");
        if (!File.Exists(mockPath))
            throw new FileNotFoundException("Mock bridge script missing.", mockPath);

        ChatGptApiBridgeInjection.TestBridgeScriptOverride = await File.ReadAllTextAsync(mockPath);
        ChatGptPageGate.TestAllowAnyInjectablePage = true;

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder);
        await _sharedWebView!.EnsureCoreWebView2Async(env);

        _sharedWebView.CoreWebView2!.Settings.IsWebMessageEnabled = true;
        _sharedBridge = new ChatGptApiBridgeInjection(_sharedWebView);
        _sharedBridge.Register();

        var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "source-sync-fixture.html");
        if (!File.Exists(fixturePath))
            throw new FileNotFoundException("Source sync fixture missing.", fixturePath);

        var core = _sharedWebView.CoreWebView2;
        var navigateTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnNavCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            core.NavigationCompleted -= OnNavCompleted;
            navigateTcs.TrySetResult();
        }

        core.NavigationCompleted += OnNavCompleted;
        core.Navigate(new Uri(fixturePath).AbsoluteUri);
        await navigateTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        await _sharedBridge.InjectAsync(core);
        await core.ExecuteScriptAsync("globalThis.__cgwMockDelayMs = 0;");
    }
}

[CollectionDefinition("SourceSyncMockBridge", DisableParallelization = true)]
public sealed class SourceSyncMockBridgeCollection : ICollectionFixture<SourceSyncMockBridgeHost>
{
}

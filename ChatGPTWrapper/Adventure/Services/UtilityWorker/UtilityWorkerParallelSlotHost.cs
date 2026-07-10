using System.IO;
using System.Windows;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>
/// Dedicated STA WebView for one parallel utility worker slot. Cookies synced from the main profile per rent.
/// </summary>
internal sealed class UtilityWorkerParallelSlotHost
{
    private readonly TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly int _slotId;
    private Thread? _uiThread;
    private Window? _window;
    private WebView2? _webView;
    private ChatGptAdventureBridgeInjection? _adventureBridge;
    private ChatGptApiBridgeInjection? _apiBridge;
    private AdventureTurnService? _turnService;

    public UtilityWorkerParallelSlotHost(int slotId) => _slotId = slotId;

    public int SlotId => _slotId;

    public CoreWebView2? Core => _webView?.CoreWebView2;

    public WebView2? WebView => _webView;

    public AdventureTurnService TurnService => _turnService
                                                 ?? throw new InvalidOperationException(
                                                     $"Parallel worker slot {_slotId} not ready.");

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_uiThread is not null)
            return;

        _uiThread = new Thread(() =>
        {
            _window = new Window
            {
                Title = $"CGW Parallel Worker {_slotId}",
                Width = 960,
                Height = 720,
                Opacity = 0,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0,
                IsHitTestVisible = false,
            };
            _webView = new WebView2 { IsHitTestVisible = false };
            _window.Content = _webView;
            _window.Show();
            _uiReady.TrySetResult();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = $"UtilityParallelWorker{_slotId}",
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        await _uiReady.Task.WaitAsync(cancellationToken);
        await RunOnUiAsync(async () =>
        {
            AppDirectories.EnsureCreated();
            var userData = AppDirectories.WebView2ParallelWorkerSlotDirectory(_slotId);
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userData);
            await _webView!.EnsureCoreWebView2Async(env);
            _apiBridge = new ChatGptApiBridgeInjection(_webView);
            _apiBridge.Register();
            _adventureBridge = new ChatGptAdventureBridgeInjection(_webView);
            _adventureBridge.Register();
            _turnService = new AdventureTurnService(_adventureBridge);
        }, cancellationToken);
    }

    public async Task EnsureProjectPageReadyAsync(
        AdventureBundle bundle,
        IReadOnlyList<CoreWebView2Cookie> chatGptCookies,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        await RunOnUiAsync(async () =>
        {
            var core = Core
                       ?? throw new InvalidOperationException($"Parallel slot {_slotId} WebView not ready.");

            await WebViewCookieSync.ApplyChatGptCookiesAsync(core, chatGptCookies, cancellationToken);
            await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);

            var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
            if (string.IsNullOrWhiteSpace(gizmoId))
                throw new InvalidOperationException("no_linked_project");

            gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
            if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
            {
                var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
                core.Navigate(projectUrl);
            }
        }, cancellationToken);
    }

    /// <summary>
    /// Prepares the slot and captures WebView/Core references on the slot UI thread (required for WPF affinity).
    /// </summary>
    public async Task<UtilityWorkerParallelSlotLease> PrepareLeaseAsync(
        AdventureBundle bundle,
        IReadOnlyList<CoreWebView2Cookie> chatGptCookies,
        CancellationToken cancellationToken = default)
    {
        await EnsureProjectPageReadyAsync(bundle, chatGptCookies, cancellationToken);

        return await RunOnUiAsync(() =>
        {
            var webView = _webView
                          ?? throw new InvalidOperationException($"Parallel slot {_slotId} WebView not ready.");
            var core = webView.CoreWebView2
                       ?? throw new InvalidOperationException($"Parallel slot {_slotId} CoreWebView2 not ready.");
            var turnService = _turnService
                              ?? throw new InvalidOperationException(
                                  $"Parallel slot {_slotId} turn service not ready.");

            return Task.FromResult(new UtilityWorkerParallelSlotLease
            {
                SlotId = _slotId,
                WebView = webView,
                Core = core,
                TurnService = turnService,
                Host = this,
            });
        }, cancellationToken);
    }

    public Task RunOnUiAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        _window!.Dispatcher.InvokeAsync(work, DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);

    public Task<T> RunOnUiAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) =>
        _window!.Dispatcher.InvokeAsync(work, DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);
}

using System.IO;
using System.Windows;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Hidden STA WebView host for DOM attachment fallback (CMD-414). Separate profile; cookies synced from main worker.
/// </summary>
internal sealed class UtilityAttachWorkerHost : IAsyncDisposable
{
    private static readonly SemaphoreSlim InitGate = new(1, 1);
    private static UtilityAttachWorkerHost? _instance;

    private readonly TaskCompletionSource _uiReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _uiThread;
    private System.Windows.Application? _app;
    private Window? _window;
    private WebView2? _webView;
    private ChatGptAdventureBridgeInjection? _adventureBridge;
    private ChatGptApiBridgeInjection? _apiBridge;
    private AdventureTurnService? _turnService;

    public CoreWebView2? Core => _webView?.CoreWebView2;

    public AdventureTurnService TurnService => _turnService
                                                 ?? throw new InvalidOperationException("Attach worker not ready.");

    public static async Task<UtilityAttachWorkerHost> EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (_instance is not null)
            return _instance;

        await InitGate.WaitAsync(cancellationToken);
        try
        {
            if (_instance is not null)
                return _instance;

            var host = new UtilityAttachWorkerHost();
            await host.InitializeAsync(cancellationToken);
            _instance = host;
            return host;
        }
        finally
        {
            InitGate.Release();
        }
    }

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        _uiThread = new Thread(() =>
        {
            _app = new System.Windows.Application();
            _window = new Window
            {
                Title = "CGW Attach Worker",
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
            _app.Run();
        })
        {
            IsBackground = true,
            Name = "UtilityAttachWorker",
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();

        await _uiReady.Task.WaitAsync(cancellationToken);
        await RunOnUiAsync(async () =>
        {
            AppDirectories.EnsureCreated();
            Directory.CreateDirectory(AppDirectories.WebView2AttachWorkerUserDataDirectory);
            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: AppDirectories.WebView2AttachWorkerUserDataDirectory);
            await _webView!.EnsureCoreWebView2Async(env);
            _apiBridge = new ChatGptApiBridgeInjection(_webView);
            _apiBridge.Register();
            _adventureBridge = new ChatGptAdventureBridgeInjection(_webView);
            _adventureBridge.Register();
            _turnService = new AdventureTurnService(_adventureBridge);
        }, cancellationToken);
    }

    public Task RunOnUiAsync(Func<Task> work, CancellationToken cancellationToken = default) =>
        _window!.Dispatcher.InvokeAsync(work, System.Windows.Threading.DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);

    public Task<T> RunOnUiAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) =>
        _window!.Dispatcher.InvokeAsync(work, System.Windows.Threading.DispatcherPriority.Normal)
            .Task.Unwrap()
            .WaitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_window?.Dispatcher is null)
            return;

        try
        {
            await _window.Dispatcher.InvokeAsync(() =>
            {
                _app?.Shutdown();
            });
        }
        catch
        {
            /* shutting down */
        }

        _instance = null;
    }
}

/// <summary>DOM attach via isolated attach-worker WebView when in-process shadow compositor fails.</summary>
internal static class UtilityAttachWorkerService
{
    public static async Task<ConversationSendResult> TryDomAttachAsync(
        CoreWebView2 cookieSourceCore,
        string conversationId,
        string gizmoId,
        string messageText,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        CancellationToken cancellationToken = default)
    {
        if (domAttachments is not { Count: > 0 })
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = "missing_attachments",
                ConversationId = conversationId,
            };
        }

        try
        {
            var host = await UtilityAttachWorkerHost.EnsureAsync(cancellationToken);
            var core = host.Core
                       ?? throw new InvalidOperationException("Attach worker WebView not ready.");

            await WebViewCookieSync.CopyChatGptCookiesAsync(cookieSourceCore, core, cancellationToken);

            return await host.RunOnUiAsync(async () =>
            {
                await ChatGptAdventureBridgeInjection.ApplyUtilityWorkerTabVisibilityAsync(core);
                return await host.TurnService.SubmitUtilityJobWithAttachmentsAsync(
                    core,
                    conversationId,
                    gizmoId,
                    messageText,
                    domAttachments,
                    jobId: "attach_worker",
                    cancellationToken: cancellationToken);
            }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConversationSendResult
            {
                Success = false,
                Error = $"attach_worker_failed:{ex.Message}",
                ConversationId = conversationId,
            };
        }
    }
}

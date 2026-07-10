using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>WinUI <see cref="IUtilityWorkerHost"/> — worker tab on WinUI, API via hidden WPF bridge.</summary>
internal sealed class WinUiUtilityWorkerHost : IUtilityWorkerHost
{
    private static readonly NoOpDisposable DomSendScope = new();

    private readonly WinUiPlaySessionService _session;
    private readonly Dictionary<object, AdventureTurnService> _turnServices = new();
    private readonly Dictionary<object, ChatGptAdventureBridgeInjection> _bridges = new();

    private WebView2? _workerWebView;
    private ChatGptProjectApiService? _projectApi;
    private ChatGptConversationSendService? _conversationSend;
    private int _apiWarmDepth;

    public WinUiUtilityWorkerHost(WinUiPlaySessionService session) => _session = session;

    public ChatGptConversationSendService ConversationSend =>
        _conversationSend ?? throw new InvalidOperationException("Project API is not ready.");

    public ChatGptProjectApiService? ProjectApi => _projectApi;

    public void OnAdventureLoaded(Guid adventureId)
    {
        UtilityWorkerCoordinator.For(adventureId).ResumeIncompleteOutbox(this);
        UpdateActiveJobCount(adventureId);
        _session.NotifyStatusChanged();
    }

    public void OnAdventureLeft()
    {
        _workerWebView = null;
        _turnServices.Clear();
        _bridges.Clear();
    }

    public void RequestOutboxPump(AdventureBundle bundle)
    {
        UtilityWorkerCoordinator.For(bundle.Metadata.Id).RequestOutboxPump(this);
        UpdateActiveJobCount(bundle.Metadata.Id);
    }

    public AdventureTurnService GetTurnService(object webView)
    {
        if (!_turnServices.TryGetValue(webView, out var service))
        {
            var bridge = GetOrRegisterBridge(webView);
            service = new AdventureTurnService(bridge);
            if (_conversationSend is not null)
                service.SetConversationSendService(_conversationSend);
            _turnServices[webView] = service;
        }
        else if (_conversationSend is not null)
        {
            service.SetConversationSendService(_conversationSend);
        }

        return service;
    }

    public void RegisterWorkerTab(object webView)
    {
        if (webView is WebView2 wv)
            _workerWebView = wv;

        _ = EnsureBridgeRegisteredAsync(webView);
    }

    public Task<object?> ResolveWorkerWebViewAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken = default) =>
        ResolveWorkerWebViewCoreAsync(bundle, cancellationToken);

    public async Task<object?> EnsureWorkerTabReadyAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        await EnsureApiServicesAsync(bundle.Metadata.Id, cancellationToken).ConfigureAwait(false);
        var webView = await ResolveWorkerWebViewCoreAsync(bundle, cancellationToken).ConfigureAwait(false);
        if (webView is WebView2 wv)
        {
            await WinUiShellHost.RunOnUiThreadAsync(async () =>
            {
                await _session.EnsurePageHostAsync(wv);
                RegisterWorkerTab(wv);
            }).ConfigureAwait(false);
        }

        return webView;
    }

    public Task EnsureWorkerWebViewBackgroundHostedAsync(
        object workerWebView,
        bool apiOnlyWarm = false,
        CancellationToken cancellationToken = default) =>
        WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            await EnsureApiServicesAsync(_session.CurrentBundle?.Metadata.Id ?? Guid.Empty, cancellationToken);
            try
            {
                await UtilityWorkerHostRuntime.WarmWorkerWebViewAsync(
                    workerWebView,
                    _projectApi?.Bridge,
                    _bridges.GetValueOrDefault(workerWebView) ?? TryGetBridge(workerWebView),
                    apiOnlyWarm,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                WinUiEventLogger.Debug("utility_worker_warm_failed", ex.Message);
            }
        });

    public Task<T> WithUtilityWebViewActivatedAsync<T>(
        object workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default) =>
        WinUiShellHost.RunOnUiThreadAsync(action);

    public IDisposable BeginDomAttachmentSend() => DomSendScope;

    public Task<T> WithUtilityComposerVisibleAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default) =>
        WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            if (_workerWebView is { } wv)
                WinUiShellHost.GetShellChatHost()?.SelectWebView(wv);

            return await action();
        });

    public object? GetPlayWebView() => _session.PlayWebView;

    public void SetStatus(string message)
    {
        WinUiEventLogger.Debug("utility_worker_status", message);
        _session.NotifyStatusChanged();
    }

    public void OnOutboxBatchCompleted(Guid adventureId, IReadOnlyList<UtilityOutboxJobResult> results)
    {
        _ = WinUiShellHost.RunOnUiThreadAsync(() =>
        {
            _session.ReloadBundle(adventureId);
            UpdateActiveJobCount(adventureId);
            _session.NotifyStatusChanged();
            return Task.CompletedTask;
        });
    }

    public void RefreshPlayJobButtons()
    {
        if (_session.CurrentBundle is { } bundle)
            UpdateActiveJobCount(bundle.Metadata.Id);
        _session.NotifyStatusChanged();
    }

    public Task<string?> TryCreateEphemeralConversationViaUiAsync(
        AdventureBundle bundle,
        object core,
        CancellationToken cancellationToken = default)
    {
        if (_projectApi is null)
            return Task.FromResult<string?>(null);

        var turnHost = _workerWebView ?? core;
        return WinUiShellHost.RunOnUiThreadAsync(() =>
            UtilityWorkerHostRuntime.TryOpenComposerAsync(
                bundle,
                core,
                _projectApi,
                GetTurnService(turnHost),
                cancellationToken));
    }

    public object? GetWorkerCookieSource() =>
        UtilityWebViewBridge.GetCore(_workerWebView) ?? UtilityWebViewBridge.GetCore(_session.PlayWebView);

    public Task<IReadOnlyList<object>> GetWorkerChatGptCookiesAsync(
        CancellationToken cancellationToken = default) =>
        UtilityWorkerHostRuntime.ReadChatGptCookiesAsync(GetWorkerCookieSource(), cancellationToken);

    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        var id = _session.CurrentBundle?.Metadata.Id ?? Guid.Empty;
        if (id == Guid.Empty)
            return false;

        return await UtilityWorkerCoordinator.For(id).ProbeAsync(this, cancellationToken);
    }

    internal void UpdateActiveJobCount(Guid adventureId) =>
        WinUiShellHost.SetUtilityJobCount(UtilityOutboxService.PendingCount(adventureId));

    private async Task EnsureApiServicesAsync(Guid adventureId, CancellationToken cancellationToken)
    {
        if (_projectApi is not null && _conversationSend is not null)
            return;

        if (Interlocked.Increment(ref _apiWarmDepth) > 1)
        {
            Interlocked.Decrement(ref _apiWarmDepth);
            while (_projectApi is null && Volatile.Read(ref _apiWarmDepth) > 0)
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await WpfStaProjectHostBridge.InvokeAsync(async host =>
            {
                await host.EnsureReadyAsync(adventureId == Guid.Empty ? null : adventureId, cancellationToken: cancellationToken);
                _projectApi = host.Api;
                _conversationSend = new ChatGptConversationSendService(host.Api.Bridge);
                _session.SendHost.WireConversationSend(_conversationSend);
            }).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _apiWarmDepth);
        }
    }

    private async Task<object?> ResolveWorkerWebViewCoreAsync(
        AdventureBundle bundle,
        CancellationToken cancellationToken)
    {
        if (UtilityWebViewBridge.GetCore(_workerWebView) is not null)
            return _workerWebView;

        return await WinUiShellHost.RunOnUiThreadAsync(async () =>
        {
            var chatHost = WinUiShellHost.GetShellChatHost();
            if (chatHost is null)
                return (object?)null;

            var pinKey = AdventureThreadRegistryService
                .GetActiveEntry(bundle, AdventureThreadKind.UtilityWorker)?.PinnedTabKey;
            var webView = chatHost.FindWebViewByPinKey(pinKey) ?? _workerWebView;
            if (webView is null && UtilityWorkerPinService.HasWorkerPin(bundle))
            {
                await chatHost.AddTabAsync("Utility worker");
                webView = chatHost.GetActiveWebView();
            }

            if (webView is null)
                return null;

            await _session.EnsurePageHostAsync(webView);
            _workerWebView = webView;
            return webView;
        });
    }

    private ChatGptAdventureBridgeInjection GetOrRegisterBridge(object webView)
    {
        if (_bridges.TryGetValue(webView, out var existing))
            return existing;

        var coreObj = UtilityWebViewBridge.GetCore(webView)
                      ?? throw new InvalidOperationException("WebView core is not ready.");
        var bridge = ChatGptAdventureBridgeInjection.CreateForCore(coreObj);
        bridge.Register();
        _bridges[webView] = bridge;
        return bridge;
    }

    private ChatGptAdventureBridgeInjection? TryGetBridge(object webView) =>
        _bridges.GetValueOrDefault(webView);

    private Task EnsureBridgeRegisteredAsync(object webView) =>
        WinUiShellHost.RunOnUiThreadAsync(() =>
        {
            GetOrRegisterBridge(webView);
            return Task.CompletedTask;
        });

    private sealed class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

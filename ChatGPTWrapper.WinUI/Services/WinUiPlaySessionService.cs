using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUiBridge;
using ChatGPTWrapper.WinUI.Theme;
using ChatGPTWrapper.WinUI.Views;
using ChatGPTWrapper.WinUI.WebView;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>WinUI play session orchestration: pin, WebView host wiring, compose send, adventure load.</summary>
public sealed class WinUiPlaySessionService
{
    private readonly ShellNavigationService _navigation;
    private readonly Dictionary<WebView2, ChatGptPlayComposeInjection> _composeInjections = new();
    private readonly HashSet<ChatGptPlayComposeInjection> _wiredComposeInjections = new();
    private readonly WinUiPlaySendHost _sendHost;
    private readonly WinUiUtilityWorkerHost _utilityWorker;
    private ChatTabHost? _tabHost;
    private WinUiPlayTabRegistry? _tabRegistry;
    private AdventureBundle? _bundle;
    private WebView2? _playWebView;
    private string? _mergedPreview;
    private CancellationTokenSource? _mergedPreviewDebounceCts;

    public WinUiPlaySessionService(ShellNavigationService navigation)
    {
        _navigation = navigation;
        _sendHost = new WinUiPlaySendHost(this);
        _utilityWorker = new WinUiUtilityWorkerHost(this);
    }

    public AdventureBundle? CurrentBundle => _bundle;

    public WebView2? PlayWebView => _playWebView;

    internal WinUiPlaySendHost SendHost => _sendHost;

    internal WinUiUtilityWorkerHost UtilityWorker => _utilityWorker;

    internal IPlayTabRegistry TabRegistry =>
        _tabRegistry ?? throw new InvalidOperationException("Play tab host is not bound.");

    public event EventHandler? StatusChanged;

    public void BindTabHost(ChatTabHost tabHost)
    {
        _tabHost = tabHost;
        _tabRegistry = new WinUiPlayTabRegistry(tabHost);
    }

    public void SetActivePlayWebView(WebView2 webView)
    {
        _playWebView = webView;
        _sendHost.ActivePlayTabHost = webView;
        NotifyStatusChanged();
    }

    public async Task LoadAdventureAsync(Guid adventureId)
    {
        _bundle = AdventureStore.Load(adventureId);
        if (_bundle is null)
            return;

        _navigation.SetSessionTitle(_bundle.Metadata.Title);
        PlayCompanionRestoreService.ApplyEnterPlayPreferences(
            _bundle.Metadata.Settings,
            UiChromeStore.Load().PlaySurface);

        var meta = AdventureStore.ListIndex().FirstOrDefault(a => a.Id == adventureId);
        if (meta is not null)
            _navigation.SetSessionTitle(meta.Title);

        _sendHost.ActivePlayTabHost = _playWebView ?? _tabRegistry?.ActiveTabHost;
        WinUiEventLogger.Debug("play_session_load", "Adventure loaded for play", new { adventureId });
        _utilityWorker.OnAdventureLoaded(adventureId);
        _utilityWorker.UpdateActiveJobCount(adventureId);
        NotifyStatusChanged();
        await Task.CompletedTask;
    }

    public void ReloadBundle(Guid adventureId)
    {
        if (_bundle?.Metadata.Id == adventureId)
            _bundle = AdventureStore.Load(adventureId);
        NotifyStatusChanged();
    }

    public void NotifyStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);

    public ChatGptPlayComposeInjection? GetActiveComposeInjection()
    {
        if (_bundle is null || _tabRegistry is null)
            return null;

        var tabHost = _tabRegistry.ResolvePlayTabHost(_bundle, _playWebView)
                      ?? _tabRegistry.ActiveTabHost;
        if (tabHost is WebView2 wv && _composeInjections.TryGetValue(wv, out var injection))
            return injection;

        return null;
    }

    public void SetMergedPreview(string? preview)
    {
        _mergedPreview = preview;
        NotifyStatusChanged();
    }

    public void ClearMergedPreview() => SetMergedPreview(null);

    public string? GetMergedPreview() => _mergedPreview;

    public async Task EnsurePlayFeaturesAsync(WebView2 webView)
    {
        try
        {
            await EnsurePageHostAsync(webView);
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("play_features", ex, adventureId: _bundle?.Metadata.Id);
            WinUiEventLogger.Error(
                "play_features_failed",
                ex.Message,
                new { exceptionType = ex.GetType().Name },
                adventureId: _bundle?.Metadata.Id);
        }
    }

    public async Task EnsurePageHostAsync(WebView2 webView)
    {
        if (_tabHost is not null)
            await _tabHost.EnsureWebViewReadyAsync(webView);
        else
            await EnsureCoreWebViewAsync(webView);

        RegisterPlayComposeInjection(webView);
    }

    public async Task EnsurePlayTabReadyAsync(
        Guid adventureId,
        bool selectTab,
        bool navigateToBrowseTarget)
    {
        if (_tabHost is null)
            return;

        await _tabHost.EnsureInitializedAsync();
        if (_playWebView is { } wv)
        {
            if (selectTab)
                _tabHost.SelectWebView(wv);

            if (navigateToBrowseTarget)
                await NavigatePlayTargetAsync(wv);
        }
    }

    public bool PinActiveTab(WebView2 webView)
    {
        if (_bundle is null)
            return false;

        try
        {
            WinUiPlayTabPin.PinTab(_bundle, webView, _tabHost?.FindTabForWebView(webView));
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("play_pin_tab", ex);
            return false;
        }

        _playWebView = webView;
        _sendHost.ActivePlayTabHost = webView;
        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        NotifyStatusChanged();
        return true;
    }

    public void ClearPin()
    {
        if (_bundle is null)
            return;

        WinUiPlayTabPin.ClearPin(_bundle);
        _playWebView = null;
        _sendHost.ActivePlayTabHost = null;
        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        NotifyStatusChanged();
    }

    public async Task NavigatePlayTargetAsync(WebView2 webView)
    {
        if (_bundle is null)
            return;

        if (_tabHost is not null)
            await _tabHost.EnsureWebViewReadyAsync(webView);

        var url = PlayTabPinService.GetPlayTargetUrl(_bundle);
        if (string.IsNullOrWhiteSpace(url))
            url = "https://chatgpt.com/";

        if (webView.CoreWebView2 is { } core)
            WinUiChatNavigation.Navigate(core, url);
        else
            webView.Source = new Uri(url);

        SetActivePlayWebView(webView);
    }

    internal async Task<string> CompleteTurnAfterSendAsync(
        PlaySendTurnCompletionRequest request,
        IPlaySendHost host) =>
        await PlaySendTurnCompletionRuntime.CompleteAsync(request, host);

    public void SaveCompanionTab(string tabName)
    {
        if (_bundle is null)
            return;

        PlayCompanionRestoreService.PersistTab(_bundle.Metadata.Settings, tabName);
        AdventureStore.Save(_bundle);
    }

    public string ResolveCompanionTab() =>
        _bundle is null
            ? "Reference"
            : PlayCompanionRestoreService.ResolveTab(_bundle.Metadata.Settings, UiChromeStore.Load().PlaySurface);

    public void SaveCompanionSection(string section)
    {
        if (_bundle is null)
            return;

        PlayCompanionRestoreService.PersistSection(_bundle.Metadata.Settings, section);
        AdventureStore.Save(_bundle);
    }

    public string ResolveCompanionSection() =>
        _bundle is null
            ? "Session"
            : PlayCompanionRestoreService.ResolveSection(_bundle.Metadata.Settings, UiChromeStore.Load().PlaySurface);

    public void SaveSidePanelWidth(double width)
    {
        if (_bundle is null)
            return;

        _bundle.Metadata.Settings.PlaySidePanelWidth = width;
        AdventureStore.Save(_bundle);
    }

    public double ResolveSidePanelWidth(double fallback = 320)
    {
        var width = _bundle?.Metadata.Settings.PlaySidePanelWidth ?? 0;
        return width > 0 ? width : fallback;
    }

    public bool ResolveSidePanelCollapsed() =>
        _bundle?.Metadata.Settings.PlaySidePanelCollapsed ?? false;

    public void SetSidePanelCollapsed(bool collapsed)
    {
        if (_bundle is null)
            return;

        _bundle.Metadata.Settings.PlaySidePanelCollapsed = collapsed;
        AdventureStore.Save(_bundle);
    }

    private void RegisterPlayComposeInjection(WebView2 webView)
    {
        if (_bundle is null || _tabRegistry is null)
            return;

        var candidateTabKey = _tabRegistry.GetTabKey(webView);
        var playWebViewTabKey = _playWebView is not null
            ? _tabRegistry.GetTabKey(_playWebView)
            : null;
        var activeTabKey = _tabRegistry.ActiveTabHost is WebView2 active
            ? _tabRegistry.GetTabKey(active)
            : null;

        if (!PlayComposeInjectionPolicy.ShouldRegisterIntercept(
                new PlayComposeRegistrationContext(
                    IsPlayMode: true,
                    Bundle: _bundle,
                    CandidateTabKey: candidateTabKey,
                    PlayWebViewTabKey: playWebViewTabKey,
                    ActiveWebViewTabKey: activeTabKey,
                    SuppressPlayAutomation: false,
                    SuppressPlayAutomationOnActiveOnly: false)))
        {
            return;
        }

        if (!_composeInjections.TryGetValue(webView, out var injection))
        {
            WinUiWebView2CoreRuntime.EnsureManagedCoreLoaded();
            injection = new ChatGptPlayComposeInjection(
                webView,
                () => webView.CoreWebView2,
                () => ShouldUseWrapperComposer(webView),
                WireWinUiStandaloneComposeEvents);
            WireComposeInjection(injection);
            _composeInjections[webView] = injection;
        }

        injection.Register();
        _ = injection.SetNativePassthroughAsync(false);
        _sendHost.RefreshArmState(injection);
    }

    private static void WireWinUiStandaloneComposeEvents(object coreObj, ChatGptPlayComposeInjection injection)
    {
        var core = (CoreWebView2)coreObj;
        core.WebMessageReceived += (_, e) =>
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                json = e.WebMessageAsJson;

            injection.HandleStandaloneWebMessage(json);
        };

        core.NavigationCompleted += async (_, e) =>
            await injection.OnStandaloneNavigationCompletedObject(core, e);
    }

    private void WireComposeInjection(ChatGptPlayComposeInjection injection)
    {
        if (!_wiredComposeInjections.Add(injection))
            return;

        injection.SendRequested += (_, args) =>
            _ = _sendHost.RequestSendAsync(args, injection);

        injection.TextChanged += (_, _) =>
        {
            DebouncedUpdateMergedPreview();
            if (_bundle is null || injection.CoreWebView is not { } core)
                return;

            _ = PrefetchSendWarmupAsync(core, _bundle);
        };
    }

    private void DebouncedUpdateMergedPreview()
    {
        _mergedPreviewDebounceCts?.Cancel();
        _mergedPreviewDebounceCts = new CancellationTokenSource();
        var token = _mergedPreviewDebounceCts.Token;
        _ = DebouncedUpdateMergedPreviewCoreAsync(token);
    }

    private async Task DebouncedUpdateMergedPreviewCoreAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(350, token);
            await UpdateMergedPreviewAsync();
        }
        catch (OperationCanceledException)
        {
            /* superseded */
        }
    }

    private Task UpdateMergedPreviewAsync()
    {
        if (_bundle is null)
            return Task.CompletedTask;

        var injection = GetActiveComposeInjection();
        var attachmentContext = injection?.GetLastAttachmentContext();
        _sendHost.ArtifactStore.Bind(_bundle);
        var artifact = PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = _bundle,
            ComposeText = injection?.GetText() ?? "",
            AttachmentContext = attachmentContext,
            ConsumeContinuationQueue = false,
            ApplySurfaceActions = true,
            PriorThreadUserMessageCount = 0,
            ResolvePlayerLine = (b, consume, text) =>
                _sendHost.ResolvePlayerInput(b, consume, text),
            SyncThreadScope = _sendHost.SyncPlayThreadScopeForPacket,
        });
        _sendHost.ArtifactStore.Set(artifact);

        if (artifact is null)
        {
            ClearMergedPreview();
            _sendHost.RefreshArmState(injection);
            return Task.CompletedTask;
        }

        var preview = _bundle.Metadata.Settings.UseContextTags
            ? ContextTagFormat.FormatStructuredPreview(artifact.MergedText)
            : artifact.MergedText;
        SetMergedPreview(preview);
        _sendHost.RefreshArmState(injection);
        return Task.CompletedTask;
    }

    private bool ShouldUseWrapperComposer(WebView2 webView)
    {
        if (_bundle is null)
            return false;

        var source = PlayWebViewCoreBridge.GetSource(webView.CoreWebView2);
        var ctx = PlayTabCapabilityContext.FromRegistry(_bundle, webView, TabRegistry, source);
        return PlayWrapperComposerPolicy.ShouldUseWrapperComposer(
            PlayTabCapabilityResolver.Resolve(ctx, PlayTabSessionFactory.FromBundle(_bundle)));
    }

    private Task PrefetchSendWarmupAsync(object core, AdventureBundle bundle) =>
        _sendHost.PrefetchSendWarmupAsync(core, bundle);

    private static async Task EnsureCoreWebViewAsync(WebView2 webView)
    {
        if (WinUiWebViewCore.TryGetCore(webView) is not null)
        {
            await WinUiTranscriptViewCoordinator.OnTabReadyAsync(webView);
            return;
        }

        await WinUiWebViewEnvironment.GetAsync();
        using var initTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await webView.EnsureCoreWebView2Async().AsTask(initTimeout.Token);
        await WinUiTranscriptViewCoordinator.OnTabReadyAsync(webView);
    }
}

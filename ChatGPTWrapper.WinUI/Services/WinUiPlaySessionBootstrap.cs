using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>
/// WinUI play-session bootstrap — ports WPF <c>EnsurePlaySessionAsync</c> using the shell chat host.
/// </summary>
internal sealed class WinUiPlaySessionBootstrap
{
    private readonly WinUiPlaySessionService _session;
    private readonly ChatTabHost _chatHost;

    public WinUiPlaySessionBootstrap(WinUiPlaySessionService session, ChatTabHost chatHost)
    {
        _session = session;
        _chatHost = chatHost;
    }

    public async Task EnterPlayAsync(Guid adventureId, CancellationToken cancellationToken = default)
    {
        WinUiEventLogger.Info("play_session_start", "Entering play mode", new { adventureId });

        await _chatHost.EnsureInitializedAsync(cancellationToken);
        await _session.LoadAdventureAsync(adventureId);

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
        {
            WinUiEventLogger.Error("play_session_start_failed", "Adventure not found", adventureId: adventureId);
            return;
        }

        AdventureNavigationService.SyncLinkedFields(bundle);

        if (PlayThreadBindingService.SanitizeOnPlayOpen(bundle))
            bundle = AdventureStore.Load(adventureId) ?? bundle;

        var registry = _session.TabRegistry;
        var selectTab = !ProjectChatDraftService.IsActive(bundle);
        var webView = await ResolvePlayWebViewAsync(
            bundle,
            registry,
            promptToPinIfMissing: false,
            selectTab,
            navigateToBrowseTarget: true,
            cancellationToken);

        if (webView is null
            && PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle))
        {
            webView = await ResolvePlayWebViewAsync(
                bundle,
                registry,
                promptToPinIfMissing: true,
                selectTab,
                navigateToBrowseTarget: true,
                cancellationToken);
        }

        if (webView is null)
        {
            webView = await _chatHost.EnsureDefaultTabAsync(cancellationToken);
            if (webView is not null)
                await NavigatePlayTargetAsync(webView, bundle, cancellationToken);
        }

        if (webView is null)
        {
            WinUiEventLogger.Error(
                "play_session_start_failed",
                "No play WebView available",
                adventureId: adventureId);
            return;
        }

        _session.SetActivePlayWebView(webView);
        _ = _session.EnsurePlayFeaturesAsync(webView);

        WinUiEventLogger.Info("play_host_content_set", "Play session WebView ready", new
        {
            adventureId,
            uri = PlayWebViewCoreBridge.GetSource(webView.CoreWebView2),
        });
    }

    private async Task<WebView2?> ResolvePlayWebViewAsync(
        AdventureBundle bundle,
        IPlayTabRegistry registry,
        bool promptToPinIfMissing,
        bool selectTab,
        bool navigateToBrowseTarget,
        CancellationToken cancellationToken)
    {
        var webView = ResolvePinnedOrActive(bundle, registry);
        if (webView is not null)
        {
            await _chatHost.EnsureWebViewReadyAsync(webView, cancellationToken);
            if (navigateToBrowseTarget && webView.CoreWebView2 is { } core)
                await NavigateBrowseTargetIfNeededAsync(core, bundle, cancellationToken);

            if (selectTab)
                _chatHost.SelectWebView(webView);

            return webView;
        }

        if (ThreadWebViewResolver.HasPersistedSession(bundle, AdventureThreadKind.Play))
        {
            webView = await RestorePlayWebViewAsync(bundle, selectTab, cancellationToken);
            if (webView is not null)
                return webView;
        }

        if (!promptToPinIfMissing || !PlayTabPinService.ShouldOfferPinPromptOnOpen(bundle))
            return null;

        WinUiEventLogger.Debug(
            "play_pin_prompt",
            "Play tab not pinned — user should pick a browser tab",
            adventureId: bundle.Metadata.Id);
        return null;
    }

    private static WebView2? ResolvePinnedOrActive(AdventureBundle bundle, IPlayTabRegistry registry)
    {
        if (registry.TryFindTabHostForPlaySession(bundle) is WebView2 sessionTab)
            return sessionTab;

        var pinKey = PlayTabPinService.GetPlayPinKey(bundle);
        if (!string.IsNullOrWhiteSpace(pinKey)
            && registry.FindTabHostByPinKey(pinKey) is WebView2 pinned)
        {
            return pinned;
        }

        if (registry.ActiveTabHost is WebView2 active)
        {
            var source = PlayWebViewCoreBridge.GetSource(registry.GetCoreWebView(active));
            if (PlayTabPinService.IsOnPlayTarget(source, bundle)
                || AdventureNavigationService.IsOnLinkedProjectPage(source, bundle))
            {
                return active;
            }
        }

        return null;
    }

    private async Task<WebView2?> RestorePlayWebViewAsync(
        AdventureBundle bundle,
        bool selectTab,
        CancellationToken cancellationToken)
    {
        var targetUrl = AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle)
                        ?? ThreadWebViewResolver.ResolveTargetUrl(bundle, AdventureThreadKind.Play);
        if (targetUrl is null)
            return null;

        var webView = _chatHost.GetFirstWebView() ?? await _chatHost.EnsureDefaultTabAsync(cancellationToken);
        if (webView is null)
            return null;

        await _chatHost.EnsureWebViewReadyAsync(webView, cancellationToken);
        if (webView.CoreWebView2 is { } core)
        {
            WinUiChatNavigation.Navigate(core, targetUrl);
            await WinUiChatNavigation.WaitForNavigationAsync(core, targetUrl, cancellationToken: cancellationToken);
        }

        if (selectTab)
            _chatHost.SelectWebView(webView);

        return webView;
    }

    private static async Task NavigateBrowseTargetIfNeededAsync(
        object core,
        AdventureBundle bundle,
        CancellationToken cancellationToken)
    {
        var browseUrl = AdventureNavigationService.ResolvePlayBrowseUrl(bundle);
        if (browseUrl is null)
            return;

        var source = PlayWebViewCoreBridge.GetSource(core);
        if (!AdventureNavigationService.ShouldNavigateToPlayTarget(source, bundle, browseUrl))
            return;

        WinUiChatNavigation.Navigate(core, browseUrl);
        await WinUiChatNavigation.WaitForNavigationAsync(core, browseUrl, cancellationToken: cancellationToken);
    }

    private async Task NavigatePlayTargetAsync(
        WebView2 webView,
        AdventureBundle bundle,
        CancellationToken cancellationToken)
    {
        await _chatHost.EnsureWebViewReadyAsync(webView, cancellationToken);
        var url = PlayTabPinService.GetPlayTargetUrl(bundle);
        if (string.IsNullOrWhiteSpace(url))
            url = "https://chatgpt.com/";

        if (webView.CoreWebView2 is { } core)
        {
            WinUiChatNavigation.Navigate(core, url);
            await WinUiChatNavigation.WaitForNavigationAsync(core, url, cancellationToken: cancellationToken);
        }
        else
        {
            webView.Source = new Uri(url);
        }
    }
}

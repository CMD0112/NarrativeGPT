using System.Windows.Controls;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private readonly Dictionary<WebView2, CancellationTokenSource> _adventureNavGuardDebounce = new();
    private DispatcherTimer? _adventureNavWatchdog;

    private void WireAdventureNavigationGuard(WebView2 wv)
    {
        if (wv.CoreWebView2 is not { } core)
            return;

        core.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
                return;

            ScheduleAdventureNavigationGuardCheck(wv);
        };

        core.HistoryChanged += (_, _) => ScheduleAdventureNavigationGuardCheck(wv);
    }

    private void UpdateAdventureNavigationWatchdog()
    {
        if (_appMode is AppMode.Play or AppMode.Design && _activeAdventureId is not null)
        {
            if (_adventureNavWatchdog is null)
            {
                _adventureNavWatchdog = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(30),
                };
                _adventureNavWatchdog.Tick += (_, _) => _ = RunAdventureNavigationWatchdogAsync();
            }

            if (!_adventureNavWatchdog.IsEnabled)
                _adventureNavWatchdog.Start();

            return;
        }

        _adventureNavWatchdog?.Stop();
    }

    private async Task RunAdventureNavigationWatchdogAsync()
    {
        foreach (var wv in EnumerateChatGptWebViews())
            await TryRecoverAdventureNavigationAsync(wv);
    }

    private IEnumerable<WebView2> EnumerateChatGptWebViews()
    {
        foreach (var item in ChatTabs.Items)
        {
            if (item is TabItem { Content: WebView2 wv }
                && IsChatGptPage(wv.CoreWebView2))
            {
                yield return wv;
            }
        }
    }

    private void ScheduleAdventureNavigationGuardCheck(WebView2 wv)
    {
        lock (_adventureNavGuardDebounce)
        {
            if (_adventureNavGuardDebounce.TryGetValue(wv, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            var cts = new CancellationTokenSource();
            _adventureNavGuardDebounce[wv] = cts;
            _ = RunAdventureNavigationGuardCheckAsync(wv, cts);
        }
    }

    private async Task RunAdventureNavigationGuardCheckAsync(WebView2 wv, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(200, cts.Token);
            await TryRecoverAdventureNavigationAsync(wv);
        }
        catch (OperationCanceledException)
        {
            /* superseded */
        }
        finally
        {
            lock (_adventureNavGuardDebounce)
            {
                if (_adventureNavGuardDebounce.TryGetValue(wv, out var current) && ReferenceEquals(current, cts))
                    _adventureNavGuardDebounce.Remove(wv);
            }

            cts.Dispose();
        }
    }

    private async Task TryRecoverAdventureNavigationAsync(WebView2 wv)
    {
        if (Volatile.Read(ref _activePlaySendCount) > 0)
            return;

        if (_activeAdventureId is not { } adventureId)
            return;

        if (_appMode is not (AppMode.Play or AppMode.Design))
            return;

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null || !AdventureProjectBindingService.HasLinkedProject(bundle))
            return;

        if (!IsAdventureContextWebView(wv, bundle))
            return;

        if (wv.CoreWebView2 is not { } core)
            return;

        if (ProjectChatDraftService.IsActive(bundle)
            && (ProjectChatDraftService.ShouldStayOnProjectPage(bundle, core.Source)
                || ProjectChatDraftService.IsDraftTab(bundle, wv, ChatTabs)))
        {
            if (_appMode == AppMode.Play)
                RefreshPlayComposeNavigationState(wv, bundle);

            return;
        }

        var intent = _appMode == AppMode.Design
            ? AdventureNavigationIntent.Design
            : AdventureNavigationIntent.Play;

        var accessDenied = await AdventureNavigationRecoveryProbe.ShowsAccessDeniedAsync(core);

        if (!accessDenied
            && !await AdventureNavigationRecoveryProbe.RequiresRecoveryAsync(core, bundle, intent))
        {
            ClearAdventureNavigationRecoveryError();
            if (_appMode == AppMode.Play)
                RefreshPlayComposeNavigationState(wv, bundle);
            return;
        }

        if (!AdventureLinkedNavigationGuard.TryBeginRecovery(adventureId, wv.GetHashCode()))
        {
            if (AdventureLinkedNavigationGuard.HasExhaustedRecovery(adventureId, wv.GetHashCode()))
                SetAdventureNavigationRecoveryError(intent);

            return;
        }

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return;

        string? recoveryUrl;
        if (accessDenied && intent == AdventureNavigationIntent.Play)
        {
            PlayConversationPageService.ReleaseStalePlayThread(bundle);
            AdventureStore.Save(bundle);
            recoveryUrl = AdventureNavigationService.ResolveLinkedProjectPageUrl(bundle);
            ProjectLinkDiagnostics.Log(
                $"Adventure nav recovery: access denied on play thread; "
                + $"clearing stale binding and opening project page from {core.Source}");
        }
        else
        {
            recoveryUrl = AdventureNavigationService.ResolveRecoveryUrl(bundle, intent);
        }

        ProjectLinkDiagnostics.Log(
            $"Adventure nav recovery: adventure={adventureId} mode={_appMode} "
            + $"from={core.Source} target={recoveryUrl ?? ChatGptUrls.BuildProjectUrl(gizmoId)}");

        ClearAdventureNavigationRecoveryError();
        PlayContextSessionCache.Invalidate(adventureId);

        var recovered = await TryRecoverToLinkedProjectAsync(wv, gizmoId, recoveryUrl);
        if (!recovered
            && await AdventureNavigationRecoveryProbe.RequiresRecoveryAsync(core, bundle, intent))
        {
            SetAdventureNavigationRecoveryError(intent);
        }
    }

    private async Task<bool> TryRecoverToLinkedProjectAsync(
        WebView2 wv,
        string gizmoId,
        string? recoveryUrl)
    {
        if (wv.CoreWebView2 is not { } core)
            return false;

        WireProjectServices(wv);

        if (!string.IsNullOrWhiteSpace(recoveryUrl)
            && Uri.TryCreate(recoveryUrl, UriKind.Absolute, out var recoveryUri)
            && ChatGptUrls.TryParseConversationId(recoveryUri, out var recoveryConversationId)
            && !string.IsNullOrWhiteSpace(recoveryConversationId))
        {
            ProjectLinkDiagnostics.Log(
                $"Adventure nav recovery: navigating to thread {recoveryUrl} from {core.Source}");
            core.Navigate(recoveryUrl);
            await WaitForChatGptNavigationAsync(core, timeoutMs: 30000);

            if (_activeAdventureId is { } adventureId)
            {
                var bundle = AdventureStore.Load(adventureId);
                if (bundle is not null
                    && AdventurePlayContextService.IsOnPlayConversationPage(
                        core.Source,
                        recoveryConversationId,
                        gizmoId))
                {
                    return true;
                }
            }

            return AdventurePlayContextService.IsOnPlayConversationPage(
                core.Source,
                recoveryConversationId,
                gizmoId);
        }

        try
        {
            if (_projectApiService is not null)
            {
                await _projectApiService.EnsureProjectPageAsync(core, gizmoId);
                return AdventureNavigationService.IsOnLinkedProjectPage(core.Source, new AdventureBundle
                {
                    Metadata = new AdventureMetadata { LinkedProjectId = gizmoId },
                });
            }
        }
        catch (ChatGptApiException ex) when (ex.StatusCode == 401)
        {
            ProjectLinkDiagnostics.Log($"Adventure nav recovery auth failed: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"Adventure nav recovery EnsureProjectPage failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(recoveryUrl))
            recoveryUrl = ChatGptUrls.BuildProjectUrl(gizmoId);

        core.Navigate(recoveryUrl);
        await WaitForChatGptNavigationAsync(core, timeoutMs: 30000);

        return !AdventureNavigationService.IsGenericHomepage(core.Source);
    }

    private bool IsAdventureContextWebView(WebView2 wv, AdventureBundle bundle)
    {
        if (ReferenceEquals(wv, _playWebView) || ReferenceEquals(wv, _designWebView))
            return true;

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey)
            && PlayTabPinService.FindWebViewByPinKey(ChatTabs, bundle.Metadata.PinnedPlayTabKey) == wv)
        {
            return true;
        }

        if (DesignTabPinService.TryFindWebViewForDesignSession(ChatTabs, bundle) == wv)
            return true;

        return ReferenceEquals(GetActiveWebView(), wv);
    }

    private void SetAdventureNavigationRecoveryError(AdventureNavigationIntent intent)
    {
        var message = AdventureNavigationService.FormatHomepageRecoveryError(intent);
        Dispatcher.Invoke(() =>
        {
            if (_appMode == AppMode.Play && _playView is not null)
                _playView.SetSessionError(message);
            else if (_appMode == AppMode.Design && _designView is not null)
                _designView.SetThreadStatus(message);
        });
    }

    private void ClearAdventureNavigationRecoveryError()
    {
        Dispatcher.Invoke(() =>
        {
            if (_appMode == AppMode.Play)
                UpdatePlayLinkStatus();
            else if (_appMode == AppMode.Design)
                UpdateDesignLinkStatus();
        });
    }
}

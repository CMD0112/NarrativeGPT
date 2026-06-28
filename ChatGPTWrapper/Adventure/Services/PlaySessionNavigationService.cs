using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal enum PlayNavigationIntent
{
    OpenSession,
    RecoverSession,
    PreSend,
}

internal sealed class PlayNavigationResult
{
    public bool Success { get; init; }

    public bool OnProjectPage { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Sole owner of play WebView navigation decisions for linked Project adventures.
/// </summary>
internal static class PlaySessionNavigationService
{
    public static string? ResolveBrowseUrl(AdventureBundle bundle) =>
        PlayThreadBindingService.ResolveBrowsableUrl(bundle);

    public static bool ShouldNavigateToBrowseTarget(
        string? source,
        AdventureBundle bundle,
        string? targetUrl)
    {
        if (string.IsNullOrWhiteSpace(targetUrl))
            return false;

        if (ProjectChatDraftService.ShouldStayOnProjectPage(bundle, source))
            return false;

        if (PlayTabPinService.IsOnPlayTarget(source, bundle))
            return false;

        if (!PlayThreadBindingService.HasBrowsablePlayTarget(bundle))
        {
            var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
            if (string.IsNullOrWhiteSpace(gizmoId))
                return false;

            var projectUrl = ChatGptUrls.BuildProjectUrl(ChatGptUrls.NormalizeGizmoId(gizmoId));
            return !AdventureNavigationService.IsOnLinkedProjectPage(source, bundle)
                   && !string.Equals(source, projectUrl, StringComparison.OrdinalIgnoreCase);
        }

        if (AdventureNavigationService.IsGenericHomepage(source))
            return true;

        return !string.Equals(source, targetUrl, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<PlayNavigationResult> EnsureOnBrowsableTargetAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        AdventureTurnService? turnService,
        PlayNavigationIntent intent = PlayNavigationIntent.OpenSession,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            return new PlayNavigationResult
            {
                Success = false,
                Error = "missing_linked_project",
            };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (AdventureNavigationService.IsGenericHomepage(core.Source))
        {
            ProjectLinkDiagnostics.Log($"Play navigation recovering from homepage for project {gizmoId}");
            if (PlayThreadBindingService.HasBrowsablePlayTarget(bundle))
            {
                var conversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
                if (!string.IsNullOrWhiteSpace(conversationId))
                {
                    return await NavigateToVerifiedConversationAsync(
                        core,
                        bundle,
                        gizmoId,
                        conversationId,
                        turnService,
                        cancellationToken);
                }
            }

            await api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
            return new PlayNavigationResult
            {
                Success = true,
                OnProjectPage = AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle),
            };
        }

        if (!PlayThreadBindingService.HasBrowsablePlayTarget(bundle))
        {
            if (!AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
            {
                var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
                ProjectLinkDiagnostics.Log($"Play navigation: project page only {projectUrl} from {core.Source}");
                core.Navigate(projectUrl);
                await WaitForProjectPageAsync(core, bundle, cancellationToken);
            }

            return new PlayNavigationResult
            {
                Success = true,
                OnProjectPage = true,
                ConversationId = PlayThreadBindingService.GetActiveConversationId(bundle),
            };
        }

        var verifiedConversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        if (string.IsNullOrWhiteSpace(verifiedConversationId))
        {
            return new PlayNavigationResult
            {
                Success = true,
                OnProjectPage = AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle),
            };
        }

        if (AdventurePlayContextService.IsOnPlayConversationPage(core, verifiedConversationId, gizmoId))
        {
            return new PlayNavigationResult
            {
                Success = true,
                ConversationId = verifiedConversationId,
            };
        }

        return await NavigateToVerifiedConversationAsync(
            core,
            bundle,
            gizmoId,
            verifiedConversationId,
            turnService,
            cancellationToken);
    }

    public static async Task<PlayNavigationResult> RecoverSessionAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default)
    {
        PlayContextSessionCache.Invalidate(bundle.Metadata.Id);

        if (ProjectChatDraftService.IsActive(bundle)
            && (ProjectChatDraftService.ShouldStayOnProjectPage(bundle, core.Source)
                || ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id)
                    is ProjectChatDraftKind.Utility or ProjectChatDraftKind.Design))
        {
            return new PlayNavigationResult
            {
                Success = AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle),
                OnProjectPage = true,
            };
        }

        return await EnsureOnBrowsableTargetAsync(
            core,
            bundle,
            api,
            turnService,
            PlayNavigationIntent.RecoverSession,
            cancellationToken);
    }

    private static async Task<PlayNavigationResult> NavigateToVerifiedConversationAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        string gizmoId,
        string conversationId,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken)
    {
        if (PlayThreadBindingService.IsRejectedConversationId(bundle, conversationId))
        {
            ProjectLinkDiagnostics.Log(
                $"Play navigation: skipping rejected conversation {conversationId}");
            await NavigateToProjectPageAsync(core, gizmoId, cancellationToken);
            return new PlayNavigationResult
            {
                Success = false,
                OnProjectPage = true,
                Error = "Play thread was rejected by ChatGPT. Pin a tab on your Project page and send to bind.",
            };
        }

        var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(
            conversationId,
            gizmoId,
            core.Source,
            PlayThreadBindingService.GetActivePlayEntry(bundle)?.PinnedTabUrl);

        ProjectLinkDiagnostics.Log($"Play navigation: verified thread {targetUrl}");
        core.Navigate(targetUrl);

        if (!await WaitForVerifiedConversationAsync(core, conversationId, gizmoId, cancellationToken))
        {
            if (IsRedirectToProjectPage(core.Source, conversationId, gizmoId))
            {
                PlayThreadBindingService.MarkRejected(bundle, conversationId, "redirect_to_project");
                PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
                AdventureStore.Save(bundle);
                await NavigateToProjectPageAsync(core, gizmoId, cancellationToken);
                return new PlayNavigationResult
                {
                    Success = false,
                    OnProjectPage = true,
                    ConversationId = conversationId,
                    Error =
                        "ChatGPT redirected away from the linked play thread. "
                        + "Open your Project → New chat → pin the tab, then send.",
                };
            }

            return new PlayNavigationResult
            {
                Success = false,
                ConversationId = conversationId,
                Error = "Timed out waiting for the verified play thread to load.",
            };
        }

        return new PlayNavigationResult
        {
            Success = true,
            ConversationId = conversationId,
        };
    }

    private static async Task NavigateToProjectPageAsync(
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var current)
            || !string.Equals(current.AbsolutePath.TrimEnd('/'), new Uri(projectUrl).AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
        {
            core.Navigate(projectUrl);
            await WaitForTrustedNavigationAsync(core, cancellationToken);
        }
    }

    private static async Task WaitForProjectPageAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
                return;

            await Task.Delay(250, cancellationToken);
        }
    }

    private static async Task<bool> WaitForVerifiedConversationAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        if (AdventurePlayContextService.IsOnPlayConversationPage(core, conversationId, gizmoId))
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            if (AdventurePlayContextService.IsOnPlayConversationPage(core, conversationId, gizmoId))
            {
                tcs.TrySetResult(true);
                return;
            }

            if (IsRedirectToProjectPage(core.Source, conversationId, gizmoId))
                tcs.TrySetResult(false);
        }

        core.NavigationCompleted += Handler;
        try
        {
            var completed = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return completed && AdventurePlayContextService.IsOnPlayConversationPage(core, conversationId, gizmoId);
        }
        catch (TimeoutException)
        {
            if (IsRedirectToProjectPage(core.Source, conversationId, gizmoId))
                return false;

            return AdventurePlayContextService.IsOnPlayConversationPage(core, conversationId, gizmoId);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    internal static bool IsRedirectToProjectPage(string? source, string conversationId, string gizmoId)
    {
        if (AdventurePlayContextService.IsOnPlayConversationPage(source, conversationId, gizmoId))
            return false;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri)
            || !ChatGptUrls.TryParseGizmoId(uri, out var parsedGizmo)
            || !ChatGptUrls.GizmoIdsEqual(parsedGizmo, gizmoId))
        {
            return false;
        }

        return !ChatGptUrls.TryParseConversationId(uri, out _);
    }

    private static async Task WaitForTrustedNavigationAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess
                && Uri.TryCreate(core.Source, UriKind.Absolute, out var u)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(u))
            {
                tcs.TrySetResult();
            }
        }

        core.NavigationCompleted += Handler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch (TimeoutException)
        {
            /* best effort */
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }
}

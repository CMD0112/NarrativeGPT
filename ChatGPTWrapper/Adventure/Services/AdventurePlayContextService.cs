using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal enum PlayContextStatus
{
    Legacy,
    Ready,
    MissingProject,
    NoConversation,
    NavigationFailed,
}

internal sealed class PlayContextResult
{
    public PlayContextStatus Status { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }

    public bool IsReady => Status == PlayContextStatus.Ready || Status == PlayContextStatus.Legacy;

    public bool IsLegacy => Status == PlayContextStatus.Legacy;
}

internal static class AdventurePlayContextService
{
    public static bool PreferAdventureWebViewForLinkedProject(bool isPlayMode, AdventureBundle? bundle) =>
        PlayTabPinService.PreferPinnedPlayWebView(isPlayMode, bundle);

    public static bool PreferPinnedPlayWebView(bool isPlayMode, AdventureBundle? bundle) =>
        PlayTabPinService.PreferPinnedPlayWebView(isPlayMode, bundle);

    public static bool ConversationBelongsToProject(
        string? conversationId,
        IReadOnlyList<GizmoConversationRef> projectConversations) =>
        !string.IsNullOrWhiteSpace(conversationId)
        && projectConversations.Any(c =>
            string.Equals(c.Id, conversationId, StringComparison.OrdinalIgnoreCase));

    public static bool ShouldAcceptLinkedConversationId(
        AdventureBundle bundle,
        string? candidateId,
        IReadOnlyList<GizmoConversationRef>? projectConversations = null)
    {
        if (string.IsNullOrWhiteSpace(candidateId))
            return false;

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return true;

        if (string.Equals(candidateId, bundle.Metadata.LinkedConversationId, StringComparison.OrdinalIgnoreCase))
            return true;

        return projectConversations is not null
               && ConversationBelongsToProject(candidateId, projectConversations);
    }

    public static async Task<PlayContextResult> EnsureLinkedProjectPlayContextAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        AdventureTurnService? turnService = null,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            await NavigateLegacyPlayAsync(core, bundle, cancellationToken);
            return new PlayContextResult { Status = PlayContextStatus.Legacy };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var conversationId = bundle.Metadata.LinkedConversationId;
        var composerAlreadyReady = false;
        var trustedUrlContext = false;

        if (string.IsNullOrWhiteSpace(conversationId)
            && AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
        {
            ProjectLinkDiagnostics.Log(
                $"Play context stable on linked project page for {gizmoId}; skipping play thread navigation");
            return new PlayContextResult
            {
                Status = PlayContextStatus.Ready,
                ConversationId = conversationId,
            };
        }

        if (AdventureNavigationService.IsGenericHomepage(core.Source))
        {
            ProjectLinkDiagnostics.Log($"Play context recovering from homepage for project {gizmoId}");
            var preservedConversationId = bundle.Metadata.LinkedConversationId;
            if (!string.IsNullOrWhiteSpace(preservedConversationId))
            {
                var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(
                    preservedConversationId,
                    gizmoId,
                    core.Source,
                    bundle.Metadata.PinnedPlayTabUrl);
                ProjectLinkDiagnostics.Log($"Homepage recovery: trying play thread {targetUrl}");
                core.Navigate(targetUrl);
                if (await WaitForPlayConversationNavigationAsync(
                        core,
                        preservedConversationId,
                        gizmoId,
                        cancellationToken))
                {
                    return new PlayContextResult
                    {
                        Status = PlayContextStatus.Ready,
                        ConversationId = preservedConversationId,
                    };
                }

                ProjectLinkDiagnostics.Log(
                    $"Homepage recovery: play thread navigation failed conv={preservedConversationId} href={core.Source}");
                return new PlayContextResult
                {
                    Status = PlayContextStatus.NavigationFailed,
                    ConversationId = preservedConversationId,
                    Error = "Could not return to the linked play thread after ChatGPT redirected to the homepage.",
                };
            }

            await api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
            return new PlayContextResult { Status = PlayContextStatus.Ready };
        }

        if (TryGetLinkedProjectConversationFromUrl(core.Source, gizmoId, out var urlConversationId))
        {
            conversationId = urlConversationId;
            PersistConversation(bundle, gizmoId, conversationId);
            trustedUrlContext = IsOnPlayConversationPage(core, conversationId, gizmoId);
            if (trustedUrlContext && turnService is not null)
            {
                var health = await turnService.GetHealthAsync(core);
                composerAlreadyReady = health.BridgeReachable && health.ComposerFound;
            }
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            var resolved = await ResolvePlayThreadAsync(
                core,
                gizmoId,
                api,
                turnService,
                cancellationToken);
            conversationId = resolved.ConversationId;
            composerAlreadyReady = resolved.ComposerReady;
            if (string.IsNullOrWhiteSpace(conversationId) && !resolved.ComposerReady)
            {
                return new PlayContextResult
                {
                    Status = PlayContextStatus.NoConversation,
                    Error = "Could not create or find a play thread in the linked Project.",
                };
            }

            if (!string.IsNullOrWhiteSpace(conversationId))
                PersistConversation(bundle, gizmoId, conversationId);
            else
            {
                ProjectLinkDiagnostics.Log(
                    $"Project chat composer ready for {gizmoId}; conversation id will bind on first Send");
            }
        }
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            var targetUrl = ChatGptUrls.ResolveProjectConversationUrl(
                conversationId,
                gizmoId,
                core.Source,
                bundle.Metadata.PinnedPlayTabUrl);
            if (!IsOnPlayConversationPage(core, conversationId, gizmoId))
            {
                ProjectLinkDiagnostics.Log($"Navigating Adventure tab to project play thread {targetUrl}");
                core.Navigate(targetUrl);
                if (!await WaitForPlayConversationNavigationAsync(core, conversationId, gizmoId, cancellationToken))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Play thread navigation timed out conv={conversationId} href={core.Source}");
                    return new PlayContextResult
                    {
                        Status = PlayContextStatus.NavigationFailed,
                        ConversationId = conversationId,
                        Error = "Timed out waiting for the Project play thread to load.",
                    };
                }

                if (AdventureNavigationService.IsGenericHomepage(core.Source))
                {
                    ProjectLinkDiagnostics.Log(
                        $"Play thread navigation rejected conv={conversationId}; href={core.Source}");
                    return new PlayContextResult
                    {
                        Status = PlayContextStatus.NavigationFailed,
                        ConversationId = conversationId,
                        Error = "ChatGPT rejected navigation to the linked play thread.",
                    };
                }
            }
        }

        await WaitForDocumentReadyAsync(core, cancellationToken);
        if (turnService is not null && !composerAlreadyReady)
        {
            if (!await WaitForComposerAsync(turnService, core, cancellationToken))
            {
                ProjectLinkDiagnostics.Log(
                    $"Composer not found on play thread conv={conversationId} href={core.Source}");
                return new PlayContextResult
                {
                    Status = PlayContextStatus.NavigationFailed,
                    ConversationId = conversationId,
                    Error = "Project play thread loaded but the ChatGPT composer was not found.",
                };
            }
        }

        return new PlayContextResult
        {
            Status = PlayContextStatus.Ready,
            ConversationId = conversationId,
        };
    }

    public static Task EnsureLinkedProjectPageAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
            return Task.CompletedTask;

        return api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
    }

    public static async Task NavigateToPlayThreadAfterSyncAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        ChatGptProjectApiService api,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return;

        await EnsureLinkedProjectPlayContextAsync(core, bundle, api, cancellationToken: cancellationToken);
    }

    private static async Task NavigateLegacyPlayAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
        {
            if (!IsOnConversationPage(core.Source, bundle.Metadata.LinkedConversationId))
            {
                core.Navigate(ChatGptUrls.BuildConversationUrl(bundle.Metadata.LinkedConversationId));
                await WaitForTrustedNavigationAsync(core, cancellationToken);
            }

            return;
        }

        if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            core.Navigate(AdventureNavigationService.ResolveTrustedFallbackUrl(bundle));
            await WaitForTrustedNavigationAsync(core, cancellationToken);
        }
    }

    private static void PersistConversation(AdventureBundle bundle, string gizmoId, string conversationId)
    {
        var previous = bundle.Metadata.LinkedConversationId;
        PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);
        bundle.Metadata.LinkedConversationId = conversationId;
        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.PlayConversationId = conversationId;
        else
        {
            bundle.Metadata.ProjectLink = new ProjectLink
            {
                GizmoId = gizmoId,
                CanonicalUrl = ChatGptUrls.BuildProjectUrl(gizmoId),
                PlayConversationId = conversationId,
                LinkedAt = DateTimeOffset.UtcNow,
            };
        }

        AdventureStore.Save(bundle);
    }

    private sealed class PlayThreadResolution
    {
        public string? ConversationId { get; init; }

        public bool ComposerReady { get; init; }
    }

    private static async Task<PlayThreadResolution> ResolvePlayThreadAsync(
        CoreWebView2 core,
        string gizmoId,
        ChatGptProjectApiService api,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken)
    {
        try
        {
            await api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

            string? conversationId;
            try
            {
                conversationId = await api.CreateProjectConversationAsync(
                    core,
                    gizmoId,
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"CreateProjectConversation threw for {gizmoId}: {ex.Message}");
                conversationId = null;
            }

            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                ProjectLinkDiagnostics.Log($"Created project play thread {conversationId} for {gizmoId} via API");
                return new PlayThreadResolution { ConversationId = conversationId };
            }

            ProjectLinkDiagnostics.Log(
                $"CreateProjectConversation returned no id for {gizmoId}; listing project conversations");

            IReadOnlyList<GizmoConversationRef> convs = Array.Empty<GizmoConversationRef>();
            try
            {
                convs = await api.ListProjectConversationsAsync(core, gizmoId, cancellationToken);
            }
            catch (Exception ex)
            {
                ProjectLinkDiagnostics.Log($"ListProjectConversations failed for {gizmoId}: {ex.Message}");
            }

            conversationId = convs.OrderByDescending(c => c.UpdatedAt).FirstOrDefault()?.Id;
            if (!string.IsNullOrWhiteSpace(conversationId))
                return new PlayThreadResolution { ConversationId = conversationId };

            ProjectLinkDiagnostics.Log(
                $"No play thread in project {gizmoId} (list count={convs.Count}); trying UI New chat");

            if (turnService is null)
                return new PlayThreadResolution();

            var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
            if (!ui.Success)
            {
                ProjectLinkDiagnostics.Log($"UI New chat in project failed: {ui.Error}");
                return new PlayThreadResolution();
            }

            conversationId = ui.ConversationId;
            if (string.IsNullOrWhiteSpace(conversationId))
                conversationId = await turnService.GetConversationIdAsync(core);

            if (!string.IsNullOrWhiteSpace(conversationId))
            {
                ProjectLinkDiagnostics.Log($"UI started project play thread {conversationId} for {gizmoId}");
                return new PlayThreadResolution
                {
                    ConversationId = conversationId,
                    ComposerReady = ui.ComposerReady,
                };
            }

            ProjectLinkDiagnostics.Log($"UI New chat opened composer for {gizmoId} (no conversation id yet)");
            return new PlayThreadResolution { ComposerReady = ui.ComposerReady };
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"ResolvePlayThread failed for {gizmoId}: {ex.Message}");
            return new PlayThreadResolution();
        }
    }

    internal static bool TryGetLinkedProjectConversationFromUrl(
        string? source,
        string gizmoId,
        out string conversationId)
    {
        conversationId = "";
        if (string.IsNullOrWhiteSpace(source))
            return false;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var parsedConv)
            || string.IsNullOrWhiteSpace(parsedConv))
        {
            return false;
        }

        if (ChatGptUrls.TryParseGizmoId(uri, out var parsedGizmo)
            && ChatGptUrls.GizmoIdsEqual(parsedGizmo, gizmoId))
        {
            conversationId = parsedConv;
            return true;
        }

        return false;
    }

    internal static bool IsOnProjectConversationPage(CoreWebView2 core, string conversationId, string gizmoId) =>
        IsOnProjectConversationPage(core.Source, conversationId, gizmoId);

    internal static bool IsOnProjectConversationPage(string? source, string conversationId, string gizmoId)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;

        if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return false;

        if (!ChatGptUrls.TryParseConversationId(uri, out var parsedConv)
            || !string.Equals(parsedConv, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return ChatGptUrls.TryParseGizmoId(uri, out var parsedGizmo)
               && ChatGptUrls.GizmoIdsEqual(parsedGizmo, gizmoId);
    }

    internal static bool IsOnConversationPage(string? source, string conversationId)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return false;

        if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return false;

        return ChatGptUrls.TryParseConversationId(uri, out var parsedConv)
               && string.Equals(parsedConv, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOnPlayConversationPage(CoreWebView2 core, string conversationId, string gizmoId) =>
        IsOnPlayConversationPage(core.Source, conversationId, gizmoId);

    internal static bool IsOnPlayConversationPage(string? source, string conversationId, string gizmoId) =>
        IsOnProjectConversationPage(source, conversationId, gizmoId)
        || IsOnConversationPage(source, conversationId);

    private static async Task<bool> WaitForPlayConversationNavigationAsync(
        CoreWebView2 core,
        string conversationId,
        string gizmoId,
        CancellationToken cancellationToken)
    {
        if (IsOnPlayConversationPage(core, conversationId, gizmoId))
            return true;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && IsOnPlayConversationPage(core, conversationId, gizmoId))
                tcs.TrySetResult(true);
        }

        core.NavigationCompleted += Handler;
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (TimeoutException)
        {
            return IsOnPlayConversationPage(core, conversationId, gizmoId);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task<bool> WaitForComposerAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var health = await turnService.GetHealthAsync(core);
            if (health.ComposerFound)
                return true;

            await Task.Delay(500, cancellationToken);
        }

        return false;
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

    private static async Task WaitForDocumentReadyAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var raw = await core.ExecuteScriptAsync(
                    "(() => document.readyState === 'complete' || document.readyState === 'interactive')");
                if (raw.Contains("true", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch
            {
                /* page may still be loading */
            }

            await Task.Delay(300, cancellationToken);
        }
    }

    private static Task ReturnToLinkedProjectPageAsync(
        ChatGptProjectApiService api,
        CoreWebView2 core,
        string gizmoId,
        CancellationToken cancellationToken) =>
        api.EnsureProjectPageAsync(core, gizmoId, cancellationToken);
}

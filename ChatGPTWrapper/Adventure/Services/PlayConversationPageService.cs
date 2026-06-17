using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class PlayThreadPageResult
{
    public bool Success { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }
}

/// <summary>
/// Play-thread page checks for send: the pinned tab URL is authoritative when the user is already in a chat.
/// </summary>
internal static class PlayConversationPageService
{
    public static async Task<string?> GetBrowserHrefAsync(CoreWebView2 core) =>
        await UtilityConversationPageService.GetPageHrefAsync(core);

    public static bool TryGetBrowserConversationId(string? source, out string conversationId)
    {
        conversationId = "";
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var parsed)
            || string.IsNullOrWhiteSpace(parsed))
        {
            return false;
        }

        conversationId = parsed;
        return true;
    }

    /// <summary>
    /// Adopts the conversation shown in the browser as the linked play thread (plain /c/ URLs included).
    /// </summary>
    public static bool TryAdoptBrowserConversation(AdventureBundle bundle, string? source)
    {
        if (!TryGetBrowserConversationId(source, out var parsed))
            return false;

        if (!IsAdoptablePlayConversationUrl(bundle, source, parsed))
            return false;

        if (string.Equals(bundle.Metadata.LinkedConversationId, parsed, StringComparison.OrdinalIgnoreCase))
            return false;

        var previous = bundle.Metadata.LinkedConversationId;
        PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, parsed);
        bundle.Metadata.LinkedConversationId = parsed;
        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.PlayConversationId = parsed;

        if (ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id) == ProjectChatDraftKind.Play)
            ProjectChatDraftService.Complete(bundle);

        return true;
    }

    internal static bool IsAdoptablePlayConversationUrl(
        AdventureBundle bundle,
        string? source,
        string conversationId)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return AdventurePlayContextService.IsOnConversationPage(source, conversationId);

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return false;

        if (AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(source, gizmoId, out _))
            return true;

        return AdventurePlayContextService.IsOnConversationPage(source, conversationId);
    }

    public static async Task<PlayThreadPageResult> EnsureReadyForPlaySendAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            return new PlayThreadPageResult
            {
                Success = false,
                Error = "missing_linked_project",
            };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var href = await GetBrowserHrefAsync(core);
        TryGetBrowserConversationId(href, out var browserConversationId);

        if (!string.IsNullOrWhiteSpace(browserConversationId)
            && IsAdoptablePlayConversationUrl(bundle, href, browserConversationId))
        {
            if (!await AdventureNavigationRecoveryProbe.ShowsAccessDeniedAsync(core))
            {
                TryAdoptBrowserConversation(bundle, href);
                return new PlayThreadPageResult
                {
                    Success = true,
                    ConversationId = browserConversationId,
                };
            }

            ProjectLinkDiagnostics.Log(
                $"Play send: access denied on browser conversation {browserConversationId}; "
                + $"stored={bundle.Metadata.LinkedConversationId} href={href}");
        }

        var storedConversationId = bundle.Metadata.LinkedConversationId;
        if (string.IsNullOrWhiteSpace(storedConversationId)
            || ProjectChatDraftService.ShouldStayOnProjectPage(bundle, href))
        {
            // Fresh play thread (or draft mode): user may be on the linked Project page (often still /project
            // after "New chat") with the start packet pasted in the composer.
            if (AdventureNavigationService.IsOnLinkedProjectPage(href, bundle))
            {
                return new PlayThreadPageResult
                {
                    Success = true,
                    ConversationId = string.IsNullOrWhiteSpace(browserConversationId)
                        ? null
                        : browserConversationId,
                };
            }

            if (string.IsNullOrWhiteSpace(storedConversationId))
            {
                return new PlayThreadPageResult
                {
                    Success = false,
                    Error =
                        "No play thread is linked. In the pinned Play tab, click New chat in your Project, "
                        + "paste the start packet (Ctrl+V), then send again.",
                };
            }
        }

        if (!string.IsNullOrWhiteSpace(browserConversationId)
            && string.Equals(browserConversationId, storedConversationId, StringComparison.OrdinalIgnoreCase)
            && await AdventureNavigationRecoveryProbe.ShowsAccessDeniedAsync(core))
        {
            PlayThreadRotationService.ReleasePlayThread(bundle);
            PlayThreadRotationService.PersistRelease(bundle);
            return new PlayThreadPageResult
            {
                Success = false,
                Error =
                    "This play thread is no longer accessible in ChatGPT. "
                    + "In the pinned tab, open your Project → your play chat, then send again "
                    + "(or use Start new play thread).",
            };
        }

        var page = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
            core,
            storedConversationId,
            gizmoId,
            cancellationToken);
        if (!page.Success)
        {
            return new PlayThreadPageResult
            {
                Success = false,
                ConversationId = storedConversationId,
                Error = page.Error ?? "Could not open the linked play thread before send.",
            };
        }

        if (await AdventureNavigationRecoveryProbe.ShowsAccessDeniedAsync(core))
        {
            PlayThreadRotationService.ReleasePlayThread(bundle);
            PlayThreadRotationService.PersistRelease(bundle);
            return new PlayThreadPageResult
            {
                Success = false,
                Error =
                    "The saved play thread is no longer accessible. "
                    + "Open your current play chat in the pinned ChatGPT tab and send again.",
            };
        }

        return new PlayThreadPageResult
        {
            Success = true,
            ConversationId = storedConversationId,
        };
    }

    public static void ReleaseStalePlayThread(AdventureBundle bundle)
    {
        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);
    }
}

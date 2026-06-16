using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlayThreadRotationService
{
    /// <summary>
    /// Clears play tab pin and conversation binding while keeping the linked Project.
    /// Ends the current play session and opens a fresh session scope for the next thread.
    /// </summary>
    public static void ReleasePlayThread(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        bundle.Metadata.PinnedPlayTabKey = null;
        bundle.Metadata.PinnedPlayTabTitle = null;
        bundle.Metadata.PinnedPlayTabUrl = null;
        bundle.Metadata.LinkedConversationId = null;

        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.PlayConversationId = null;

        AdventureSessionService.EndSession(bundle);
        AdventureSessionService.EnsureSession(bundle);
        PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
    }

    public static void PersistRelease(AdventureBundle bundle) =>
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);

    public static void BindPlayConversation(AdventureBundle bundle, string gizmoId, string conversationId)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var previous = bundle.Metadata.LinkedConversationId;
        PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);
        bundle.Metadata.LinkedConversationId = conversationId;

        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.PlayConversationId = conversationId;
        else
        {
            bundle.Metadata.ProjectLink = new ProjectLink
            {
                GizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId),
                CanonicalUrl = ChatGptUrls.BuildProjectUrl(gizmoId),
                PlayConversationId = conversationId,
                LinkedAt = DateTimeOffset.UtcNow,
            };
        }

        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
    }

    public static bool ShouldRejectApiConversation(CreateProjectConversationResult result) =>
        string.IsNullOrWhiteSpace(result.ConversationId) || result.ClientBootstrapped;

    public static bool IsUsablePlayConversationId(string? conversationId, string gizmoId, string? source)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return false;

        if (AdventurePlayContextService.IsOnPlayConversationPage(source, conversationId, gizmoId))
            return true;

        return AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                   source,
                   gizmoId,
                   out var parsed)
               && string.Equals(parsed, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatThreadStatus(AdventureBundle bundle)
    {
        var conversationId = bundle.Metadata.LinkedConversationId;
        if (string.IsNullOrWhiteSpace(conversationId))
            return "Play thread: not bound — use Start new play thread… or link a tab.";

        var shortId = conversationId.Length > 12
            ? conversationId[..12] + "…"
            : conversationId;
        return $"Play thread: {shortId}";
    }

    public static string FormatStartThreadReadyMessage(string? source, AdventureBundle bundle)
    {
        var where = AdventureNavigationService.DescribeNavigationState(
            source,
            bundle,
            AdventureNavigationIntent.Play);
        return "New play thread started.\n\n"
               + "1. In the pinned Play tab, click New chat in your Project.\n"
               + "2. Click the ChatGPT composer and press Ctrl+V.\n"
               + "3. Press Send.\n\n"
               + $"The start packet is on your clipboard (page: {where}). "
               + "The conversation id will bind after you send.";
    }

    public static string FormatCreateFailure(string? source, AdventureBundle bundle, string? detail)
    {
        var where = AdventureNavigationService.DescribeNavigationState(
            source,
            bundle,
            AdventureNavigationIntent.Play);
        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $"\n\n{detail}";
        return $"Could not prepare a new Project play chat (page: {where}).{suffix}\n\n"
               + "Try: open your linked Project in the Play tab, click New chat, then use "
               + "Start new play thread… again.";
    }
}

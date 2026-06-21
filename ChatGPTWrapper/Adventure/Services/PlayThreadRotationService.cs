using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlayThreadRotationService
{
    /// <summary>
    /// Archives the active play thread and prepares a fresh registry slot while keeping the linked Project.
    /// Ends the current play session and opens a fresh session scope for the next thread.
    /// </summary>
    public static void ReleasePlayThread(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.BeginNewActiveThread(bundle, AdventureThreadKind.Play);

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

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        var previous = string.IsNullOrWhiteSpace(entry.ConversationId)
            ? bundle.Metadata.LinkedConversationId
            : entry.ConversationId;

        if (!string.Equals(previous, conversationId, StringComparison.OrdinalIgnoreCase))
            PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);

        entry.ConversationId = conversationId;
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

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

    public static string FormatThreadStatus(AdventureBundle bundle) =>
        AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Play);

    public static string FormatNarrativeFromSourcesReadyMessage(string? source, AdventureBundle bundle)
    {
        var where = AdventureNavigationService.DescribeNavigationState(
            source,
            bundle,
            AdventureNavigationIntent.Play);
        return "Narrative start ready.\n\n"
               + "1. In the pinned Play tab, click New chat in your Project.\n"
               + "2. Click the ChatGPT composer and press Ctrl+V.\n"
               + "3. Press Send.\n\n"
               + $"The narrative start packet is on your clipboard (page: {where}). "
               + "It uses your source files and adventure JSON only — no prior play summary or transcript. "
               + "The conversation id will bind after you send.";
    }

    public static string FormatStartThreadReadyMessage(string? source, AdventureBundle bundle) =>
        FormatNarrativeFromSourcesReadyMessage(source, bundle);

    public static string FormatHandoffThreadReadyMessage(string? source, AdventureBundle bundle)
    {
        var where = AdventureNavigationService.DescribeNavigationState(
            source,
            bundle,
            AdventureNavigationIntent.Play);
        return "Play handoff ready.\n\n"
               + "1. In the pinned Play tab, click New chat in your Project.\n"
               + "2. Click the ChatGPT composer and press Ctrl+V.\n"
               + "3. Press Send.\n\n"
               + $"The handoff packet is on your clipboard (page: {where}). "
               + "It includes your carry-forward summary and continuation context. "
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
               + $"{PlayThreadRotationCopy.NarrativeFromSourcesButton} or {PlayThreadRotationCopy.HandoffToNewChatButton} again.";
    }
}

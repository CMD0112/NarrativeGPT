using ChatGPTWrapper.Adventure.Models;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlayContextSessionCache
{
    private static readonly Dictionary<Guid, CachedEntry> Entries = new();
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    internal sealed class CachedEntry
    {
        public required string Source { get; init; }

        public string? ConversationId { get; init; }

        public bool ComposerFound { get; init; }

        public DateTimeOffset CachedAt { get; init; }
    }

    public static void Invalidate(Guid adventureId) => Entries.Remove(adventureId);

    public static void Record(Guid adventureId, string? source, string? conversationId, bool composerFound)
    {
        if (string.IsNullOrWhiteSpace(source))
            return;

        Entries[adventureId] = new CachedEntry
        {
            Source = source,
            ConversationId = conversationId,
            ComposerFound = composerFound,
            CachedAt = DateTimeOffset.UtcNow,
        };
    }

    public static bool TryGetFresh(Guid adventureId, out CachedEntry entry)
    {
        if (Entries.TryGetValue(adventureId, out entry!)
            && DateTimeOffset.UtcNow - entry.CachedAt <= MaxAge)
        {
            return true;
        }

        entry = null!;
        return false;
    }

    public static Task<bool> ShouldSkipReensureAsync(
        AdventureBundle bundle,
        CoreWebView2 core,
        AdventureTurnService turnService) =>
        Task.FromResult(ShouldSkipReensureForSource(bundle, core.Source));

    internal static bool ShouldSkipReensureForSource(AdventureBundle bundle, string? source)
    {
        if (string.IsNullOrWhiteSpace(AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)))
            return false;

        if (AdventureNavigationService.IsGenericHomepage(source))
            return false;

        TryBindConversationFromUrl(bundle, source);
        TrySyncConversationFromUrl(bundle, source);

        var conversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (!string.IsNullOrWhiteSpace(conversationId)
            && !string.IsNullOrWhiteSpace(gizmoId)
            && AdventurePlayContextService.IsOnPlayConversationPage(source, conversationId, gizmoId))
        {
            if (!TryGetFresh(bundle.Metadata.Id, out var cached)
                || !string.Equals(cached.Source, source, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(cached.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            {
                Record(bundle.Metadata.Id, source, conversationId, composerFound: true);
            }

            return true;
        }

        if (!PlayThreadBindingService.IsVerified(bundle))
        {
            return AdventureNavigationService.IsOnLinkedProjectPage(source, bundle)
                   && ProjectChatDraftService.IsActive(bundle.Metadata.Id);
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return AdventureNavigationService.IsOnLinkedProjectPage(source, bundle);
        }

        if (string.IsNullOrWhiteSpace(gizmoId)
            || !AdventurePlayContextService.IsOnPlayConversationPage(source, conversationId, gizmoId))
        {
            return false;
        }

        if (!TryGetFresh(bundle.Metadata.Id, out var verifiedCached)
            || !string.Equals(verifiedCached.Source, source, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(verifiedCached.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            Record(bundle.Metadata.Id, source, conversationId, composerFound: true);
        }

        return true;
    }

    /// <summary>
    /// Binds or rotates play thread from the pinned tab URL before packet assembly.
    /// </summary>
    public static bool TrySyncPlayThreadFromSource(AdventureBundle bundle, string? source) =>
        TryBindConversationFromUrl(bundle, source) || TrySyncConversationFromUrl(bundle, source);

    public static bool TryBindConversationFromUrl(AdventureBundle bundle, string? source)
    {
        if (!string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle)))
            return false;

        return TryApplyConversationFromUrl(bundle, source, out _);
    }

    /// <summary>
    /// Updates linked play thread when the pinned tab shows a different project conversation.
    /// </summary>
    public static bool TrySyncConversationFromUrl(AdventureBundle bundle, string? source)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return false;

        if (!TryApplyConversationFromUrl(bundle, source, out var conversationId))
            return false;

        return !string.IsNullOrWhiteSpace(conversationId);
    }

    private static bool TryApplyConversationFromUrl(
        AdventureBundle bundle,
        string? source,
        out string? conversationId)
    {
        conversationId = null;
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var parsed)
            || string.IsNullOrWhiteSpace(parsed))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            if (!AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                    source,
                    bundle.Metadata.LinkedProjectId,
                    out parsed))
            {
                return false;
            }
        }

        if (string.Equals(
                PlayThreadBindingService.GetActiveConversationId(bundle),
                parsed,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        conversationId = parsed;
        PlayThreadBindingService.MarkPendingPin(bundle, parsed);

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && ChatGptUrls.TryParseGizmoId(uri, out var gizmoId)
            && !string.IsNullOrWhiteSpace(gizmoId))
        {
            var normalized = ChatGptUrls.NormalizeGizmoId(gizmoId);
            bundle.Metadata.LinkedProjectId = normalized;
            bundle.Metadata.LinkedProjectHint = normalized;
            bundle.Metadata.ProjectLink = new ProjectLink
            {
                GizmoId = normalized,
                CanonicalUrl = ChatGptUrls.BuildProjectUrl(normalized),
                PlayConversationId = parsed,
                LinkedAt = DateTimeOffset.UtcNow,
            };
        }

        return true;
    }
}

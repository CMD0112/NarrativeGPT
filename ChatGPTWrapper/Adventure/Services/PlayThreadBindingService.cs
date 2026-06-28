using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

internal static class PlayThreadBindingService
{
    public static AdventureThreadEntry? GetActivePlayEntry(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        return AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
    }

    public static PlayThreadBindingTrust GetTrust(AdventureBundle bundle)
    {
        var entry = GetActivePlayEntry(bundle);
        if (entry is null || string.IsNullOrWhiteSpace(entry.ConversationId))
            return PlayThreadBindingTrust.Unbound;

        return entry.BindingTrust;
    }

    public static bool IsVerified(AdventureBundle bundle) =>
        GetTrust(bundle) == PlayThreadBindingTrust.Verified;

    public static bool IsNavigable(AdventureBundle bundle) => IsVerified(bundle);

    /// <summary>
    /// Play thread URL may be opened or restored (verified binding, or user-pinned tab with conversation).
    /// </summary>
    public static bool HasBrowsablePlayTarget(AdventureBundle bundle) =>
        IsVerified(bundle)
        || (HasPinnedTab(bundle) && !string.IsNullOrWhiteSpace(GetActiveConversationId(bundle)));

    public static bool HasPinnedTab(AdventureBundle bundle)
    {
        var entry = GetActivePlayEntry(bundle);
        return !string.IsNullOrWhiteSpace(entry?.PinnedTabKey);
    }

    public static bool HasDurableBinding(AdventureBundle bundle) =>
        HasPinnedTab(bundle)
        || IsVerified(bundle)
        || HasPinnedConversationUrl(bundle);

    /// <summary>
    /// Registry-authoritative play conversation id. Legacy <see cref="AdventureMetadata.LinkedConversationId"/>
    /// is a read fallback only when the active registry entry has no conversation (pre-schema-6 adventures).
    /// </summary>
    public static string? GetActiveConversationId(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        EnsureRegistryBoundFromLegacy(bundle);

        var activeEntry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (activeEntry is not null)
        {
            return string.IsNullOrWhiteSpace(activeEntry.ConversationId)
                ? null
                : activeEntry.ConversationId;
        }

        return null;
    }

    /// <summary>
    /// Promotes in-memory legacy conversation id into the thread registry when migration already ran
    /// but no active play entry exists (common in tests and transitional saves).
    /// </summary>
    internal static void EnsureRegistryBoundFromLegacy(AdventureBundle bundle)
    {
        if (AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play) is not null)
            return;

        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            return;

        AdventureThreadRegistryService.BindActiveConversation(
            bundle,
            AdventureThreadKind.Play,
            bundle.Metadata.LinkedConversationId,
            notifyPlayThreadChanged: false);
    }

    public static bool HasPinnedConversationUrl(AdventureBundle bundle)
    {
        var entry = GetActivePlayEntry(bundle);
        if (HasConversationInUrl(entry?.PinnedTabUrl))
            return true;

        return HasConversationInUrl(bundle.Metadata.PinnedPlayTabUrl);
    }

    private static bool HasConversationInUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && ChatGptUrls.TryParseConversationId(uri, out var conversationId)
        && !string.IsNullOrWhiteSpace(conversationId);

    public static string? ResolveBrowsableUrl(AdventureBundle bundle)
    {
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);

        if (HasBrowsablePlayTarget(bundle))
        {
            var entry = GetActivePlayEntry(bundle);
            if (entry is not null && !string.IsNullOrWhiteSpace(entry.ConversationId))
            {
                if (!string.IsNullOrWhiteSpace(entry.PinnedTabUrl)
                    && Uri.TryCreate(entry.PinnedTabUrl, UriKind.Absolute, out var pinnedUri)
                    && ChatGptUrls.TryParseConversationId(pinnedUri, out var pinnedConv)
                    && string.Equals(pinnedConv, entry.ConversationId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.PinnedTabUrl;
                }

                if (!string.IsNullOrWhiteSpace(gizmoId))
                {
                    return ChatGptUrls.ResolveProjectConversationUrl(
                        entry.ConversationId,
                        gizmoId,
                        entry.PinnedTabUrl);
                }

                return ChatGptUrls.BuildConversationUrl(entry.ConversationId);
            }
        }

        if (!string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectUrl(ChatGptUrls.NormalizeGizmoId(gizmoId));

        return null;
    }

    public static void MarkUnbound(AdventureBundle bundle)
    {
        var entry = GetOrCreatePlayEntry(bundle);
        entry.ConversationId = "";
        entry.BindingTrust = PlayThreadBindingTrust.Unbound;
        entry.RejectedConversationId = null;
        SyncLegacyShim(bundle, entry);
    }

    public static void MarkPendingPin(AdventureBundle bundle, string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var entry = GetOrCreatePlayEntry(bundle);
        if (string.Equals(entry.RejectedConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
            return;

        var previous = entry.ConversationId;
        entry.ConversationId = conversationId;
        entry.BindingTrust = PlayThreadBindingTrust.PendingPin;
        if (!string.Equals(previous, conversationId, StringComparison.OrdinalIgnoreCase))
            PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);

        SyncLegacyShim(bundle, entry);
    }

    public static void MarkVerified(AdventureBundle bundle, string? conversationId = null)
    {
        var entry = GetOrCreatePlayEntry(bundle);
        if (!string.IsNullOrWhiteSpace(conversationId))
        {
            var previous = entry.ConversationId;
            entry.ConversationId = conversationId;
            if (!string.Equals(previous, conversationId, StringComparison.OrdinalIgnoreCase))
                PlayTurnScopeService.OnPlayThreadChanged(bundle, previous, conversationId);
        }

        if (string.IsNullOrWhiteSpace(entry.ConversationId))
            return;

        entry.BindingTrust = PlayThreadBindingTrust.Verified;
        entry.RejectedConversationId = null;
        SyncLegacyShim(bundle, entry);
    }

    public static void MarkRejected(AdventureBundle bundle, string conversationId, string? reason = null)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return;

        var entry = GetOrCreatePlayEntry(bundle);
        entry.RejectedConversationId = conversationId;
        entry.BindingTrust = PlayThreadBindingTrust.Rejected;

        if (string.Equals(entry.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            entry.ConversationId = "";
            PlayTurnScopeService.OnPlayThreadChanged(bundle, conversationId, null);
        }

        ProjectLinkDiagnostics.Log(
            $"Play thread binding rejected conv={conversationId}"
            + (string.IsNullOrWhiteSpace(reason) ? "" : $" reason={reason}"));

        SyncLegacyShim(bundle, entry);
    }

    public static bool IsRejectedConversationId(AdventureBundle bundle, string conversationId)
    {
        var entry = GetActivePlayEntry(bundle);
        return entry is not null
               && !string.IsNullOrWhiteSpace(entry.RejectedConversationId)
               && string.Equals(entry.RejectedConversationId, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fresh adventure with no pin and no verified thread: clear phantom bootstrap ids on play open.
    /// </summary>
    public static bool SanitizeOnPlayOpen(AdventureBundle bundle)
    {
        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
            return false;

        if (!PlayTurnScopeService.IsFreshPlayThread(bundle))
            return false;

        if (HasPinnedTab(bundle))
            return false;

        var trust = GetTrust(bundle);
        if (trust is PlayThreadBindingTrust.Verified)
            return false;

        var hadBinding = !string.IsNullOrWhiteSpace(GetActiveConversationId(bundle))
                         || trust is PlayThreadBindingTrust.PendingPin or PlayThreadBindingTrust.Rejected;

        if (!hadBinding)
            return false;

        ProjectLinkDiagnostics.Log(
            $"SanitizeOnPlayOpen: clearing unverified play binding for adventure {bundle.Metadata.Id} trust={trust}");
        PlayThreadRotationService.ReleasePlayThread(bundle);
        PlayThreadRotationService.PersistRelease(bundle);
        PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
        return true;
    }

    public static async Task<bool> TryPromoteVerifiedFromPageAsync(
        AdventureBundle bundle,
        CoreWebView2 core,
        AdventureTurnService? turnService,
        CancellationToken cancellationToken = default)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return false;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var conversationId = GetActiveConversationId(bundle);
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            if (!AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                    core.Source,
                    gizmoId,
                    out conversationId)
                && !TryParseConversationFromSource(core.Source, out conversationId))
            {
                return false;
            }

            MarkPendingPin(bundle, conversationId);
        }

        if (!AdventurePlayContextService.IsOnPlayConversationPage(core, conversationId, gizmoId))
            return false;

        if (turnService is not null)
        {
            var health = await turnService.GetHealthAsync(core);
            if (!health.ComposerFound)
                return false;
        }

        MarkVerified(bundle, conversationId);
        return true;
    }

    private static bool TryParseConversationFromSource(string? source, out string conversationId)
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

    private static AdventureThreadEntry GetOrCreatePlayEntry(AdventureBundle bundle) =>
        AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);

    private static void SyncLegacyShim(AdventureBundle bundle, AdventureThreadEntry entry)
    {
        if (bundle.Metadata.SchemaVersion >= 6)
            return;

        bundle.Metadata.LinkedConversationId = string.IsNullOrWhiteSpace(entry.ConversationId)
            ? null
            : entry.ConversationId;
        if (bundle.Metadata.ProjectLink is not null)
            bundle.Metadata.ProjectLink.PlayConversationId = bundle.Metadata.LinkedConversationId;
    }
}

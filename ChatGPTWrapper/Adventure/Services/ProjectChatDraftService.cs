using System.Collections.Concurrent;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Wpf;
using System.Windows.Controls;

namespace ChatGPTWrapper.Adventure.Services;

internal enum ProjectChatDraftKind
{
    Play,
    Design,
    Utility,
}

internal sealed class ProjectChatDraftSnapshot
{
    public required Guid AdventureId { get; init; }

    public required ProjectChatDraftKind Kind { get; init; }

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;

    public string? PriorLinkedConversationId { get; init; }

    public string? PriorPinnedPlayTabKey { get; init; }

    public string? PriorPinnedPlayTabTitle { get; init; }

    public string? PriorPinnedPlayTabUrl { get; init; }

    public string? PriorProjectLinkPlayConversationId { get; init; }

    public string? PriorDesignConversationId { get; init; }

    public string? PriorPinnedDesignTabKey { get; init; }

    public string? PriorPinnedDesignTabTitle { get; init; }

    public string? PriorPinnedDesignTabUrl { get; init; }

    public Dictionary<string, Guid>? PriorActiveThreadIds { get; init; }

    public string? DraftTabKey { get; set; }

    /// <summary>True when <see cref="TryAutoBeginOnProjectPage"/> entered draft without explicit user action.</summary>
    public bool AutoEntered { get; init; }
}

/// <summary>
/// Suspends auto-navigation to stored play/design threads while the author drafts a new
/// Project-level chat (New chat on the linked Project page).
/// </summary>
internal static class ProjectChatDraftService
{
    private static readonly ConcurrentDictionary<Guid, ProjectChatDraftSnapshot> Active =
        new();

    public static bool IsActive(Guid adventureId) => Active.ContainsKey(adventureId);

    public static bool HasActivePlayDraft() =>
        Active.Values.Any(s => s.Kind == ProjectChatDraftKind.Play);

    public static bool IsActive(AdventureBundle bundle) => IsActive(bundle.Metadata.Id);

    public static ProjectChatDraftKind? GetActiveKind(Guid adventureId) =>
        Active.TryGetValue(adventureId, out var snapshot) ? snapshot.Kind : null;

    public static bool ShouldStayOnProjectPage(AdventureBundle bundle, string? source)
    {
        if (!IsActive(bundle.Metadata.Id))
            return false;

        return AdventureNavigationService.IsOnLinkedProjectPage(source, bundle);
    }

    public static bool ShouldSuppressPlayTabSelection(AdventureBundle bundle) =>
        IsActive(bundle.Metadata.Id);

    public static void NoteDraftTab(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        var tabKey = PlayTabPinService.GetTabKey(webView, tabs);
        if (string.IsNullOrWhiteSpace(tabKey))
            return;

        if (!Active.TryGetValue(bundle.Metadata.Id, out var snapshot))
            return;

        snapshot.DraftTabKey = tabKey;
    }

    public static bool IsDraftTab(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        if (!Active.TryGetValue(bundle.Metadata.Id, out var snapshot)
            || string.IsNullOrWhiteSpace(snapshot.DraftTabKey))
        {
            return false;
        }

        var tabKey = PlayTabPinService.GetTabKey(webView, tabs);
        return tabKey is not null
               && string.Equals(tabKey, snapshot.DraftTabKey, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Utility/design drafting must not attach play compose hooks or play send warmup.
    /// Play rotation draft keeps compose on the tab being rotated.
    /// </summary>
    public static bool ShouldSuppressPlayAutomation(
        AdventureBundle? bundle,
        WebView2? webView,
        TabControl? tabs,
        string? source = null)
    {
        if (bundle is null)
            return false;

        var ctx = PlayTabCapabilityContext.From(bundle, webView, tabs, source);
        return PlayTabCapabilityResolver.Resolve(ctx).LegacySuppressPlayAutomation;
    }

    public static bool IsValidDraftTarget(AdventureBundle bundle, string? source, AdventureNavigationIntent intent)
    {
        if (!IsActive(bundle.Metadata.Id))
            return false;

        if (AdventureNavigationService.IsOnLinkedProjectPage(source, bundle))
            return true;

        if (intent == AdventureNavigationIntent.Design
            && DesignTabPinService.IsOnDesignTarget(source, bundle))
        {
            return true;
        }

        return false;
    }

    public static string FormatStatusLine(AdventureBundle bundle)
    {
        if (!Active.TryGetValue(bundle.Metadata.Id, out var snapshot))
            return "";

        var kind = snapshot.Kind switch
        {
            ProjectChatDraftKind.Play => "play",
            ProjectChatDraftKind.Design => "design",
            ProjectChatDraftKind.Utility => "utility",
            _ => "project",
        };

        return $"Drafting new {kind} chat on Project page — redirect paused. ChatGPT composer sends plain messages here; use the adventure panel composer on your play thread for injected packets.";
    }

    private static Dictionary<string, Guid>? CopyActiveThreadIds(AdventureMetadata metadata)
    {
        AdventureThreadRegistryService.EnsureMigrated(new AdventureBundle { Metadata = metadata });
        return metadata.ActiveThreadIds?.Count > 0
            ? new Dictionary<string, Guid>(metadata.ActiveThreadIds, StringComparer.OrdinalIgnoreCase)
            : null;
    }

    public static void BeginPlayDraft(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var metadata = bundle.Metadata;
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var playEntry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
        Active[bundle.Metadata.Id] = new ProjectChatDraftSnapshot
        {
            AdventureId = metadata.Id,
            Kind = ProjectChatDraftKind.Play,
            PriorLinkedConversationId = PlayThreadBindingService.GetActiveConversationId(bundle)
                                      ?? metadata.LinkedConversationId,
            PriorPinnedPlayTabKey = playEntry?.PinnedTabKey ?? metadata.PinnedPlayTabKey,
            PriorPinnedPlayTabTitle = playEntry?.PinnedTabTitle ?? metadata.PinnedPlayTabTitle,
            PriorPinnedPlayTabUrl = playEntry?.PinnedTabUrl ?? metadata.PinnedPlayTabUrl,
            PriorProjectLinkPlayConversationId = metadata.ProjectLink?.PlayConversationId,
            PriorActiveThreadIds = CopyActiveThreadIds(metadata),
        };
    }

    public static void BeginDesignDraft(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var metadata = bundle.Metadata;
        Active[bundle.Metadata.Id] = new ProjectChatDraftSnapshot
        {
            AdventureId = metadata.Id,
            Kind = ProjectChatDraftKind.Design,
            PriorDesignConversationId = AdventureDesignContextService.GetDesignConversationId(bundle),
            PriorPinnedDesignTabKey = metadata.PinnedDesignTabKey,
            PriorPinnedDesignTabTitle = metadata.PinnedDesignTabTitle,
            PriorPinnedDesignTabUrl = metadata.PinnedDesignTabUrl,
            PriorActiveThreadIds = CopyActiveThreadIds(metadata),
        };
    }

    public static void BeginUtilityDraft(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (IsActive(bundle.Metadata.Id))
            return;

        var metadata = bundle.Metadata;
        Active[bundle.Metadata.Id] = new ProjectChatDraftSnapshot
        {
            AdventureId = metadata.Id,
            Kind = ProjectChatDraftKind.Utility,
            PriorLinkedConversationId = metadata.LinkedConversationId,
            PriorPinnedPlayTabKey = metadata.PinnedPlayTabKey,
            PriorPinnedPlayTabTitle = metadata.PinnedPlayTabTitle,
            PriorPinnedPlayTabUrl = metadata.PinnedPlayTabUrl,
            PriorProjectLinkPlayConversationId = metadata.ProjectLink?.PlayConversationId,
            PriorDesignConversationId = AdventureDesignContextService.GetDesignConversationId(bundle),
            PriorPinnedDesignTabKey = metadata.PinnedDesignTabKey,
            PriorPinnedDesignTabTitle = metadata.PinnedDesignTabTitle,
            PriorPinnedDesignTabUrl = metadata.PinnedDesignTabUrl,
            PriorActiveThreadIds = CopyActiveThreadIds(metadata),
        };
    }

    public static void BeginDraftOnProjectPage(AdventureBundle bundle) =>
        BeginUtilityDraft(bundle);

    /// <summary>
    /// When a stored play/design thread exists, landing on the Project page should pause
    /// auto-redirect so the author can New chat or compose without being pulled to the pin.
    /// </summary>
    public static bool TryCancelAutoEnteredDraft(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!Active.TryGetValue(bundle.Metadata.Id, out var snapshot) || !snapshot.AutoEntered)
            return false;

        Cancel(bundle);
        return true;
    }

    public static bool TryAutoBeginOnProjectPage(
        AdventureBundle bundle,
        string? source,
        WebView2? webView = null,
        TabControl? tabs = null,
        bool playModeActive = false)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        if (playModeActive)
            return false;

        if (IsActive(bundle.Metadata.Id))
            return false;

        if (!AdventureProjectBindingService.HasLinkedProject(bundle))
            return false;

        if (!AdventureNavigationService.IsOnLinkedProjectPage(source, bundle))
            return false;

        var hasStoredPlay = !string.IsNullOrWhiteSpace(PlayThreadBindingService.GetActiveConversationId(bundle));
        var hasStoredDesign = !string.IsNullOrWhiteSpace(
            AdventureDesignContextService.GetDesignConversationId(bundle));
        if (!hasStoredPlay && !hasStoredDesign)
            return false;

        var metadata = bundle.Metadata;
        Active[bundle.Metadata.Id] = new ProjectChatDraftSnapshot
        {
            AdventureId = metadata.Id,
            Kind = ProjectChatDraftKind.Utility,
            AutoEntered = true,
            PriorLinkedConversationId = metadata.LinkedConversationId,
            PriorPinnedPlayTabKey = metadata.PinnedPlayTabKey,
            PriorPinnedPlayTabTitle = metadata.PinnedPlayTabTitle,
            PriorPinnedPlayTabUrl = metadata.PinnedPlayTabUrl,
            PriorProjectLinkPlayConversationId = metadata.ProjectLink?.PlayConversationId,
            PriorDesignConversationId = AdventureDesignContextService.GetDesignConversationId(bundle),
            PriorPinnedDesignTabKey = metadata.PinnedDesignTabKey,
            PriorPinnedDesignTabTitle = metadata.PinnedDesignTabTitle,
            PriorPinnedDesignTabUrl = metadata.PinnedDesignTabUrl,
            PriorActiveThreadIds = CopyActiveThreadIds(metadata),
        };

        if (webView is not null && tabs is not null)
            NoteDraftTab(bundle, webView, tabs);

        ProjectLinkDiagnostics.Log(
            $"Auto-entered project chat draft for adventure {bundle.Metadata.Id} "
            + $"(playThread={hasStoredPlay} designThread={hasStoredDesign})");

        return true;
    }

    public static void Complete(AdventureBundle bundle)
    {
        Active.TryRemove(bundle.Metadata.Id, out _);
    }

    public static void Cancel(AdventureBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!Active.TryRemove(bundle.Metadata.Id, out var snapshot))
            return;

        RestoreSnapshot(bundle, snapshot);
        AdventureStore.Save(bundle, allowLinkMetadataOverwrite: true);
        PlayContextSessionCache.Invalidate(bundle.Metadata.Id);
    }

    private static void RestoreSnapshot(AdventureBundle bundle, ProjectChatDraftSnapshot snapshot)
    {
        var metadata = bundle.Metadata;

        switch (snapshot.Kind)
        {
            case ProjectChatDraftKind.Play:
                metadata.LinkedConversationId = snapshot.PriorLinkedConversationId;
                metadata.PinnedPlayTabKey = snapshot.PriorPinnedPlayTabKey;
                metadata.PinnedPlayTabTitle = snapshot.PriorPinnedPlayTabTitle;
                metadata.PinnedPlayTabUrl = snapshot.PriorPinnedPlayTabUrl;
                if (metadata.ProjectLink is not null)
                    metadata.ProjectLink.PlayConversationId = snapshot.PriorProjectLinkPlayConversationId;
                if (!string.IsNullOrWhiteSpace(snapshot.PriorLinkedConversationId))
                    PlayThreadBindingService.MarkVerified(bundle, snapshot.PriorLinkedConversationId);
                else
                    PlayThreadBindingService.MarkUnbound(bundle);

                var playEntry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
                playEntry.PinnedTabKey = snapshot.PriorPinnedPlayTabKey;
                playEntry.PinnedTabTitle = snapshot.PriorPinnedPlayTabTitle;
                playEntry.PinnedTabUrl = snapshot.PriorPinnedPlayTabUrl;
                break;

            case ProjectChatDraftKind.Design:
                metadata.PinnedDesignTabKey = snapshot.PriorPinnedDesignTabKey;
                metadata.PinnedDesignTabTitle = snapshot.PriorPinnedDesignTabTitle;
                metadata.PinnedDesignTabUrl = snapshot.PriorPinnedDesignTabUrl;
                if (!string.IsNullOrWhiteSpace(snapshot.PriorDesignConversationId))
                {
                    metadata.UtilitySessions[GenerationJobId.DesignAdventure] = new GenerationUtilitySession
                    {
                        ConversationId = snapshot.PriorDesignConversationId,
                        Sequence = 1,
                        CreatedAt = DateTimeOffset.UtcNow,
                        LastUsedAt = DateTimeOffset.UtcNow,
                    };
                }
                else
                {
                    metadata.UtilitySessions.Remove(GenerationJobId.DesignAdventure);
                }

                break;

            case ProjectChatDraftKind.Utility:
                break;
        }

        if (snapshot.PriorActiveThreadIds is { Count: > 0 })
        {
            metadata.ActiveThreadIds = new Dictionary<string, Guid>(
                snapshot.PriorActiveThreadIds,
                StringComparer.OrdinalIgnoreCase);
        }
    }
}

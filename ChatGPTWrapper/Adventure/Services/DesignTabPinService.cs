using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services;

internal static class DesignTabPinService
{
    public const string DesignPinRequiredError =
        "design_pin_required: Open your linked Project, create a New chat, then Pin design tab";

    public static bool PreferPinnedDesignWebView(AdventureBundle? bundle)
    {
        if (bundle is null)
            return false;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design);
        return !string.IsNullOrWhiteSpace(entry?.PinnedTabKey)
               || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabKey);
    }

    public static bool HasDesignPin(AdventureBundle? bundle) =>
        bundle is not null
        && PreferPinnedDesignWebView(bundle)
        && !string.IsNullOrWhiteSpace(GetDesignConversationId(bundle));

    public static bool HasPersistedDesignSession(AdventureBundle? bundle) =>
        bundle is not null
        && (!string.IsNullOrWhiteSpace(AdventureDesignContextService.GetDesignConversationId(bundle))
            || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabUrl)
            || !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabKey));

    public static bool IsTrustedPinnedDesignConversation(
        AdventureMetadata metadata,
        string conversationId)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
            return false;

        if (!string.IsNullOrWhiteSpace(metadata.PinnedDesignTabUrl)
            && metadata.PinnedDesignTabUrl.Contains(conversationId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var bundle = new AdventureBundle { Metadata = metadata };
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var registryId = AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Design);
        return string.Equals(registryId, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetDesignConversationId(AdventureBundle bundle) =>
        AdventureDesignContextService.GetDesignConversationId(bundle);

    public static string? GetDesignTargetUrl(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var fromRegistry = AdventureThreadRegistryService.GetTargetUrl(bundle, AdventureThreadKind.Design);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
            return fromRegistry;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        var conversationId = GetDesignConversationId(bundle);

        if (!string.IsNullOrWhiteSpace(conversationId) && !string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectConversationUrl(conversationId, gizmoId);

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabUrl)
            && ChatGptUrls.TryCreateTrustedNavigationUri(bundle.Metadata.PinnedDesignTabUrl, out _))
        {
            return bundle.Metadata.PinnedDesignTabUrl;
        }

        return null;
    }

    public static string? GetDesignBrowseUrl(AdventureBundle bundle)
    {
        var target = GetDesignTargetUrl(bundle);
        if (!string.IsNullOrWhiteSpace(target))
            return target;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        return ChatGptUrls.BuildProjectUrl(ChatGptUrls.NormalizeGizmoId(gizmoId));
    }

    public static GenerationUtilitySession? TryResolveDesignSessionFromPin(AdventureBundle bundle)
    {
        var conversationId = GetDesignConversationId(bundle);
        var pinnedUrl = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)?.PinnedTabUrl
                        ?? bundle.Metadata.PinnedDesignTabUrl;
        if (string.IsNullOrWhiteSpace(conversationId)
            && !string.IsNullOrWhiteSpace(pinnedUrl)
            && Uri.TryCreate(pinnedUrl, UriKind.Absolute, out var uri)
            && ChatGptUrls.TryParseConversationId(uri, out var fromUrl))
        {
            conversationId = fromUrl;
        }

        if (string.IsNullOrWhiteSpace(conversationId))
            return null;

        var jobId = GenerationJobId.DesignAdventure;
        var existing = GenerationUtilitySessionService.GetSession(bundle.Metadata, jobId);
        if (existing is not null
            && string.Equals(existing.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return new GenerationUtilitySession
        {
            ConversationId = conversationId,
            Sequence = GenerationUtilitySessionService.GetNextSequence(bundle.Metadata, jobId),
            SeedVersion = GenerationUtilitySessionService.GetSeedVersion(bundle, jobId),
            CreatedAt = DateTimeOffset.UtcNow,
            LastUsedAt = DateTimeOffset.UtcNow,
        };
    }

    public static bool PruneUnverifiedDesignSession(
        AdventureBundle bundle,
        IReadOnlyList<GizmoConversationRef> conversations)
    {
        var jobId = GenerationJobId.DesignAdventure;
        var session = GenerationUtilitySessionService.GetSession(bundle.Metadata, jobId);
        if (session is null || conversations.Count == 0)
            return false;

        if (conversations.Any(c =>
                string.Equals(c.Id, session.ConversationId, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabUrl)
            && bundle.Metadata.PinnedDesignTabUrl.Contains(
                session.ConversationId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        GenerationUtilitySessionService.ArchiveSession(bundle.Metadata, jobId, session, "not_in_project");
        AdventureStore.Save(bundle);
        return true;
    }

    public static WebView2? TryFindWebViewForDesignSession(TabControl tabs, AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var pinKey = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)?.PinnedTabKey
                       ?? bundle.Metadata.PinnedDesignTabKey;

        if (PlayTabPinService.FindWebViewByPinKey(tabs, pinKey) is { } pinned)
            return pinned;

        return TryFindWebViewOnDesignTarget(tabs, bundle);
    }

    /// <summary>
    /// Finds any tab on a valid design conversation (not the play thread), even when unpinned.
    /// </summary>
    public static WebView2? TryFindWebViewOnEligibleDesignConversation(TabControl tabs, AdventureBundle bundle)
    {
        if (TryFindWebViewForDesignSession(tabs, bundle) is { } pinnedOrTarget)
            return pinnedOrTarget;

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            if (wv.CoreWebView2?.Source is not { } source)
                continue;

            if (TryResolveDesignConversationFromSource(bundle, source, out _, out _))
                return wv;
        }

        return null;
    }

    public static WebView2? TryFindWebViewOnDesignTarget(TabControl tabs, AdventureBundle bundle)
    {
        var targetConversationId = GetDesignConversationId(bundle);
        if (targetConversationId is null)
            return null;

        foreach (var item in tabs.Items)
        {
            if (item is not TabItem { Content: WebView2 wv })
                continue;

            if (wv.CoreWebView2?.Source is not { } source)
                continue;

            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.TryParseConversationId(uri, out var sourceConversationId))
            {
                continue;
            }

            if (string.Equals(sourceConversationId, targetConversationId, StringComparison.OrdinalIgnoreCase))
                return wv;
        }

        return null;
    }

    public static bool IsOnDesignTarget(string? source, AdventureBundle bundle)
    {
        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var targetConversationId = GetDesignConversationId(bundle);
        if (targetConversationId is null)
            return false;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.TryParseConversationId(uri, out var sourceConversationId))
        {
            return false;
        }

        if (!string.Equals(sourceConversationId, targetConversationId, StringComparison.OrdinalIgnoreCase))
            return false;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return true;

        return AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                   source,
                   gizmoId,
                   out _)
               || ChatGptUrls.TryParseGizmoId(uri, out _);
    }

    public static void PinDesignTab(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)
                      ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design);
        PinDesignTabToEntry(bundle, entry.Id, webView, tabs, setActive: true);
    }

    public static void PinDesignTabToEntry(
        AdventureBundle bundle,
        Guid entryId,
        WebView2 webView,
        TabControl tabs,
        bool setActive = true)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetEntry(bundle, entryId)
                    ?? throw new InvalidOperationException("Thread entry not found.");

        if (entry.Kind != AdventureThreadKind.Design)
            throw new InvalidOperationException("Entry is not a design thread.");

        if (entry.Status == AdventureThreadStatus.Archived)
            throw new InvalidOperationException("Cannot pin an archived thread.");

        var source = webView.CoreWebView2?.Source;
        if (!TryResolveDesignConversationFromSource(bundle, source, out var conversationId, out var error))
        {
            throw new InvalidOperationException(error switch
            {
                "design_tab_not_on_conversation" =>
                    "Open a Project conversation (/c/…) in this tab, then pin it as the design tab.",
                "design_same_as_play_thread" =>
                    "Design thread cannot be the play thread — create a New chat in the Project.",
                _ => "Could not pin this tab for design. Open a Project conversation page first.",
            });
        }

        if (!string.IsNullOrWhiteSpace(conversationId))
            entry.ConversationId = conversationId;

        AdventureThreadRegistryService.UpdatePinFromWebView(bundle, entry.Id, webView, tabs, source);
        if (setActive)
            AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);

        if (ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id) == ProjectChatDraftKind.Design)
            ProjectChatDraftService.Complete(bundle);

        AdventureThreadRegistryService.SyncActiveDesignUtilitySession(bundle);
        AdventureStore.Save(bundle);
    }

    public static bool TryResolveDesignConversationFromSource(
        AdventureBundle bundle,
        string? source,
        out string? conversationId,
        out string? error)
    {
        conversationId = null;
        error = null;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            error = "design_no_project";
            return false;
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        string? resolved = null;
        if (AdventurePlayContextService.TryGetLinkedProjectConversationFromUrl(
                source,
                gizmoId,
                out var fromProject))
        {
            resolved = fromProject;
        }
        else if (Uri.TryCreate(source, UriKind.Absolute, out var uri)
                 && ChatGptUrls.TryParseConversationId(uri, out var fromUri)
                 && !string.IsNullOrWhiteSpace(fromUri))
        {
            resolved = fromUri;
        }

        if (string.IsNullOrWhiteSpace(resolved))
        {
            error = "design_tab_not_on_conversation";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId)
            && string.Equals(
                resolved,
                bundle.Metadata.LinkedConversationId,
                StringComparison.OrdinalIgnoreCase))
        {
            error = "design_same_as_play_thread";
            return false;
        }

        var playConversation = AdventureThreadRegistryService.GetActiveConversationId(bundle, AdventureThreadKind.Play);
        if (!string.IsNullOrWhiteSpace(playConversation)
            && string.Equals(resolved, playConversation, StringComparison.OrdinalIgnoreCase))
        {
            error = "design_same_as_play_thread";
            return false;
        }

        conversationId = resolved;
        return true;
    }

    public static void ClearDesignPin(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        if (AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design) is { } entry)
            AdventureThreadRegistryService.ClearEntryPin(bundle, entry.Id);

        AdventureThreadRegistryService.ClearLegacyDesignBindingFields(bundle.Metadata);
        AdventureStore.Save(bundle);
    }

    public static bool IsSameTabAsDesignPin(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var pinKey = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)?.PinnedTabKey
                       ?? bundle.Metadata.PinnedDesignTabKey;
        if (string.IsNullOrWhiteSpace(pinKey))
            return false;

        var key = PlayTabPinService.GetTabKey(webView, tabs);
        return key is not null
               && string.Equals(key, pinKey, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatDesignThreadStatus(AdventureBundle bundle) =>
        AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Design);

    public static string? FormatDesignDraftBanner(AdventureBundle bundle) =>
        string.IsNullOrWhiteSpace(ProjectChatDraftService.FormatStatusLine(bundle))
            ? null
            : ProjectChatDraftService.FormatStatusLine(bundle);

    /// <summary>
    /// Re-binds the design thread pin after navigation (e.g. app restart) without surfacing errors.
    /// </summary>
    public static bool TryRestorePinFromWebView(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        var source = webView.CoreWebView2?.Source;
        if (!TryResolveDesignConversationFromSource(bundle, source, out var conversationId, out _))
            return false;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Design)
                      ?? AdventureThreadRegistryService.RegisterEntry(bundle, AdventureThreadKind.Design);

        if (!string.IsNullOrWhiteSpace(conversationId))
            entry.ConversationId = conversationId;

        AdventureThreadRegistryService.UpdatePinFromWebView(bundle, entry.Id, webView, tabs, source);
        AdventureThreadRegistryService.SetActivePin(bundle, entry.Id, notifyPlayThreadChanged: false);
        AdventureStore.Save(bundle);
        return true;
    }
}

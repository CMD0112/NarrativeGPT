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

    public static bool PreferPinnedDesignWebView(AdventureBundle? bundle) =>
        !string.IsNullOrWhiteSpace(bundle?.Metadata.PinnedDesignTabKey);

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

        return !string.IsNullOrWhiteSpace(metadata.PinnedDesignTabKey)
               && metadata.UtilitySessions is not null
               && metadata.UtilitySessions.TryGetValue(GenerationJobId.DesignAdventure, out var session)
               && string.Equals(session.ConversationId, conversationId, StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetDesignConversationId(AdventureBundle bundle) =>
        AdventureDesignContextService.GetDesignConversationId(bundle);

    public static string? GetDesignTargetUrl(AdventureBundle bundle)
    {
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
        if (string.IsNullOrWhiteSpace(conversationId)
            && !string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabUrl)
            && Uri.TryCreate(bundle.Metadata.PinnedDesignTabUrl, UriKind.Absolute, out var uri)
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
        if (PlayTabPinService.FindWebViewByPinKey(tabs, bundle.Metadata.PinnedDesignTabKey) is { } pinned)
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
        var key = PlayTabPinService.GetTabKey(webView, tabs)
                  ?? throw new InvalidOperationException("Could not resolve tab key for WebView.");

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
        {
            var utilityJobId = GenerationJobId.DesignAdventure;
            bundle.Metadata.UtilitySessions ??= new Dictionary<string, GenerationUtilitySession>(StringComparer.OrdinalIgnoreCase);
            if (bundle.Metadata.UtilitySessions.TryGetValue(utilityJobId, out var existing))
            {
                existing.ConversationId = conversationId;
                existing.LastUsedAt = DateTimeOffset.UtcNow;
            }
            else
            {
                bundle.Metadata.UtilitySessions[utilityJobId] = new GenerationUtilitySession
                {
                    ConversationId = conversationId,
                    Sequence = GenerationUtilitySessionService.GetNextSequence(bundle.Metadata, utilityJobId),
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastUsedAt = DateTimeOffset.UtcNow,
                };
            }
        }

        bundle.Metadata.PinnedDesignTabKey = key;
        bundle.Metadata.PinnedDesignTabTitle = PlayTabPinService.GetTabTitle(webView, tabs);
        bundle.Metadata.PinnedDesignTabUrl = source;
        if (ProjectChatDraftService.GetActiveKind(bundle.Metadata.Id) == ProjectChatDraftKind.Design)
            ProjectChatDraftService.Complete(bundle);
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

        conversationId = resolved;
        return true;
    }

    public static void ClearDesignPin(AdventureBundle bundle)
    {
        bundle.Metadata.PinnedDesignTabKey = null;
        bundle.Metadata.PinnedDesignTabTitle = null;
        bundle.Metadata.PinnedDesignTabUrl = null;
        AdventureStore.Save(bundle);
    }

    public static bool IsSameTabAsDesignPin(AdventureBundle bundle, WebView2 webView, TabControl tabs)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabKey))
            return false;

        var key = PlayTabPinService.GetTabKey(webView, tabs);
        return key is not null
               && string.Equals(key, bundle.Metadata.PinnedDesignTabKey, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatDesignThreadStatus(AdventureBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId)
            && !AdventureProjectBindingService.HasLinkedProject(bundle))
            return "No Project linked — link a Project to use the design thread.";

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);

        var conv = GetDesignConversationId(bundle);
        if (!string.IsNullOrWhiteSpace(conv))
            return $"Project linked · Design thread: c/{conv}";

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.PinnedDesignTabKey))
            return "Project linked · Design tab pinned — open the tab and confirm it is on a Project chat.";

        return "Project linked · Open Project → New chat → Pin design tab";
    }
}

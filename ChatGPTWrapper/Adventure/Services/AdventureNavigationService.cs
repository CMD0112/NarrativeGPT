using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal enum AdventureNavigationIntent
{
    Play,
    Design,
}

internal static class AdventureNavigationService
{
    public static void SyncLinkedFields(AdventureBundle? bundle)
    {
        if (bundle is null)
            return;

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
    }

    public static bool HasLinkedProject(AdventureBundle bundle) =>
        AdventureProjectBindingService.HasLinkedProject(bundle);

    public static string? ResolvePlayBrowseUrl(AdventureBundle bundle)
    {
        SyncLinkedFields(bundle);
        var target = PlayTabPinService.GetPlayTargetUrl(bundle);
        if (!string.IsNullOrWhiteSpace(target))
            return target;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (!string.IsNullOrWhiteSpace(gizmoId))
            return ChatGptUrls.BuildProjectUrl(ChatGptUrls.NormalizeGizmoId(gizmoId));

        if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            return ChatGptUrls.BuildConversationUrl(bundle.Metadata.LinkedConversationId);

        return null;
    }

    public static string? ResolveDesignBrowseUrl(AdventureBundle bundle, bool preferThread = true)
    {
        SyncLinkedFields(bundle);
        if (preferThread)
        {
            var thread = DesignTabPinService.GetDesignTargetUrl(bundle);
            if (!string.IsNullOrWhiteSpace(thread))
                return thread;
        }

        return DesignTabPinService.GetDesignBrowseUrl(bundle);
    }

    /// <summary>
    /// Trusted navigation target when the WebView is on an unrelated page. Never returns bare homepage when a project is linked.
    /// </summary>
    public static string ResolveTrustedFallbackUrl(AdventureBundle bundle)
    {
        var play = ResolvePlayBrowseUrl(bundle);
        if (!string.IsNullOrWhiteSpace(play))
            return play;

        var design = ResolveDesignBrowseUrl(bundle);
        if (!string.IsNullOrWhiteSpace(design))
            return design;

        return "https://chatgpt.com";
    }

    public static bool IsGenericHomepage(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            return true;

        if (!ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return false;

        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length > 0)
            return false;

        // Root URL with ?project=g-… is a project workspace entry, not an untargeted homepage.
        return !ChatGptUrls.TryParseGizmoId(uri, out _);
    }

    public static bool IsOnLinkedProjectPage(string? source, AdventureBundle bundle)
    {
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId)
            || !Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return false;
        }

        return ChatGptUrls.TryParseGizmoId(uri, out var parsed)
               && ChatGptUrls.GizmoIdsEqual(parsed, gizmoId);
    }

    public static bool ShouldNavigateToPlayTarget(string? source, AdventureBundle bundle, string targetUrl) =>
        PlaySessionNavigationService.ShouldNavigateToBrowseTarget(source, bundle, targetUrl);

    public static bool ShouldNavigateToDesignTarget(string? source, AdventureBundle bundle, string targetUrl)
    {
        SyncLinkedFields(bundle);

        if (DesignTabPinService.IsOnDesignTarget(source, bundle))
            return false;

        if (IsGenericHomepage(source))
            return true;

        if (IsOnLinkedProjectPage(source, bundle))
            return false;

        return !string.Equals(source, targetUrl, StringComparison.OrdinalIgnoreCase);
    }

    public static string FormatPlaySessionError(PlayContextResult? result)
    {
        if (result is null)
            return "Play session could not be prepared.";

        return result.Status switch
        {
            PlayContextStatus.Legacy => "No linked Project — using legacy ChatGPT thread.",
            PlayContextStatus.Ready => "Play thread ready.",
            PlayContextStatus.MissingProject => result.Error ?? "Link a ChatGPT Project first.",
            PlayContextStatus.NoConversation =>
                result.Error ?? "Could not find or create a play thread in the linked Project.",
            PlayContextStatus.NavigationFailed =>
                (result.Error ?? "Navigation to the play thread failed.")
                + " Open Play settings → Session to pin the correct tab, or retry Play.",
            _ => result.Error ?? result.Status.ToString(),
        };
    }

    public static string FormatDesignSessionError(DesignContextResult? result)
    {
        if (result is null)
            return "Design browser could not be prepared.";

        return result.Status switch
        {
            DesignContextStatus.Ready => "Design thread ready.",
            DesignContextStatus.MissingProject => result.Error ?? "Link a ChatGPT Project first.",
            DesignContextStatus.NoConversation =>
                result.Error ?? DesignTabPinService.DesignPinRequiredError,
            DesignContextStatus.NavigationFailed =>
                (result.Error ?? "Navigation to the design thread failed.")
                + " Open your Project → New chat → Pin design tab.",
            _ => result.Error ?? result.Status.ToString(),
        };
    }

    /// <summary>
    /// True when a linked adventure WebView landed on the bare ChatGPT homepage and should be steered back.
    /// </summary>
    public static bool RequiresHomepageRecovery(string? source, AdventureBundle bundle) =>
        RequiresNavigationRecovery(source, bundle);

    /// <summary>
    /// URL-level signals that the WebView left the linked adventure context (homepage, wrong project entry, etc.).
    /// </summary>
    public static bool RequiresNavigationRecovery(string? source, AdventureBundle bundle)
    {
        if (!HasLinkedProject(bundle))
            return false;

        if (IsGenericHomepage(source))
            return true;

        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri)
            || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
        {
            return true;
        }

        // Root URL with a different project's ?project= query — common after auth/session loss.
        if (uri.AbsolutePath.TrimEnd('/').Length == 0
            && ChatGptUrls.TryParseGizmoId(uri, out var parsed)
            && !ChatGptUrls.GizmoIdsEqual(
                parsed,
                AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata)!))
        {
            return true;
        }

        return false;
    }

    public static bool IsOnValidAdventureWebTarget(
        string? source,
        AdventureBundle bundle,
        AdventureNavigationIntent intent)
    {
        SyncLinkedFields(bundle);

        if (intent == AdventureNavigationIntent.Play)
        {
            if (PlayTabPinService.IsOnPlayTarget(source, bundle))
                return true;

            if (ProjectChatDraftService.IsValidDraftTarget(bundle, source, intent))
                return true;

            if (IsOnLinkedProjectPage(source, bundle))
                return true;

            if (!string.IsNullOrWhiteSpace(bundle.Metadata.LinkedConversationId))
            {
                var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
                return !string.IsNullOrWhiteSpace(gizmoId)
                       && AdventurePlayContextService.IsOnPlayConversationPage(
                           source,
                           bundle.Metadata.LinkedConversationId,
                           gizmoId);
            }
        }
        else if (DesignTabPinService.IsOnDesignTarget(source, bundle))
        {
            return true;
        }
        else if (ProjectChatDraftService.IsValidDraftTarget(bundle, source, intent))
        {
            return true;
        }
        else if (!string.IsNullOrWhiteSpace(DesignTabPinService.GetDesignConversationId(bundle)))
        {
            var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
            var designConversationId = DesignTabPinService.GetDesignConversationId(bundle);
            return !string.IsNullOrWhiteSpace(gizmoId)
                   && !string.IsNullOrWhiteSpace(designConversationId)
                   && AdventurePlayContextService.IsOnPlayConversationPage(
                       source,
                       designConversationId,
                       gizmoId);
        }

        return IsOnLinkedProjectPage(source, bundle);
    }

    public static string? ResolveLinkedProjectPageUrl(AdventureBundle bundle)
    {
        SyncLinkedFields(bundle);
        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        return ChatGptUrls.BuildProjectUrl(ChatGptUrls.NormalizeGizmoId(gizmoId));
    }

    public static string? ResolveRecoveryUrl(AdventureBundle bundle, AdventureNavigationIntent intent)
    {
        if (!HasLinkedProject(bundle))
            return null;

        SyncLinkedFields(bundle);

        if (intent == AdventureNavigationIntent.Play
            && !AdventureProjectBindingService.ShouldDeferLinkedPlayContextAfterProjectLink(bundle)
            && !ProjectChatDraftService.IsActive(bundle))
        {
            var conversationId = PlayThreadBindingService.GetActiveConversationId(bundle);
            var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
            if (!string.IsNullOrWhiteSpace(conversationId)
                && !string.IsNullOrWhiteSpace(gizmoId)
                && !PlayThreadBindingService.IsRejectedConversationId(bundle, conversationId)
                && PlayThreadBindingService.HasBrowsablePlayTarget(bundle))
            {
                var entry = PlayThreadBindingService.GetActivePlayEntry(bundle);
                if (!string.IsNullOrWhiteSpace(entry?.PinnedTabUrl)
                    && Uri.TryCreate(entry.PinnedTabUrl, UriKind.Absolute, out var pinnedUri)
                    && ChatGptUrls.TryParseConversationId(pinnedUri, out var pinnedConv)
                    && string.Equals(pinnedConv, conversationId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry.PinnedTabUrl;
                }

                return ChatGptUrls.ResolveProjectConversationUrl(
                    conversationId,
                    gizmoId,
                    entry?.PinnedTabUrl);
            }
        }

        if (intent == AdventureNavigationIntent.Design)
        {
            var designUrl = ResolveDesignBrowseUrl(bundle, preferThread: true);
            if (!string.IsNullOrWhiteSpace(designUrl)
                && Uri.TryCreate(designUrl, UriKind.Absolute, out var uri)
                && ChatGptUrls.TryParseConversationId(uri, out _))
            {
                return designUrl;
            }
        }

        return ResolveLinkedProjectPageUrl(bundle);
    }

    public static string FormatHomepageRecoveryError(AdventureNavigationIntent intent)
    {
        var mode = intent == AdventureNavigationIntent.Design ? "Design" : "Play";
        return $"ChatGPT session lost access to the linked Project while {mode} was open. "
               + "Sign in on the ChatGPT tab if prompted, then use Link Project or reopen the adventure.";
    }

    public static string DescribeNavigationState(string? source, AdventureBundle bundle, AdventureNavigationIntent intent)
    {
        if (IsGenericHomepage(source))
            return "homepage";

        var targetUrl = intent == AdventureNavigationIntent.Play
            ? ResolvePlayBrowseUrl(bundle)
            : ResolveDesignBrowseUrl(bundle);

        if (intent == AdventureNavigationIntent.Play && PlayTabPinService.IsOnPlayTarget(source, bundle))
            return "play thread";

        if (intent == AdventureNavigationIntent.Design && DesignTabPinService.IsOnDesignTarget(source, bundle))
            return "design thread";

        if (IsOnLinkedProjectPage(source, bundle))
            return "project page";

        if (!string.IsNullOrWhiteSpace(targetUrl)
            && string.Equals(source, targetUrl, StringComparison.OrdinalIgnoreCase))
            return "expected url";

        return string.IsNullOrWhiteSpace(source) ? "empty" : "other";
    }
}

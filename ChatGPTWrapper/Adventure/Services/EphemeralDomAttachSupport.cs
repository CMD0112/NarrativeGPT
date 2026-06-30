using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>CMD-424: gate, provision, and fallback resolution for ephemeral DOM attach.</summary>
internal static class EphemeralDomAttachSupport
{
    internal const string BootstrapSeedText = ".";

    internal sealed record AttachTarget(
        string ConversationId,
        CreateProjectConversationResult Created,
        bool SkipPageEnsure);

    internal sealed record AttachProbe(
        string? PageHref,
        bool ComposerFound,
        bool SubmitFound,
        string? ConversationId)
    {
        public bool OnProjectHome =>
            UtilityConversationPageService.IsProjectHomePage(PageHref);
    }

    public static bool CanAttachOnConversationPage(string? pageHref, string conversationId, string gizmoId) =>
        !string.IsNullOrWhiteSpace(conversationId)
        && UtilityConversationPageService.MatchesTargetConversation(pageHref, conversationId, gizmoId);

    public static bool RequiresConversationProvision(
        AttachProbe probe,
        string? conversationId,
        string gizmoId)
    {
        if (CanAttachOnConversationPage(probe.PageHref, conversationId ?? probe.ConversationId ?? "", gizmoId)
            && probe.SubmitFound)
        {
            return false;
        }

        if (probe.OnProjectHome && !probe.SubmitFound)
            return true;

        return string.IsNullOrWhiteSpace(conversationId)
               && string.IsNullOrWhiteSpace(probe.ConversationId);
    }

    public static async Task<AttachProbe> ProbeAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var health = await turnService.GetAdventureComposerHealthAsync(core, cancellationToken);
        var href = await UtilityConversationPageService.GetPageHrefAsync(core);
        return new AttachProbe(
            href,
            health.ComposerFound,
            health.SubmitFound,
            health.ConversationId);
    }

    public static void LogAttachProbe(string phase, AttachProbe probe, int? attachmentCount = null)
    {
        ProjectLinkDiagnostics.Log(
            $"ephemeral_dom_attach_probe phase={phase} href={probe.PageHref} "
            + $"composer={probe.ComposerFound} submit={probe.SubmitFound} "
            + $"conv={probe.ConversationId ?? ""} files={attachmentCount?.ToString() ?? ""}");
    }

    public static async Task<AttachTarget?> ResolveAttachTargetAsync(
        CoreWebView2 core,
        string gizmoId,
        string conversationId,
        CreateProjectConversationResult created,
        AdventureTurnService turnService,
        CancellationToken cancellationToken)
    {
        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);
        var probe = await ProbeAsync(turnService, core, cancellationToken);
        LogAttachProbe("resolve", probe);

        if (CanAttachOnConversationPage(probe.PageHref, conversationId, gizmoId))
        {
            return new AttachTarget(
                conversationId,
                created,
                SkipPageEnsure: false);
        }

        if (!RequiresConversationProvision(probe, conversationId, gizmoId)
            && probe.SubmitFound
            && !string.IsNullOrWhiteSpace(conversationId))
        {
            return new AttachTarget(conversationId, created, SkipPageEnsure: false);
        }

        if (!RequiresConversationProvision(probe, conversationId, gizmoId)
            && !probe.OnProjectHome
            && !string.IsNullOrWhiteSpace(probe.ConversationId))
        {
            return new AttachTarget(
                probe.ConversationId!,
                new CreateProjectConversationResult { ConversationId = probe.ConversationId },
                SkipPageEnsure: false);
        }

        return await TryProvisionConversationAsync(
            core,
            gizmoId,
            turnService,
            cancellationToken);
    }

    internal static async Task<AttachTarget?> TryProvisionConversationAsync(
        CoreWebView2 core,
        string gizmoId,
        AdventureTurnService turnService,
        CancellationToken cancellationToken)
    {
        ProjectLinkDiagnostics.Log("ephemeral_attach_provision_start");
        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        var start = await turnService.StartProjectChatAsync(core, cancellationToken);
        if (start.Success)
        {
            var fromStart = await WaitForConversationMaterializedAsync(
                turnService,
                core,
                gizmoId,
                maxAttempts: 20,
                cancellationToken);
            if (fromStart is not null)
                return fromStart;
        }

        ProjectLinkDiagnostics.Log("ephemeral_attach_provision_seed_send");
        var seed = await turnService.SubmitUtilityJobAsync(
            core,
            conversationId: "",
            gizmoId,
            BootstrapSeedText,
            timeoutMs: 60_000,
            skipPageEnsure: true,
            maxComposerWaitSeconds: 15,
            cancellationToken: cancellationToken);

        if (!seed.Success
            && !string.Equals(seed.Error, "capture_premature", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(seed.Error, "capture_timeout", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(seed.Error, "submit_not_verified", StringComparison.OrdinalIgnoreCase))
        {
            ProjectLinkDiagnostics.Log(
                $"ephemeral_attach_provision_seed_failed error={seed.Error ?? "unknown"}");
            return null;
        }

        var materialized = await WaitForConversationMaterializedAsync(
            turnService,
            core,
            gizmoId,
            maxAttempts: 60,
            cancellationToken);
        if (materialized is null)
        {
            ProjectLinkDiagnostics.Log("ephemeral_attach_provision_failed");
            return null;
        }

        ProjectLinkDiagnostics.Log(
            $"ephemeral_attach_provision_ready conv={materialized.ConversationId}");
        return materialized;
    }

    private static async Task<AttachTarget?> WaitForConversationMaterializedAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        string gizmoId,
        int maxAttempts,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

            var probe = await ProbeAsync(turnService, core, cancellationToken);
            var conversationId = probe.ConversationId ?? await turnService.GetConversationIdAsync(core);
            if (string.IsNullOrWhiteSpace(conversationId))
                continue;

            if (UtilityConversationPageService.IsProjectHomePage(probe.PageHref))
            {
                var nav = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
                    core,
                    conversationId,
                    gizmoId,
                    cancellationToken);
                if (!nav.Success)
                    continue;
            }

            probe = await ProbeAsync(turnService, core, cancellationToken);
            LogAttachProbe("materialized", probe);
            if (CanAttachOnConversationPage(probe.PageHref, conversationId, gizmoId)
                || probe.SubmitFound
                || !probe.OnProjectHome)
            {
                return new AttachTarget(
                    conversationId,
                    new CreateProjectConversationResult { ConversationId = conversationId },
                    SkipPageEnsure: false);
            }
        }

        return null;
    }

    public static string? ResolveFallbackConversationId(
        string? pushConversationId,
        string? ephemeralConversationId,
        string? pinnedConversationId) =>
        FirstNonEmpty(pushConversationId, ephemeralConversationId, pinnedConversationId);

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return null;
    }
}

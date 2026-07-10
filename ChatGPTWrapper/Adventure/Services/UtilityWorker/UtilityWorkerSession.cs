using System.Collections.Concurrent;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>
/// Per-adventure utility worker session: single conversation authority and page readiness.
/// </summary>
internal sealed class UtilityWorkerSession
{
    private static readonly ConcurrentDictionary<Guid, UtilityWorkerSession> Sessions = new();
    private static readonly ConcurrentDictionary<Guid, DateTimeOffset> RateLimitBackoffUntil = new();

    private UtilityWorkerSession(Guid adventureId) => AdventureId = adventureId;

    public Guid AdventureId { get; }

    public static UtilityWorkerSession For(Guid adventureId) =>
        Sessions.GetOrAdd(adventureId, id => new UtilityWorkerSession(id));

    public static string? GetConversationId(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var fromRegistry = AdventureThreadRegistryService.GetActiveConversationId(
            bundle,
            AdventureThreadKind.UtilityWorker);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
            return fromRegistry;

        return UtilityWorkerSessionService.GetSession(bundle.Metadata)?.ConversationId;
    }

    public static bool HasPin(AdventureBundle bundle) =>
        !string.IsNullOrWhiteSpace(GetConversationId(bundle));

    public static void SyncCapabilitiesConversationId(AdventureBundle bundle)
    {
        var conv = GetConversationId(bundle);
        if (string.IsNullOrWhiteSpace(conv))
            return;

        bundle.Metadata.UtilityWorkerCapabilities ??= new UtilityWorkerCapabilities();
        bundle.Metadata.UtilityWorkerCapabilities.WorkerConversationId = conv;
    }

    public bool IsRateLimited =>
        RateLimitBackoffUntil.TryGetValue(AdventureId, out var until)
        && DateTimeOffset.UtcNow < until;

    public void ApplyRateLimit(TimeSpan duration) =>
        RateLimitBackoffUntil[AdventureId] = DateTimeOffset.UtcNow.Add(duration);

    public async Task<UtilityConversationPageResult> EnsurePageReadyAsync(
        CoreWebView2 core,
        AdventureBundle bundle,
        CancellationToken cancellationToken = default)
    {
        var conversationId = GetConversationId(bundle);
        var gizmoId = bundle.Metadata.LinkedProjectId;
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            return new UtilityConversationPageResult
            {
                Success = false,
                Error = "worker_not_configured",
            };
        }

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        if (UtilityEphemeralWorkerPolicy.IsEnabled(bundle))
        {
            if (AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
                return new UtilityConversationPageResult { Success = true };

            var projectUrl = ChatGptUrls.BuildProjectUrl(gizmoId);
            core.Navigate(projectUrl);
            for (var attempt = 0; attempt < 30; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (AdventureNavigationService.IsOnLinkedProjectPage(core.Source, bundle))
                    return new UtilityConversationPageResult { Success = true };

                await Task.Delay(500, cancellationToken);
            }

            return new UtilityConversationPageResult
            {
                Success = false,
                Error = "project_page_not_ready",
            };
        }

        if (string.IsNullOrWhiteSpace(conversationId))
        {
            return new UtilityConversationPageResult
            {
                Success = false,
                Error = "worker_not_configured",
            };
        }

        var nav = await UtilityConversationPageService.EnsureOnProjectConversationStrictAsync(
            core,
            conversationId,
            gizmoId,
            cancellationToken);
        if (!nav.Success)
            return nav;

        return await UtilityConversationPageService.WaitForStableOnConversationPageAsync(
            core,
            conversationId,
            gizmoId,
            cancellationToken);
    }
}

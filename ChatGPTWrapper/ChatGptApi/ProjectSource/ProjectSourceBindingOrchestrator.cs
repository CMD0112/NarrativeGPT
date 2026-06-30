using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi.ProjectSource;

/// <summary>
/// Publication bind ladder. Byte-level integrity is proven later by
/// <see cref="ProjectSourceIntegrityVerifier"/>; listing APIs are advisory only.
/// <list type="number">
/// <item>Snorlax (<c>g-p-*</c>): project-files attach only for publication (no detail upsert — fork risk).</item>
/// <item>Snorlax escalation: library upload, then DOM/CDP when API lanes fail.</item>
/// <item>Legacy projects: merge upsert attach.</item>
/// </list>
/// </summary>
internal sealed class ProjectSourceBindingOrchestrator
{
    private readonly ChatGptProjectApiService _api;

    public ProjectSourceBindingOrchestrator(ChatGptProjectApiService api)
    {
        _api = api;
    }

    public Task<ProjectSourceBindingStrategy> BindAsync(
        CoreWebView2 core,
        string gizmoId,
        GizmoFileRef file,
        Guid? adventureId,
        CancellationToken cancellationToken) =>
        _api.BindSourceFileForPublicationAsync(core, gizmoId, file, adventureId, cancellationToken);

    internal static ProjectSourceBindingStrategy ResolveSnorlaxSyncAttachStrategy(bool usedDetailUpsertFallback) =>
        usedDetailUpsertFallback
            ? ProjectSourceBindingStrategy.SnorlaxDetailUpsert
            : ProjectSourceBindingStrategy.SnorlaxProjectFilesApi;
}

internal static class ProjectSourceBindingStrategyExtensions
{
    public static bool UsedUpsertFallback(this ProjectSourceBindingStrategy strategy) =>
        strategy == ProjectSourceBindingStrategy.SnorlaxDetailUpsert;
}

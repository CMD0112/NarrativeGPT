using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityReferenceEditCallbacks
{
    public Func<IReadOnlyList<PhraseHighlightRule>?>? GetPhraseHighlightRules { get; init; }

    public Func<Task>? OpenSourceManagerAsync { get; init; }

    public Action<AdventureBundle>? OnBundleReloaded { get; init; }

    public Action? OnStatusRefreshRequested { get; init; }

    public Action<EntityEditSourceSyncResult>? OnSourceSyncCompleted { get; init; }
}

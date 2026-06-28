using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class EntityReferenceEditCallbacks
{
    public Action<IReadOnlyList<PhraseHighlightRule>>? CommitPhraseHighlightRules { get; init; }

  public Func<IReadOnlyList<PhraseHighlightRule>?>? GetPhraseHighlightRules { get; init; }

    public Func<Task>? OpenSourceManagerAsync { get; init; }

    public Action<AdventureBundle>? OnBundleReloaded { get; init; }

    public Action? OnStatusRefreshRequested { get; init; }

    public Action<EntityEditSourceSyncResult>? OnSourceSyncCompleted { get; init; }

    /// <summary>Fired after phrase highlight rules are persisted to chrome (Format ↔ entity card sync).</summary>
    public Action? OnPhraseHighlightRulesChanged { get; init; }

    /// <summary>Play-only: append text to the play composer prompt.</summary>
    public Action<string>? InsertIntoComposer { get; init; }

    /// <summary>Play-only: switch companion to the State tab.</summary>
    public Action? OpenStateTab { get; init; }
}

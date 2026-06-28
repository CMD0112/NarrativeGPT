namespace ChatGPTWrapper.Adventure.Services;

public sealed class PhraseHighlightPruneReport
{
    public IReadOnlyList<string> RemovedPhrases { get; init; } = [];

    public int RemovedCount => RemovedPhrases.Count;
}

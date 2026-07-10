namespace ChatGPTWrapper.Adventure.Services;

public sealed class PhraseHighlightBasicAliasReport
{
    public IReadOnlyList<string> AddedPhrases { get; init; } = [];

    public IReadOnlyList<string> UpdatedPhrases { get; init; } = [];

    public int ChangedCount => AddedPhrases.Count + UpdatedPhrases.Count;
}

public sealed class PhraseHighlightEntityAliasAlignReport
{
    public IReadOnlyList<string> RemovedPhrases { get; init; } = [];

    public IReadOnlyList<string> AddedPhrases { get; init; } = [];

    public IReadOnlyList<string> UpdatedPhrases { get; init; } = [];

    public int ChangedCount => RemovedPhrases.Count + AddedPhrases.Count + UpdatedPhrases.Count;
}

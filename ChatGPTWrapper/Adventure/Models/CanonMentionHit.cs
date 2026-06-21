namespace ChatGPTWrapper.Adventure.Models;

public enum CanonMentionKind
{
    Name,
    Alias,
    Trigger,
    ContextIndex,
    JsonField,
}

public sealed class CanonMentionHit
{
    public string File { get; init; } = "";

    public string? SectionId { get; init; }

    public int LineNumber { get; init; }

    public string MatchedTerm { get; init; } = "";

    public CanonMentionKind Kind { get; init; }

    public string Snippet { get; init; } = "";

    public EntityTextReplacementAction Action { get; set; } = EntityTextReplacementAction.Replace;
}

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>One message node on the active ChatGPT conversation branch.</summary>
public sealed class ConversationBranchMessage
{
    public string NodeId { get; init; } = "";

    public string? MessageId { get; init; }

    public string Role { get; init; } = "";

    public string RawText { get; init; } = "";

    public string? DisplayText { get; init; }

    public string? ParentNodeId { get; init; }

    public int BranchIndex { get; init; }

    public double? CreateTime { get; init; }

    public bool IsUtility { get; init; }

    public bool IsInjectedContext { get; init; }
}

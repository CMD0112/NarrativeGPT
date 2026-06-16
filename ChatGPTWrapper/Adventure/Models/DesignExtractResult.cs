namespace ChatGPTWrapper.Adventure.Models;

public sealed class DesignExtractResult
{
    public bool Success { get; init; }

    public int ProposalCount { get; init; }

    public string? Error { get; init; }
}

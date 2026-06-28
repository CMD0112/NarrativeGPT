namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Persisted utility job parse result (CMD-332).</summary>
public sealed class UtilityJobRunRecord
{
    public Guid RunId { get; set; } = Guid.NewGuid();

    public string JobId { get; set; } = "";

    public int SchemaVersion { get; set; } = 1;

    public string Trigger { get; set; } = "";

    public int? LinkedTurnIndex { get; set; }

    public string? ConversationId { get; set; }

    public string? PromptHash { get; set; }

    public string? RawResponse { get; set; }

    public string? ParsedPayload { get; set; }

    public List<Guid> ProposalIds { get; set; } = [];

    public int ProposalCount { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? SentMessageId { get; set; }

    public string? AssistantMessageId { get; set; }

    public UtilityJobRunState State { get; set; } = UtilityJobRunState.Complete;

    public string Lane { get; set; } = "";

    public bool StreamComplete { get; set; }

    public string? PushError { get; set; }

    public string? PullError { get; set; }

    public DateTimeOffset? PushedAt { get; set; }
}

/// <summary>Index of latest utility runs per job id.</summary>
public sealed class UtilityJobResultsIndex
{
    public Dictionary<string, List<Guid>> RunsByJobId { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

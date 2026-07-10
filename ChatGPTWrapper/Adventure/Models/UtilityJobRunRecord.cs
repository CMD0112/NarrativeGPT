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

    /// <summary>Set when the user has cleared this run's proposals from review (accept or dismiss).</summary>
    public DateTimeOffset? ReviewResolvedAt { get; set; }

    public UtilityContextManifestRecord? ContextManifest { get; set; }

    /// <summary>Flight recorder entry when this run was bundled with a verified play send.</summary>
    public Guid? LinkedFlightRecordId { get; set; }

    /// <summary>Pairs local-llm and ChatGPT utility runs from the same dual-run job.</summary>
    public Guid? DualRunGroupId { get; set; }

    /// <summary>Play-thread ingest event frozen at utility job dispatch.</summary>
    public Guid? PlayThreadIngestEventId { get; set; }

    public Guid? PlayThreadEntryId { get; set; }

    public string? PlayThreadRawPath { get; set; }

    public string? PlayThreadProjectionPath { get; set; }

    /// <summary>Relative path under <c>utility-results/{runId}/</c> for assembled context snapshot.</summary>
    public string? ContextProjectionPath { get; set; }

    /// <summary>Canonical Project sources input path when source file I/O catalog applies.</summary>
    public string? SourceIoInputPath { get; set; }

    /// <summary>Ephemeral utility chat id (captured before thread delete).</summary>
    public string? EphemeralConversationId { get; set; }

    /// <summary>Relative path under <c>utility-results/{runId}/</c> for ephemeral job capture metadata.</summary>
    public string? EphemeralCapturePath { get; set; }
}

/// <summary>Index of latest utility runs per job id.</summary>
public sealed class UtilityJobResultsIndex
{
    public Dictionary<string, List<Guid>> RunsByJobId { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

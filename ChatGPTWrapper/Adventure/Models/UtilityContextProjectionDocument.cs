namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Persisted play-thread projection snapshot at utility job dispatch (<c>utility-results/{runId}/context-projection.json</c>).</summary>
public sealed class UtilityContextProjectionDocument
{
    public int SchemaVersion { get; set; } = 1;

    public Guid RunId { get; set; }

    public string JobId { get; set; } = "";

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? PlayThreadEntryId { get; set; }

    public Guid? PlayThreadIngestEventId { get; set; }

    public string? PlayThreadRawPath { get; set; }

    public string? PlayThreadProjectionPath { get; set; }

    public string ProjectionSource { get; set; } = "";

    public int TurnPairCount { get; set; }

    public int MessageCount { get; set; }

    public Guid? LinkedTurnId { get; set; }
}

/// <summary>Ephemeral utility chat capture metadata before thread delete.</summary>
public sealed class UtilityEphemeralCaptureDocument
{
    public int SchemaVersion { get; set; } = 1;

    public Guid RunId { get; set; }

    public string JobId { get; set; } = "";

    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;

    public string? ConversationId { get; set; }

    public string CaptureTrigger { get; set; } = "ephemeral_job_complete";

    public string? PromptHash { get; set; }

    public string? ContentHash { get; set; }

    public int ResponseCharCount { get; set; }

    public bool StreamComplete { get; set; }
}

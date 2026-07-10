namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Pending utility job on the worker lane.</summary>
public sealed class UtilityOutboxEntry
{
    public Guid RunId { get; set; } = Guid.NewGuid();

    public string JobId { get; set; } = "";

    public UtilityExecutionChannel Channel { get; set; } = UtilityExecutionChannel.ManualBackground;

    public UtilityJobRunState State { get; set; } = UtilityJobRunState.Queued;

    public string Lane { get; set; } = UtilityLane.Worker;

    public Guid? LinkedTurnId { get; set; }

    public int? TurnIndex { get; set; }

    public Guid? EntityId { get; set; }

    public string? EntityKind { get; set; }

    public Guid? CardId { get; set; }

    public string? SentMessageId { get; set; }

    public string? AssistantMessageId { get; set; }

    public bool StreamComplete { get; set; }

    public string? PartialAssistantText { get; set; }

    public string? PushError { get; set; }

    public string? PullError { get; set; }

    public string? PromptHash { get; set; }

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? PushedAt { get; set; }

    /// <summary>Set when coordinator finished utility source I/O publish for this run.</summary>
    public DateTimeOffset? SourceInputsPublishedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Parallel slot that owns this entry while in flight (0 = unclaimed).</summary>
    public int ClaimedBySlot { get; set; }

    public DateTimeOffset? ClaimedAt { get; set; }

    public List<UtilityOutboxAttachment>? Attachments { get; set; }

    public string? UserPrompt { get; set; }

    public string? AttachmentReferenceNote { get; set; }
}

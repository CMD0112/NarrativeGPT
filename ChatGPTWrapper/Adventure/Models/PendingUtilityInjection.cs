namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Queued utility job waiting to be embedded in the next play packet.</summary>
public sealed class PendingUtilityInjection
{
    public Guid RunId { get; set; } = Guid.NewGuid();

    public string JobId { get; set; } = "";

    public UtilityExecutionChannel Channel { get; set; } = UtilityExecutionChannel.AutoBackground;

    public Guid? LinkedTurnId { get; set; }

    public int? TurnIndex { get; set; }

    public Guid? EntityId { get; set; }

    public string? EntityKind { get; set; }

    public Guid? CardId { get; set; }

    public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.UtcNow;
}

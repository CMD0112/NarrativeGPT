namespace ChatGPTWrapper.Adventure.Models;

public sealed class TurnRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public int Index { get; set; }

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    public string PlayerText { get; set; } = "";

    public string? NarratorText { get; set; }

    public TurnStatus Status { get; set; } = TurnStatus.Pending;

    public Guid? ParentTurnId { get; set; }

    public List<ResponseAttempt> Attempts { get; set; } = [];

    public Guid? SessionId { get; set; }

    public Guid? ChapterId { get; set; }

    public string? PromptPacketHash { get; set; }

    /// <summary>ChatGPT play-thread conversation id when this turn was accepted.</summary>
    public string? ConversationId { get; set; }
}

namespace ChatGPTWrapper.Adventure.Models;

public sealed class UtilityExchangeRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string JobId { get; set; } = "";

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    public string PromptHash { get; set; } = "";

    public string? ResponseText { get; set; }

    public string? ConversationId { get; set; }
}

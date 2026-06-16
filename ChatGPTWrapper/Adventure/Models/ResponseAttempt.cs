namespace ChatGPTWrapper.Adventure.Models;

public sealed class ResponseAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset At { get; set; } = DateTimeOffset.UtcNow;

    public string? NarratorText { get; set; }

    public bool Accepted { get; set; }

    public bool FromRegenerate { get; set; }
}

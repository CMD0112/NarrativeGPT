namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Wrapper-side index entry for a message on the play thread active branch.</summary>
public sealed class ThreadMessageRecord
{
    public string MessageId { get; set; } = "";

    public int Ordinal { get; set; }

    public string Role { get; set; } = "";

    public string? PlayerLine { get; set; }

    /// <summary>Assistant message body or utility response snippet.</summary>
    public string? BodyText { get; set; }

    public string? PacketHash { get; set; }

    public bool IsUtility { get; set; }

    public bool IsInjectedContext { get; set; }

    public bool HiddenInDisplay { get; set; }

    public bool SupersededByEdit { get; set; }

    public Guid? LinkedTurnId { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

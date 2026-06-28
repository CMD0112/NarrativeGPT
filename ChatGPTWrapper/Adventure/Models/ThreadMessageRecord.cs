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

    /// <summary>auto | manual when <see cref="IsUtility"/> (CMD-329).</summary>
    public string? UtilityChannel { get; set; }

    public bool SupersededByEdit { get; set; }

    public Guid? LinkedTurnId { get; set; }

    /// <summary>CMD-352 taxonomy: play_user, narrator_revision_prompt, narrator_replacement, …</summary>
    public string? MessageKind { get; set; }

    /// <summary>Links original + revision prompt + replacement for one narrator edit.</summary>
    public string? RevisionGroupId { get; set; }

    /// <summary>Prior <see cref="MessageId"/> this record replaces.</summary>
    public string? SupersedesMessageId { get; set; }

    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

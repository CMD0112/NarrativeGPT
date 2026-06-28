namespace ChatGPTWrapper.Adventure.Models;

/// <summary>Authoritative link from overlay play-pair ordinal to log turn.</summary>
public sealed class LogTurnLink
{
    public Guid TurnId { get; init; }

    public int TurnIndex { get; init; }

    public string PlayerSnippet { get; init; } = "";

    public int DisplayTurnNumber { get; init; }
}

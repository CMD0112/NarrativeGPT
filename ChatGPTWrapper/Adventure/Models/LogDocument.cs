namespace ChatGPTWrapper.Adventure.Models;

public sealed class LogDocument
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public List<TurnRecord> Turns { get; set; } = [];

    public List<PlaySession> Sessions { get; set; } = [];

    public List<StoryChapter> Chapters { get; set; } = [];
}

public sealed class PlaySession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndedAt { get; set; }

    public List<Guid> TurnIds { get; set; } = [];
}

public sealed class StoryChapter
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "";

    public int StartTurnIndex { get; set; }
}

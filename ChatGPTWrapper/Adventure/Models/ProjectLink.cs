namespace ChatGPTWrapper.Adventure.Models;

public sealed class ProjectLink
{
    public int SchemaVersion { get; set; } = AdventureJson.SchemaVersion;

    public string GizmoId { get; set; } = "";

    public string? CanonicalUrl { get; set; }

    public string? PlayConversationId { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset? LinkedAt { get; set; }
}

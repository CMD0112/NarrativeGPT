namespace ChatGPTWrapper.Adventure.Models;

public sealed class UtilitySourceFileRegistryDocument
{
    public int SchemaVersion { get; set; } = 1;

    public List<UtilitySourceFileRegistryEntry> Entries { get; set; } = [];
}

public sealed class UtilitySourceFileRegistryEntry
{
    public Guid RunId { get; set; }

    public string JobId { get; set; } = "";

    public string RemotePath { get; set; } = "";

    public string? FileId { get; set; }

    /// <summary>Lowercase hex SHA-256 of published bytes (utility fast-publish idempotency).</summary>
    public string? ContentSha256 { get; set; }

    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;

    public UtilitySourceFileDeleteTrigger DeleteTrigger { get; set; } =
        UtilitySourceFileDeleteTrigger.OnJobComplete;

    public DateTimeOffset? DeleteAfterUtc { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public string? DeleteError { get; set; }
}

public enum UtilitySourceFileDeleteTrigger
{
    OnJobComplete = 0,
    TtlFallback = 1,
}

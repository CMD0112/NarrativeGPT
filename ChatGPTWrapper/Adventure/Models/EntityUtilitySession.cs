namespace ChatGPTWrapper.Adventure.Models;

public sealed class GenerationUtilitySession
{
    public string ConversationId { get; set; } = "";

    public int Sequence { get; set; } = 1;

    public int SeedVersion { get; set; }

    public int JobCount { get; set; }

    public int ConsecutiveParseFailures { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed class GenerationUtilitySessionArchive
{
    public string JobId { get; set; } = "";

    public string ConversationId { get; set; } = "";

    public int Sequence { get; set; }

    public DateTimeOffset RotatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Reason { get; set; } = "";
}

// Legacy type for JSON migration on adventure.json
public sealed class EntityUtilitySession
{
    public string ConversationId { get; set; } = "";

    public int Sequence { get; set; } = 1;

    public int SeedVersion { get; set; }

    public int JobCount { get; set; }

    public int ConsecutiveParseFailures { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAt { get; set; }
}

public sealed class EntityUtilitySessionArchive
{
    public string ConversationId { get; set; } = "";

    public int Sequence { get; set; }

    public DateTimeOffset RotatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string Reason { get; set; } = "";
}

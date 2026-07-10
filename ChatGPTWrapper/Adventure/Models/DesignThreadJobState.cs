namespace ChatGPTWrapper.Adventure.Models;

/// <summary>
/// Design-thread job counters formerly stored in <c>UtilitySessions[design_adventure]</c>.
/// </summary>
public sealed class DesignThreadJobState
{
    public int Sequence { get; set; } = 1;

    public int SeedVersion { get; set; }

    public int JobCount { get; set; }

    public int ConsecutiveParseFailures { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastUsedAt { get; set; }

    public static DesignThreadJobState FromUtilitySession(GenerationUtilitySession session) =>
        new()
        {
            Sequence = session.Sequence,
            SeedVersion = session.SeedVersion,
            JobCount = session.JobCount,
            ConsecutiveParseFailures = session.ConsecutiveParseFailures,
            CreatedAt = session.CreatedAt,
            LastUsedAt = session.LastUsedAt,
        };

    public GenerationUtilitySession ToUtilitySession(string conversationId) =>
        new()
        {
            ConversationId = conversationId,
            Sequence = Sequence,
            SeedVersion = SeedVersion,
            JobCount = JobCount,
            ConsecutiveParseFailures = ConsecutiveParseFailures,
            CreatedAt = CreatedAt,
            LastUsedAt = LastUsedAt,
        };
}

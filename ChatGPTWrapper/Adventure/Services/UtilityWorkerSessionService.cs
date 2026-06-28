using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Single multiplexed utility worker session per adventure.</summary>
internal static class UtilityWorkerSessionService
{
    public const string SessionJobId = "utility_worker";

    public const string TitlePrefix = "[CGW:worker]";

    public static string GetTitlePrefix() => TitlePrefix;

    public static string BuildWorkerTitleLine(AdventureBundle bundle, int sequence) =>
        $"{TitlePrefix} {bundle.Metadata.Title} · {bundle.Metadata.Id:N} · #{sequence}";

    public static GenerationUtilitySession? GetSession(AdventureMetadata metadata) =>
        GenerationUtilitySessionService.GetSession(metadata, SessionJobId);

    public static string? GetWorkerConversationId(AdventureBundle bundle)
    {
        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var fromRegistry = AdventureThreadRegistryService.GetActiveConversationId(
            bundle,
            AdventureThreadKind.UtilityWorker);
        if (!string.IsNullOrWhiteSpace(fromRegistry))
            return fromRegistry;

        return GetSession(bundle.Metadata)?.ConversationId;
    }

    public static bool MatchesWorkerConversationTitle(string? title, Guid adventureId) =>
        !string.IsNullOrWhiteSpace(title)
        && title.Contains(TitlePrefix, StringComparison.OrdinalIgnoreCase)
        && title.Contains(adventureId.ToString("N"), StringComparison.OrdinalIgnoreCase);

    public static GenerationUtilitySession? TryReconcileSession(
        AdventureBundle bundle,
        IReadOnlyList<GizmoConversationRef> conversations)
    {
        var adventureId = bundle.Metadata.Id;
        var matches = conversations
            .Where(c => MatchesWorkerConversationTitle(c.Title, adventureId))
            .OrderByDescending(c => c.UpdatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (matches.Count == 0)
            return null;

        var best = matches[0];
        return new GenerationUtilitySession
        {
            ConversationId = best.Id,
            Sequence = GenerationUtilitySessionService.TryParseSequenceFromTitle(best.Title) ?? matches.Count,
            SeedVersion = 1,
            CreatedAt = best.UpdatedAt ?? DateTimeOffset.UtcNow,
            LastUsedAt = best.UpdatedAt,
        };
    }

    public static void BindSession(AdventureBundle bundle, GenerationUtilitySession session)
    {
        AdventureMetadataMigration.MigrateUtilitySessions(bundle.Metadata);
        bundle.Metadata.UtilitySessions[SessionJobId] = session;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(
            bundle,
            AdventureThreadKind.UtilityWorker,
            "Utility worker");
        entry.ConversationId = session.ConversationId;
    }

    public static void RecordJobCompleted(AdventureMetadata metadata, bool parseSuccess)
    {
        var session = GetSession(metadata);
        if (session is null)
            return;

        session.JobCount++;
        session.LastUsedAt = DateTimeOffset.UtcNow;
        if (parseSuccess)
            session.ConsecutiveParseFailures = 0;
        else
            session.ConsecutiveParseFailures++;

        metadata.UtilitySessions[SessionJobId] = session;
    }

    public static bool ShouldRotateSession(AdventureBundle bundle, GenerationUtilitySession session) =>
        session.JobCount >= GenerationUtilitySessionService.MaxJobsPerSession
        || session.ConsecutiveParseFailures >= GenerationUtilitySessionService.MaxConsecutiveParseFailures;

    public static void ArchiveSession(AdventureMetadata metadata, GenerationUtilitySession session, string reason) =>
        GenerationUtilitySessionService.ArchiveSession(metadata, SessionJobId, session, reason);

    public static string FormatWorkerStatus(AdventureBundle bundle)
    {
        var caps = bundle.Metadata.UtilityWorkerCapabilities;
        var conv = GetWorkerConversationId(bundle);
        if (caps?.IsGreen == true)
        {
            return !string.IsNullOrWhiteSpace(conv)
                ? "Utility worker: ready"
                : "Utility worker: pin required";
        }

        if (string.IsNullOrWhiteSpace(conv))
            return "Utility worker: not set up";

        return caps?.LastProbeError is { } err
            ? $"Utility worker: not ready ({err})"
            : "Utility worker: probe required";
    }
}

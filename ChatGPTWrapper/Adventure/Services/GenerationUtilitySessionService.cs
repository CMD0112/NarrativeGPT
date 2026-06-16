using System.Text.RegularExpressions;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class GenerationUtilitySessionService
{
    public const int MaxJobsPerSession = 50;
    public const int MaxConsecutiveParseFailures = 3;

    public static string GetTitlePrefix(string jobId) => jobId switch
    {
        GenerationJobId.ProcessTurn => "[CGW:process]",
        GenerationJobId.ExtractEntities or GenerationJobId.ExpandEntity => "[CGW:entity]",
        GenerationJobId.ProposeMemories => "[CGW:memory]",
        GenerationJobId.UpdateSummary => "[CGW:summary]",
        GenerationJobId.BootstrapLore or GenerationJobId.ExpandStoryCard
            or GenerationJobId.BootstrapSections or GenerationJobId.ExpandSection => "[CGW:lore]",
        GenerationJobId.ContinuityCheck => "[CGW:check]",
        GenerationJobId.ProposeSourceEdits => "[CGW:source-edit]",
        GenerationJobId.DesignAdventure or GenerationJobId.DesignExtractStep => "[CGW:design]",
        _ => "[CGW:job]",
    };

    public static int GetSeedVersion(AdventureBundle bundle, string jobId) =>
        GenerationJobGuideService.GetEffectiveSeedVersion(bundle, jobId);

    public static string BuildUtilityTitleLine(AdventureBundle bundle, string jobId, int sequence) =>
        $"{GetTitlePrefix(jobId)} {bundle.Metadata.Title} · {bundle.Metadata.Id:N} · #{sequence}";

    public static GenerationUtilitySession? GetSession(AdventureMetadata metadata, string jobId)
    {
        AdventureMetadataMigration.MigrateUtilitySessions(metadata);
        return metadata.UtilitySessions.TryGetValue(jobId, out var session) ? session : null;
    }

    public static bool ShouldRotateSession(
        AdventureBundle bundle,
        GenerationUtilitySession session,
        string jobId) =>
        session.JobCount >= MaxJobsPerSession
        || session.SeedVersion != GetSeedVersion(bundle, jobId)
        || session.ConsecutiveParseFailures >= MaxConsecutiveParseFailures;

    public static bool MatchesUtilityConversationTitle(string? title, Guid adventureId, string jobId) =>
        !string.IsNullOrWhiteSpace(title)
        && title.Contains(GetTitlePrefix(jobId), StringComparison.OrdinalIgnoreCase)
        && title.Contains(adventureId.ToString("N"), StringComparison.OrdinalIgnoreCase);

    public static GenerationUtilitySession? TryReconcileSession(
        AdventureBundle bundle,
        string jobId,
        IReadOnlyList<GizmoConversationRef> conversations)
    {
        var adventureId = bundle.Metadata.Id;
        var matches = conversations
            .Where(c => MatchesUtilityConversationTitle(c.Title, adventureId, jobId))
            .OrderByDescending(c => c.UpdatedAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (matches.Count == 0)
            return null;

        var best = matches[0];
        return new GenerationUtilitySession
        {
            ConversationId = best.Id,
            Sequence = TryParseSequenceFromTitle(best.Title) ?? matches.Count,
            SeedVersion = GetSeedVersion(bundle, jobId),
            CreatedAt = best.UpdatedAt ?? DateTimeOffset.UtcNow,
            LastUsedAt = best.UpdatedAt,
        };
    }

    public static int? TryParseSequenceFromTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return null;

        var match = Regex.Match(title, @"#(\d+)\s*$");
        return match.Success && int.TryParse(match.Groups[1].Value, out var seq) ? seq : null;
    }

    public static void ArchiveSession(AdventureMetadata metadata, string jobId, GenerationUtilitySession session, string reason)
    {
        metadata.UtilitySessionArchive.Add(new GenerationUtilitySessionArchive
        {
            JobId = jobId,
            ConversationId = session.ConversationId,
            Sequence = session.Sequence,
            RotatedAt = DateTimeOffset.UtcNow,
            Reason = reason,
        });
        metadata.UtilitySessions.Remove(jobId);
    }

    public static int GetNextSequence(AdventureMetadata metadata, string jobId)
    {
        var active = GetSession(metadata, jobId)?.Sequence ?? 0;
        var archivedMax = metadata.UtilitySessionArchive
            .Where(a => string.Equals(a.JobId, jobId, StringComparison.OrdinalIgnoreCase))
            .Select(a => a.Sequence)
            .DefaultIfEmpty(0)
            .Max();
        return Math.Max(active, archivedMax) + 1;
    }

    public static string FormatUtilityStatus(AdventureBundle bundle, string jobId)
    {
        var label = GenerationJobGuideService.GetDisplayLabel(jobId);
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return $"{label} — link a Project to enable";

        var utilityJobId = GenerationJobHandlers.GetUtilityJobId(jobId);
        bundle.Metadata.UtilityJobLastErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bundle.Metadata.UtilityJobLastErrors.TryGetValue(utilityJobId, out var lastError);

        var session = GetSession(bundle.Metadata, utilityJobId);
        if (PlayTabPinService.HasUtilityPin(bundle))
        {
            var convShort = session?.ConversationId is { Length: >= 8 } id
                ? id[..8]
                : "—";
            var jobCount = session?.JobCount ?? 0;
            var errorSuffix = string.IsNullOrWhiteSpace(lastError) ? "" : $" · last error: {lastError}";
            return $"{label} · utility tab pinned · conv={convShort}… · {jobCount} job(s){errorSuffix}";
        }

        if (session is null || string.IsNullOrWhiteSpace(session.ConversationId))
        {
            return string.IsNullOrWhiteSpace(lastError)
                ? $"{label} — no active thread"
                : $"{label} — no active thread · last error: {lastError}";
        }

        var lastUsed = session.LastUsedAt?.ToLocalTime().ToString("g") ?? "never";
        var errorSuffix2 = string.IsNullOrWhiteSpace(lastError) ? "" : $" · last error: {lastError}";
        return $"{label} · thread #{session.Sequence} · {session.JobCount} job(s) · last {lastUsed}{errorSuffix2}";
    }

    public static string FormatAllUtilityStatus(AdventureBundle bundle)
    {
        if (string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return "Generation jobs: link a Project to enable AI jobs.";

        return string.Join(Environment.NewLine,
            GenerationJobId.All.Select(jobId => FormatUtilityStatus(bundle, jobId)));
    }
}

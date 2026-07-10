using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class UtilityDualRunCompareItem
{
    public Guid DualRunGroupId { get; init; }

    public string JobId { get; init; } = "";

    public string JobLabel { get; init; } = "";

    public UtilityJobRunRecord? LocalRun { get; init; }

    public UtilityJobRunRecord? RemoteRun { get; init; }

    public int LocalProposalCount => LocalRun?.ProposalCount ?? 0;

    public int RemoteProposalCount => RemoteRun?.ProposalCount ?? 0;
}

public static class UtilityDualRunReviewService
{
    private static readonly HashSet<string> RemoteLanes = new(StringComparer.OrdinalIgnoreCase)
    {
        UtilityLane.PlayLegacyInline,
        UtilityLane.Worker,
        UtilityLane.PlayInjection,
    };

    public static IReadOnlyList<UtilityDualRunCompareItem> ListPendingCompares(Guid adventureId)
    {
        var index = UtilityJobResultStore.LoadIndex(adventureId);
        var groups = new Dictionary<Guid, List<UtilityJobRunRecord>>();

        foreach (var (_, runIds) in index.RunsByJobId)
        {
            foreach (var runId in runIds)
            {
                var run = UtilityJobResultStore.LoadRun(adventureId, runId);
                if (run?.DualRunGroupId is not Guid groupId)
                    continue;

                if (!groups.TryGetValue(groupId, out var list))
                {
                    list = [];
                    groups[groupId] = list;
                }

                list.Add(run);
            }
        }

        var results = new List<UtilityDualRunCompareItem>();
        foreach (var (groupId, runs) in groups)
        {
            var local = runs.FirstOrDefault(r =>
                string.Equals(r.Lane, UtilityLane.LocalLlm, StringComparison.OrdinalIgnoreCase));
            var remote = runs.FirstOrDefault(r => RemoteLanes.Contains(r.Lane));
            if (local is null || remote is null)
                continue;

            if (local.ReviewResolvedAt.HasValue && remote.ReviewResolvedAt.HasValue)
                continue;

            var jobId = local.JobId;
            results.Add(new UtilityDualRunCompareItem
            {
                DualRunGroupId = groupId,
                JobId = jobId,
                JobLabel = GenerationJobGuideService.GetDisplayLabel(jobId),
                LocalRun = local,
                RemoteRun = remote,
            });
        }

        return results
            .OrderByDescending(r => r.LocalRun?.CapturedAt ?? r.RemoteRun?.CapturedAt ?? DateTimeOffset.MinValue)
            .ToList();
    }

    public static string FormatCompareDetail(UtilityDualRunCompareItem item)
    {
        var local = FormatRunSection("Local LLM", item.LocalRun, item.JobId);
        var remote = FormatRunSection("ChatGPT utility", item.RemoteRun, item.JobId);
        return $"""
            Job: {item.JobLabel} ({item.JobId})
            Group: {item.DualRunGroupId:D}

            {local}

            {remote}

            Tagged proposals from each source also appear in the category lists — use the Source filter to isolate Local LLM vs ChatGPT.
            """;
    }

    private static string FormatRunSection(string heading, UtilityJobRunRecord? run, string? jobId = null)
    {
        if (run is null)
            return $"=== {heading} ===\n(missing run record)";

        var lane = UtilityProposalInferenceTagging.FormatSourceLabel(run.Lane);
        var payload = string.IsNullOrWhiteSpace(run.ParsedPayload)
            ? run.RawResponse ?? "(empty)"
            : run.ParsedPayload;
        var error = string.IsNullOrWhiteSpace(run.Error) ? "" : $"\nError: {run.Error}";

        var compliance = string.Equals(run.Lane, UtilityLane.LocalLlm, StringComparison.OrdinalIgnoreCase)
                         && !string.IsNullOrWhiteSpace(jobId)
            ? FormatLocalCompliance(run, jobId)
            : "";

        return $"""
            === {heading} ({lane}) ===
            Run: {run.RunId:D}
            Proposals parsed: {run.ProposalCount}{compliance}{error}

            {payload}
            """;
    }

    private static string FormatLocalCompliance(UtilityJobRunRecord run, string jobId)
    {
        var assessment = LocalUtilityResponseDiagnostics.Assess(jobId, run.RawResponse, run.ProposalCount);
        var hint = string.IsNullOrWhiteSpace(assessment.ComplianceHint)
            ? ""
            : $"\nHint: {assessment.ComplianceHint}";
        return $"""

            Compliance: {assessment.ComplianceLabel}
            Expected: {assessment.ExpectedShapeSummary}{hint}
            """;
    }

    public static void MarkGroupReviewResolved(Guid adventureId, Guid dualRunGroupId)
    {
        foreach (var item in ListPendingCompares(adventureId).Where(c => c.DualRunGroupId == dualRunGroupId))
        {
            if (item.LocalRun is { } local)
                UtilityJobResultStore.MarkRunReviewResolved(adventureId, local.RunId);
            if (item.RemoteRun is { } remote)
                UtilityJobResultStore.MarkRunReviewResolved(adventureId, remote.RunId);
        }
    }
}

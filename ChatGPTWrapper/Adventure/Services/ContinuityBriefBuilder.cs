using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services;

internal static class ContinuityBriefBuilder
{
    public static string BuildBriefJson(AdventureBundle bundle, GenerationJobContext? context = null)
    {
        var brief = new
        {
            triggerTurnIndex = context?.Turn?.Index,
            lastContinuityCheckAt = bundle.Continuity.LastCheckedAt,
            priorWarningFingerprints = bundle.Continuity.DismissedWarningHashes.TakeLast(10).ToList(),
            pendingProposals = new
            {
                entities = bundle.Entities.ReviewQueue.Take(12).Select(e => new
                {
                    kind = ResolveEntityProposalKind(e.ProposedChange),
                    summary = BuildProposalSummary(e.ProposedChange, "name"),
                }).ToList(),
                memories = bundle.Memory.ReviewQueue.Take(12).Select(m => new
                {
                    title = OneLine(m.Text, 80),
                    summary = OneLine(m.Outcome ?? m.Text, 120),
                }).ToList(),
                state = bundle.State.ReviewQueue.Take(6).Select(s => new
                {
                    location = s.Location,
                    objectives = s.Objectives,
                    objectivesRemove = s.ObjectivesRemove,
                    time = s.Time,
                    summary = OneLine(s.Rationale ?? "", 120),
                }).ToList(),
                summary = bundle.Summary.SourceProposals?.Where(p => !p.Resolved).Take(3).Select(p => new
                {
                    proposedDigestExcerpt = OneLine(p.Text, 160),
                }).ToList(),
                sourceEdits = bundle.Scenario.SourceEditReviewQueue.Take(6).Select(s => new
                {
                    targetFile = s.TargetFile,
                    rationale = OneLine(s.Rationale, 120),
                }).ToList(),
            },
        };

        return JsonSerializer.Serialize(brief, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
    }

    private static string ResolveEntityProposalKind(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var action = JsonElementParsing.GetStringProperty(doc.RootElement, "action");
            return string.Equals(action, "update", StringComparison.OrdinalIgnoreCase) ? "update" : "create";
        }
        catch
        {
            return "create";
        }
    }

    private static string BuildProposalSummary(string json, string property)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var name = JsonElementParsing.GetStringProperty(doc.RootElement, property);
            if (!string.IsNullOrWhiteSpace(name))
                return name!;
        }
        catch
        {
            /* ignore */
        }

        return OneLine(json, 120);
    }

    private static string OneLine(string text, int max)
    {
        var normalized = (text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (normalized.Length <= max)
            return normalized;
        return normalized[..max] + "…";
    }
}

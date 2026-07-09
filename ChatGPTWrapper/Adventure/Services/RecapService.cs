using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class RecapService
{
    public static string BuildRecapPrompt(AdventureBundle bundle, RecapStyle style)
    {
        var turns = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .TakeLast(style == RecapStyle.Brief ? 8 : 24);

        var body = string.Join("\n\n", turns.Select(t =>
            $"{t.PlayerText}\n{(t.NarratorText ?? "")}"));

        var instruction = style switch
        {
            RecapStyle.Brief => "Write a brief player-facing recap (2-4 sentences).",
            RecapStyle.Detailed => "Write a detailed recap covering major events and open threads.",
            RecapStyle.SpoilerFree => "Write a recap without spoiling unresolved mysteries.",
            RecapStyle.Session => "Write a session recap of the recent play.",
            _ => "Write a recap of recent events.",
        };

        return $"""
            === RECAP JOB ===
            {instruction}
            Output plain recap prose only. Do NOT return JSON, story cards, entities, memories, or markdown fences.

            === TRANSCRIPT ===
            {body}

            === CURRENT SUMMARY ===
            {bundle.Summary.RollingSummary}
            """;
    }

    public static string BuildSummaryUpdatePrompt(AdventureBundle bundle, bool omitRecentTurns = false)
    {
        var memoryIndex = MemoryBaselineService.BuildSinceLastSummaryRevisionBlock(bundle);
        if (omitRecentTurns)
        {
            return "=== STORY DIGEST UPDATE JOB ===\n" +
                   "Update the rolling story summary. Preserve major events, relationships, conflicts, and consequences. Output only the new summary text.\n\n" +
                   $"=== CURRENT DIGEST ===\n{bundle.Summary.RollingSummary}\n\n" +
                   memoryIndex;
        }

        var recent = bundle.Log.Turns
            .Where(t => t.Status == TurnStatus.Accepted)
            .OrderBy(t => t.Index)
            .TakeLast(6);

        var transcript = string.Join("\n", recent.Select(t =>
            $"{t.PlayerText} -> {(t.NarratorText ?? "")}"));

        return "=== STORY DIGEST UPDATE JOB ===\n" +
               "Update the rolling story summary. Preserve major events, relationships, conflicts, and consequences. Output only the new summary text.\n\n" +
               $"=== CURRENT DIGEST ===\n{bundle.Summary.RollingSummary}\n\n" +
               memoryIndex + "\n\n" +
               $"=== RECENT TURNS ===\n{transcript}";
    }
}

internal enum RecapStyle
{
    Brief,
    Detailed,
    SpoilerFree,
    Session,
}

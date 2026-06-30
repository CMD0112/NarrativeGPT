using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Canonical adventure-wide and per-job story-context defaults for AI tools.</summary>
public static class UtilityStoryContextDefaults
{
    public static UtilityStoryContextSettings AdventureBaseline { get; } = new();

    public static IReadOnlyList<AutomationContextJobSpec> AutomationJobs { get; } =
    [
        new(GenerationJobId.ExtractEntities, "Auto-extract entities", HasInterval: false),
        new(GenerationJobId.ProposeMemories, "Auto-propose memories", HasInterval: false),
        new(GenerationJobId.UpdateSummary, "Auto-update summary", HasInterval: true),
        new(GenerationJobId.ContinuityCheck, "Auto-continuity check", HasInterval: false),
    ];

    public static UtilityStoryContextSettings GetJobProfileDefaults(string jobId) =>
        UtilityStoryContextProfiles.ApplyJobProfile(AdventureBaseline.Clone(), jobId);

    public static UtilityStoryContextSettings GetEffective(AdventureBundle bundle, string jobId) =>
        UtilityStoryContextSettingsService.Resolve(bundle, jobId);

    public static UtilityStoryContextSettings GetEditableBase(AdventureBundle bundle, string jobId)
    {
        UtilityStoryContextSettingsService.EnsureDefaults(bundle.Metadata);
        if (UtilityStoryContextSettingsService.HasJobOverride(bundle, jobId))
        {
            var key = GenerationJobHandlers.GetUtilityJobId(jobId);
            return bundle.Metadata.UtilityJobGuideOverrides![key].Context!.Clone();
        }

        return bundle.Metadata.Settings.UtilityStoryContext.Clone();
    }

    public static bool UsesJobOverride(AdventureBundle bundle, string jobId) =>
        UtilityStoryContextSettingsService.HasJobOverride(bundle, jobId);

    public static void ClearJobOverride(AdventureBundle bundle, string jobId) =>
        UtilityStoryContextSettingsService.SetJobOverride(bundle, jobId, null);

    public static void ResetAdventureBaseline(AdventureMetadata metadata)
    {
        UtilityStoryContextSettingsService.EnsureDefaults(metadata);
        metadata.Settings.UtilityStoryContext = AdventureBaseline.Clone();
    }

    public static bool MatchesJobProfileDefaults(UtilityStoryContextSettings settings, string jobId)
    {
        var jobDefaults = GetJobProfileDefaults(jobId);
        return settings.MaxTurnPairs == jobDefaults.MaxTurnPairs
               && settings.LookbackAnchor == jobDefaults.LookbackAnchor
               && settings.MaxContextChars == jobDefaults.MaxContextChars
               && settings.Source == jobDefaults.Source;
    }

    public static string FormatLookbackAnchor(UtilityLookbackAnchor anchor) => anchor switch
    {
        UtilityLookbackAnchor.FromEnd => "Latest turns (from end)",
        UtilityLookbackAnchor.SinceLastAcceptedTurn => "Since last accepted turn",
        UtilityLookbackAnchor.SinceTurnIndex => "Since turn index",
        UtilityLookbackAnchor.AcceptedOnly => "Accepted turns only",
        _ => anchor.ToString(),
    };

    public static string FormatContextWindowSummary(UtilityStoryContextSettings settings) =>
        $"{settings.MaxTurnPairs} turn pair{(settings.MaxTurnPairs == 1 ? "" : "s")}, " +
        $"{FormatLookbackAnchor(settings.LookbackAnchor).ToLowerInvariant()}, " +
        $"{FormatContextCharBudget(settings.MaxContextChars)}";

    public static string FormatContextCharBudget(int maxContextChars) =>
        maxContextChars <= 0 ? "unlimited context" : $"{maxContextChars:N0} char budget";

    public sealed record AutomationContextJobSpec(string JobId, string Label, bool HasInterval);
}

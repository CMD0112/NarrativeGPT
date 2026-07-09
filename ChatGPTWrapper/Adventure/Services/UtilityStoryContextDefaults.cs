using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Canonical adventure-wide and per-job story-context defaults for play utility jobs.</summary>
public static class UtilityStoryContextDefaults
{
    public static UtilityStoryContextSettings AdventureBaseline { get; } = new();

    /// <summary>Post-turn automations in layer order — session/narrative, canon profile, play state, canon evolution.</summary>
    public static IReadOnlyList<AutomationContextJobSpec> AutomationJobs { get; } =
    [
        new(GenerationJobId.UpdateState, "Auto-update session state", HasInterval: false),
        new(GenerationJobId.ProposeMemories, "Auto-propose memories", HasInterval: false),
        new(GenerationJobId.UpdateSummary, "Auto-update story digest", HasInterval: true),
        new(GenerationJobId.ContinuityCheck, "Auto-run continuity check", HasInterval: false),
        new(GenerationJobId.ExtractEntities, "Auto-extract entities", HasInterval: false),
        new(GenerationJobId.ProposeEntityState, "Auto-propose entity state", HasInterval: false),
        new(GenerationJobId.ProposeCanonEvolution, "Auto-propose canon evolution", HasInterval: false),
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

    public static string GetAutomationLayer(string jobId) =>
        GenerationJobGuideService.GetCatalogCategory(jobId);

    public static string FormatLookbackAnchor(UtilityLookbackAnchor anchor) => FormatTranscriptScope(anchor);

    /// <summary>Author-facing label for how a utility job slices play transcript context.</summary>
    public static string FormatTranscriptScope(UtilityLookbackAnchor anchor) => anchor switch
    {
        UtilityLookbackAnchor.FromEnd => "Latest exchanges (from end)",
        UtilityLookbackAnchor.SinceLastAcceptedTurn => "Since last accepted turn",
        UtilityLookbackAnchor.SinceTurnIndex => "From turn index onward",
        UtilityLookbackAnchor.AcceptedOnly => "All accepted turns",
        _ => anchor.ToString(),
    };

    public static string DescribeTranscriptScopeForLayer(string layer, UtilityLookbackAnchor anchor)
    {
        if (anchor == UtilityLookbackAnchor.FromEnd
            && layer is "Canon profile" or "Play state" or "Session")
        {
            return "Latest exchange — narrow scope for post-turn layer updates";
        }

        if (anchor == UtilityLookbackAnchor.FromEnd && layer == "Canon evolution")
            return "Recent exchanges — enough context to justify canon promotion";

        if (anchor == UtilityLookbackAnchor.AcceptedOnly && layer == "Narrative")
            return "Wide accepted transcript — narrative consistency jobs";

        return FormatTranscriptScope(anchor);
    }

    /// <summary>Guidance for packet-include toggles in the job catalog detail panel.</summary>
    public static string DescribePacketIncludesForLayer(string layer) => layer switch
    {
        "Narrative" =>
            "Rolling digest supports summary and continuity jobs. Entity index is usually off unless the job cross-checks referents.",
        "Session" =>
            "Canon entity index helps scene referents. Session snapshot is job output — leave Include state off unless debugging.",
        "Canon profile" =>
            "Entity index helps match creates to existing referents. Keep digest off for narrow latest-exchange focus.",
        "Play state" =>
            "Entity index is required for patch targets. Internal state blocks are proposed output, not packet input.",
        "Canon evolution" =>
            "Entity index plus rolling digest help justify promoting play divergences into canon profile.",
        _ => "Toggle ancillary sections included in the utility job packet alongside scoped transcript.",
    };

    public static string FormatContextWindowSummary(UtilityStoryContextSettings settings) =>
        FormatContextWindowSummary(settings, jobId: null);

    public static string FormatContextWindowSummary(UtilityStoryContextSettings settings, string? jobId)
    {
        var scopeLabel = jobId is null
            ? FormatTranscriptScope(settings.LookbackAnchor).ToLowerInvariant()
            : DescribeTranscriptScopeForLayer(GetAutomationLayer(jobId), settings.LookbackAnchor).ToLowerInvariant();
        return $"{settings.MaxTurnPairs} turn pair{(settings.MaxTurnPairs == 1 ? "" : "s")}, " +
               $"{scopeLabel}, " +
               $"{FormatContextCharBudget(settings.MaxContextChars)}";
    }

    public static string FormatContextCharBudget(int maxContextChars) =>
        maxContextChars <= 0 ? "unlimited context" : $"{maxContextChars:N0} char budget";

    public sealed record AutomationContextJobSpec(string JobId, string Label, bool HasInterval);
}

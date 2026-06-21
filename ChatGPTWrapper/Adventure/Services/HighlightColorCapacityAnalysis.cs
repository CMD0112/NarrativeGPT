using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Summarizes phrase-highlight color usage for cast import and auto-color assignment.
/// </summary>
public sealed class HighlightColorCapacityAnalysis
{
    public int ExistingRuleCount { get; init; }

    public int ExistingDistinctColors { get; init; }

    public int CandidateCount { get; init; }

    public int AlreadyImportedCount { get; init; }

    public int NewCandidateCount { get; init; }

    /// <summary>New cast names that consume an additional distinct color (non-inherited aliases).</summary>
    public int NewDistinctColorsNeeded { get; init; }

    public int PaletteColorCount { get; init; }

    public int ColorsInUseCount { get; init; }

    public int RemainingDistinctPaletteSlots { get; init; }

    public bool PaletteMayBeInsufficient { get; init; }

    public IReadOnlyList<string> ColorsInUse { get; init; } = [];

    public string BuildSummaryLine()
    {
        if (CandidateCount == 0)
            return ExistingRuleCount > 0
                ? $"{ExistingRuleCount} existing rule{(ExistingRuleCount == 1 ? "" : "s")} · {ExistingDistinctColors} color{(ExistingDistinctColors == 1 ? "" : "s")} in use"
                : "No cast names to import";

        var parts = new List<string>();
        if (AlreadyImportedCount > 0)
            parts.Add($"{AlreadyImportedCount} already added");
        if (NewCandidateCount > 0)
            parts.Add($"{NewCandidateCount} new");

        parts.Add(
            $"{ColorsInUseCount} color{(ColorsInUseCount == 1 ? "" : "s")} in use"
            + (PaletteColorCount > 0 ? $" · palette {PaletteColorCount}" : ""));

        if (NewDistinctColorsNeeded > 0)
            parts.Add($"{NewDistinctColorsNeeded} new distinct color{(NewDistinctColorsNeeded == 1 ? "" : "s")} needed");

        if (RemainingDistinctPaletteSlots >= 0 && PaletteColorCount > 0)
            parts.Add($"{RemainingDistinctPaletteSlots} palette slot{(RemainingDistinctPaletteSlots == 1 ? "" : "s")} free");

        if (PaletteMayBeInsufficient)
            parts.Add("palette may be tight — some colors may be nudged");

        return string.Join(" · ", parts);
    }
}

internal static class HighlightColorCapacityAnalyzer
{
    public static HighlightColorCapacityAnalysis Analyze(
        IReadOnlyList<PhraseHighlightRule>? existingRules,
        IReadOnlyList<CastPhraseImportCandidate> candidates,
        HighlightColorAssignmentOptions options,
        IReadOnlyList<string> palette)
    {
        existingRules ??= [];
        var existingColors = CollectDistinctColors(existingRules);
        var colorsInUse = new HashSet<string>(existingColors, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates.Where(c => !c.AlreadyExists))
            colorsInUse.Add(candidate.Color);

        var alreadyImported = candidates.Count(c => c.AlreadyExists);
        var newCandidates = candidates.Count - alreadyImported;
        var newDistinctNeeded = CountNewDistinctColorsNeeded(candidates, options);

        var paletteCount = palette.Count;
        var remainingSlots = paletteCount > 0
            ? Math.Max(0, paletteCount - colorsInUse.Count)
            : 0;

        var insufficient = options.AvoidDuplicateColors
                           && paletteCount > 0
                           && newDistinctNeeded > remainingSlots;

        return new HighlightColorCapacityAnalysis
        {
            ExistingRuleCount = existingRules.Count,
            ExistingDistinctColors = existingColors.Count,
            CandidateCount = candidates.Count,
            AlreadyImportedCount = alreadyImported,
            NewCandidateCount = newCandidates,
            NewDistinctColorsNeeded = newDistinctNeeded,
            PaletteColorCount = paletteCount,
            ColorsInUseCount = colorsInUse.Count,
            RemainingDistinctPaletteSlots = remainingSlots,
            PaletteMayBeInsufficient = insufficient,
            ColorsInUse = colorsInUse.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    internal static Dictionary<string, PhraseHighlightRule> IndexExistingRules(
        IEnumerable<PhraseHighlightRule>? existingRules)
    {
        var map = new Dictionary<string, PhraseHighlightRule>(StringComparer.OrdinalIgnoreCase);
        if (existingRules is null)
            return map;

        foreach (var rule in existingRules)
        {
            var phrase = rule.Phrase?.Trim();
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            map.TryAdd(phrase, rule);
        }

        return map;
    }

    internal static void SeedFromExistingRules(
        IEnumerable<PhraseHighlightRule>? existingRules,
        ISet<string> usedColors,
        IDictionary<string, string> characterColors)
    {
        if (existingRules is null)
            return;

        foreach (var rule in existingRules)
        {
            var phrase = rule.Phrase?.Trim();
            if (string.IsNullOrWhiteSpace(phrase))
                continue;

            var color = NormalizeColor(rule.Color);
            if (!string.IsNullOrWhiteSpace(color))
            {
                usedColors.Add(color);
                characterColors.TryAdd(phrase, color);
            }
        }
    }

    private static int CountNewDistinctColorsNeeded(
        IReadOnlyList<CastPhraseImportCandidate> candidates,
        HighlightColorAssignmentOptions options)
    {
        var needed = 0;
        var assignedCharacterColors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (candidate.AlreadyExists)
                continue;

            if (TryParseAliasParent(candidate.Role, out var parentName)
                && options.AliasColorMode != HighlightAliasColorMode.Distinct
                && assignedCharacterColors.ContainsKey(parentName))
            {
                continue;
            }

            if (TryParseAliasParent(candidate.Role, out parentName)
                && options.AliasColorMode != HighlightAliasColorMode.Distinct)
            {
                assignedCharacterColors[parentName] = candidate.Color;
            }
            else if (!TryParseAliasParent(candidate.Role, out _))
            {
                assignedCharacterColors[candidate.Phrase] = candidate.Color;
            }

            needed++;
        }

        return needed;
    }

    private static HashSet<string> CollectDistinctColors(IEnumerable<PhraseHighlightRule> rules)
    {
        var colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in rules)
        {
            var color = NormalizeColor(rule.Color);
            if (!string.IsNullOrWhiteSpace(color))
                colors.Add(color);
        }

        return colors;
    }

    private static string? NormalizeColor(string? color)
    {
        var trimmed = color?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static bool TryParseAliasParent(string role, out string parentName)
    {
        parentName = "";
        const string prefix = "Alias · ";
        if (!role.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        parentName = role[prefix.Length..].Trim();
        return !string.IsNullOrWhiteSpace(parentName);
    }
}

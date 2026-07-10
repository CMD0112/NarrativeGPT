using ChatGPTWrapper;

namespace ChatGPTWrapper.Format;

public static class FormatRefinementSuggester
{
    private const double MaxComfortableWidthRem = 48;
    private const int MaxSuggestionsPerCategory = 5;
    private const int MaxGlobalSuggestions = 8;

    public static IReadOnlyList<FormatRefinementSuggestion> GetSuggestedActions(
        ContinuousViewFormatSettings format,
        FormatRefinementContext context,
        FormatRefinementCategory? category = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(context);

        var candidates = new List<FormatRefinementSuggestion>();

        AddAnalyzerMappedSuggestions(candidates, format, context);
        AddPredicateSuggestions(candidates, format, context);

        var deduped = candidates
            .GroupBy(s => s.Action.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(s => s.Severity).First())
            .Where(s => !FormatRefinementCatalog.IsSatisfied(s.Action, format, context))
            .OrderByDescending(s => s.Severity)
            .ThenBy(s => s.Action.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (category is not null)
        {
            return deduped
                .Where(s => s.Action.Category == category)
                .Take(MaxSuggestionsPerCategory)
                .ToList();
        }

        return deduped.Take(MaxGlobalSuggestions).ToList();
    }

    public static IReadOnlyList<FormatRefinementSuggestion> GetSuggestedForCategory(
        ContinuousViewFormatSettings format,
        FormatRefinementContext context,
        FormatRefinementCategory category) =>
        GetSuggestedActions(format, context, category);

    private static void AddAnalyzerMappedSuggestions(
        List<FormatRefinementSuggestion> candidates,
        ContinuousViewFormatSettings format,
        FormatRefinementContext context)
    {
        var warnings = FormatReadabilityAnalyzer.Analyze(
            format,
            context.PhraseHighlightRules,
            context.PhraseHighlightsEnabled);

        foreach (var warning in warnings)
        {
            var actionId = MapWarningToActionId(warning, format, context);
            if (actionId is null)
                continue;

            var action = FormatRefinementCatalog.Find(actionId);
            if (action is null)
                continue;

            candidates.Add(new FormatRefinementSuggestion(action, warning.Severity, warning.Message));
        }
    }

    private static string? MapWarningToActionId(
        FormatReadabilityWarning warning,
        ContinuousViewFormatSettings format,
        FormatRefinementContext context)
    {
        if (warning.SettingKey == FormatSettingKeys.ContentMaxWidthRem)
        {
            if (format.ContentMaxWidthRem > MaxComfortableWidthRem)
                return "layout-comfortable-width";
            if (format.ContentMaxWidthRem is < 10 or > 90)
                return format.ContentMaxWidthRem < 10 ? "layout-comfortable-width" : "layout-narrow-column";
        }

        if (warning.SettingKey == FormatSettingKeys.UserTextColor)
        {
            if (context.PhraseHighlightsEnabled)
                return "color-brighten-assistant-text";
            return "color-brighten-user-text";
        }

        if (warning.SettingKey == FormatSettingKeys.AssistantTextColor)
            return "color-brighten-assistant-text";

        if (warning.SettingKey == FormatSettingKeys.AssistantFontSizeRem)
            return format.AssistantFontSizeRem < 0.6 ? "type-larger-assistant" : "type-larger-assistant";

        if (warning.SettingKey == FormatSettingKeys.AssistantLineHeight)
        {
            if (format.AssistantLineHeight <= 0)
                return "type-open-assistant-spacing";
            return "type-open-assistant-spacing";
        }

        if (warning.SettingKey == FormatSettingKeys.ComposerClearanceMinPx)
            return null;

        return null;
    }

    private static void AddPredicateSuggestions(
        List<FormatRefinementSuggestion> candidates,
        ContinuousViewFormatSettings format,
        FormatRefinementContext context)
    {
        if (format.ContentMaxWidthRem > MaxComfortableWidthRem)
            TryAdd(candidates, "layout-comfortable-width", FormatReadabilitySeverity.Info);

        if (format.AssistantLineHeight < 1.55)
            TryAdd(candidates, "type-open-assistant-spacing", FormatReadabilitySeverity.Info);

        if (format.UserLineHeight < 1.52)
            TryAdd(candidates, "type-open-user-spacing", FormatReadabilitySeverity.Info);

        if (format.SegmentDividerOpacity > 20 && format.ShowSegmentDividers)
            TryAdd(candidates, "layout-softer-dividers", FormatReadabilitySeverity.Info);

        if (format.UserBackgroundOpacity > 8 || format.AssistantBackgroundOpacity > 6)
            TryAdd(candidates, "color-reduce-background-tints", FormatReadabilitySeverity.Info);

        if (context.TranscriptViewMode == TranscriptViewMode.Weave)
        {
            if (format.WeaveEmbedKind != WeaveEmbedKind.Aside)
                TryAdd(candidates, "weave-aside-embeds", FormatReadabilitySeverity.Info);

            if (format.WeaveEmbedMarginBlockRem < 0.9)
                TryAdd(candidates, "weave-flowing-margins", FormatReadabilitySeverity.Info);

            if (format.ShowSegmentDividers)
                TryAdd(candidates, "weave-hide-dividers", FormatReadabilitySeverity.Info);
        }

    }

    private static void TryAdd(
        List<FormatRefinementSuggestion> candidates,
        string actionId,
        FormatReadabilitySeverity severity)
    {
        var action = FormatRefinementCatalog.Find(actionId);
        if (action is null)
            return;

        candidates.Add(new FormatRefinementSuggestion(action, severity));
    }
}

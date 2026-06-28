using ChatGPTWrapper;

namespace ChatGPTWrapper.Format;

public enum FormatRefinementCategory
{
    Layout,
    Typography,
    Colors,
    RoleDistinction,
    CodeHeadings,
    Weave,
}

public sealed class FormatRefinementContext
{
    public TranscriptViewMode TranscriptViewMode { get; init; } = TranscriptViewMode.Continuous;

    public bool PhraseHighlightsEnabled { get; init; }

    public IReadOnlyList<PhraseHighlightRule>? PhraseHighlightRules { get; init; }
}

public sealed record FormatRefinementAction(
    string Id,
    string Label,
    string Description,
    FormatRefinementCategory Category,
    string PrimarySettingKey,
    Action<ContinuousViewFormatSettings> Apply,
    Func<ContinuousViewFormatSettings, FormatRefinementContext, bool>? IsSatisfied = null);

public sealed record FormatRefinementSuggestion(
    FormatRefinementAction Action,
    FormatReadabilitySeverity Severity,
    string? Detail = null);

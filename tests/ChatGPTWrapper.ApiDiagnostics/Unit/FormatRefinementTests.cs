using ChatGPTWrapper;
using ChatGPTWrapper.Format;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class FormatRefinementTests
{
    [Fact]
    public void Each_catalog_action_applies_without_error()
    {
        foreach (var action in FormatRefinementCatalog.All)
        {
            var format = ContinuousViewFormatSettings.CreateDefaults();
            Assert.True(FormatRefinementCatalog.TryApply(action.Id, format));
        }
    }

    [Theory]
    [InlineData(FormatRefinementCategory.Layout)]
    [InlineData(FormatRefinementCategory.Typography)]
    [InlineData(FormatRefinementCategory.Colors)]
    [InlineData(FormatRefinementCategory.RoleDistinction)]
    [InlineData(FormatRefinementCategory.CodeHeadings)]
    [InlineData(FormatRefinementCategory.Weave)]
    public void Each_category_has_at_least_eight_common_actions(FormatRefinementCategory category)
    {
        var actions = FormatRefinementCatalog.GetCommonActions(category);
        Assert.True(actions.Count >= 8, $"{category} has {actions.Count} actions");
    }

    [Fact]
    public void Layout_comfortable_width_is_incremental()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserFontSizeRem = 1.25;
        format.AssistantAccentColor = "#FF0000";

        Assert.True(FormatRefinementCatalog.TryApply("layout-comfortable-width", format));

        Assert.Equal(44, format.ContentMaxWidthRem);
        Assert.Equal(1.25, format.UserFontSizeRem);
        Assert.Equal("#FF0000", format.AssistantAccentColor);
    }

    [Fact]
    public void Suggester_emits_width_suggestion_for_wide_column()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 60;
        var context = new FormatRefinementContext();

        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(
            format,
            context,
            FormatRefinementCategory.Layout);

        Assert.Contains(suggested, s => s.Action.Id == "layout-comfortable-width");
    }

    [Fact]
    public void Suggester_emits_contrast_suggestion_for_clashing_assistant_colors()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.AssistantTextColor = "#1A1A1C";
        format.AssistantBackgroundColor = "#161618";
        format.AssistantBackgroundOpacity = 100;
        var context = new FormatRefinementContext();

        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(
            format,
            context,
            FormatRefinementCategory.Colors);

        Assert.Contains(suggested, s => s.Action.Id == "color-brighten-assistant-text");
    }

    [Fact]
    public void Suggester_emits_spacing_suggestion_for_tight_assistant_lines()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.AssistantLineHeight = 1.4;
        var context = new FormatRefinementContext();

        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(
            format,
            context,
            FormatRefinementCategory.Typography);

        Assert.Contains(suggested, s => s.Action.Id == "type-open-assistant-spacing");
    }

    [Fact]
    public void Comfortable_width_suggestion_hidden_when_already_narrow()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.ContentMaxWidthRem = 42;
        var context = new FormatRefinementContext();

        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(
            format,
            context,
            FormatRefinementCategory.Layout);

        Assert.DoesNotContain(suggested, s => s.Action.Id == "layout-comfortable-width");
    }

    [Fact]
    public void Weave_mode_suggests_aside_embeds_when_not_configured()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.WeaveEmbedKind = WeaveEmbedKind.Blockquote;
        var context = new FormatRefinementContext { TranscriptViewMode = TranscriptViewMode.Weave };

        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(
            format,
            context,
            FormatRefinementCategory.Weave);

        Assert.Contains(suggested, s => s.Action.Id == "weave-aside-embeds");
    }

    [Fact]
    public void Hyperlegible_refinement_sets_custom_font_stack()
    {
        var format = ContinuousViewFormatSettings.CreateDefaults();
        Assert.True(FormatRefinementCatalog.TryApply("type-hyperlegible", format));
        Assert.Contains("Hyperlegible", format.AssistantFontFamily, StringComparison.Ordinal);
    }
}

using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightColorAssignmentTests
{
    [Fact]
    public void ReassignRuleColors_selected_changes_only_target_phrases()
    {
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara", Color = "#111111", EntityCategory = "Characters" },
            new() { Phrase = "Kael", Color = "#222222", EntityCategory = "Characters" },
            new() { Phrase = "Bram", Color = "#333333", EntityCategory = "Characters" },
        };

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.SequentialSpectrum);

        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            rules,
            options,
            theme,
            canvas,
            PhraseHighlightReassignScope.Selected,
            [rules[1]],
            assignmentSalt: 3);

        Assert.False(string.Equals("#222222", rules[1].Color, StringComparison.OrdinalIgnoreCase));
        Assert.Equal("#111111", rules[0].Color, ignoreCase: true);
        Assert.Equal("#333333", rules[2].Color, ignoreCase: true);
    }

    [Fact]
    public void ReassignRuleColors_salt_changes_output()
    {
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara", Color = "#111111" },
            new() { Phrase = "Kael", Color = "#222222" },
        };

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        var first = rules.Select(r => r.Clone()).ToList();
        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            first, options, theme, canvas, PhraseHighlightReassignScope.All, assignmentSalt: 1);

        var second = rules.Select(r => r.Clone()).ToList();
        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            second, options, theme, canvas, PhraseHighlightReassignScope.All, assignmentSalt: 2);

        Assert.False(string.Equals(first[0].Color, second[0].Color, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReassignRuleColors_salt_changes_output_with_grouping_profile()
    {
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", Color = "#111111", EntityId = Guid.NewGuid(), EntityCategory = "Characters" },
            new() { Phrase = "Bram Rusk", Color = "#222222", EntityId = Guid.NewGuid(), EntityCategory = "Characters" },
            new() { Phrase = "Harbor", Color = "#333333", EntityId = Guid.NewGuid(), EntityCategory = "Locations" },
        };

        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctWorldShared)
            .Clone();
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        var first = rules.Select(r => r.Clone()).ToList();
        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            first, options, theme, canvas, PhraseHighlightReassignScope.All, assignmentSalt: 1, groupingProfile: profile);

        var second = rules.Select(r => r.Clone()).ToList();
        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            second, options, theme, canvas, PhraseHighlightReassignScope.All, assignmentSalt: 4, groupingProfile: profile);

        Assert.False(string.Equals(first[0].Color, second[0].Color, StringComparison.OrdinalIgnoreCase));
        Assert.False(string.Equals(first[2].Color, second[2].Color, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReassignRuleColors_shared_group_reroll_selected_expands_to_all_peers()
    {
        var harborId = Guid.NewGuid();
        var forestId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Harbor", Color = "#AABBCC", EntityId = harborId, EntityCategory = "Locations" },
            new() { Phrase = "Forest", Color = "#AABBCC", EntityId = forestId, EntityCategory = "Locations" },
        };

        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared)
            .Clone();
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            rules,
            options,
            theme,
            canvas,
            PhraseHighlightReassignScope.Selected,
            [rules[0]],
            assignmentSalt: 3,
            groupingProfile: profile);

        Assert.False(string.Equals("#AABBCC", rules[0].Color, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(rules[0].Color, rules[1].Color, ignoreCase: true);
    }

    [Fact]
    public void ReassignRuleColors_selected_alias_sync_group_updates_linked_rows()
    {
        var entityId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", Color = "#111111", EntityId = entityId, EntityCategory = "Characters" },
            new()
            {
                Phrase = "Mara",
                Color = "#111111",
                EntityId = entityId,
                EntityCategory = "Characters",
                SyncWithPhrase = "Mara Holt",
            },
        };

        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);

        PhraseHighlightColorAssignmentService.ReassignRuleColors(
            rules,
            options,
            theme,
            canvas,
            PhraseHighlightReassignScope.Selected,
            [rules[1]],
            assignmentSalt: 2);

        Assert.False(string.Equals("#111111", rules[1].Color, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(rules[1].Color, rules[0].Color, ignoreCase: true);
    }

    [Fact]
    public void AssignColor_avoids_user_and_narrator_body_text_colors()
    {
        var theme = ThemeApplicationService.ResolveEffectiveTheme(ThemeApplicationService.CreateDefaultSettings());
        var canvas = theme.GetHex("BgBase");
        var format = ContinuousViewFormatSettings.CreateDefaults();
        format.UserTextColor = "#FFFFFF";
        format.AssistantTextColor = "#ECE8E2";
        var reserved = HighlightColorReservedColors.Resolve(theme, format);
        var options = HighlightColorProfileLibrary.OptionsForBuiltIn(HighlightColorProfileIds.ThemeHarmony);
        var palette = HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas, minimumDistinctColors: 12, reserved);

        foreach (var color in palette)
        {
            Assert.False(HighlightColorReservedColors.Conflicts(color, reserved));
        }

        var used = new HashSet<string>(reserved, StringComparer.OrdinalIgnoreCase);
        var assigned = CastHighlightColorAssignment.AssignColor(
            options,
            "Character",
            "Mara Holt",
            palette,
            canvas,
            new Dictionary<string, string>(),
            used,
            discoveryIndex: 0,
            theme,
            reserved);

        Assert.False(HighlightColorReservedColors.Conflicts(assigned, reserved));
    }

    [Fact]
    public void InferAssignmentRole_detects_alias_by_prefix()
    {
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara", EntityCategory = "Characters" },
            new() { Phrase = "Mar" },
        };

        var role = PhraseHighlightColorAssignmentService.InferAssignmentRole(rules[1], rules);
        Assert.Equal("Alias · Mara", role);
    }
}

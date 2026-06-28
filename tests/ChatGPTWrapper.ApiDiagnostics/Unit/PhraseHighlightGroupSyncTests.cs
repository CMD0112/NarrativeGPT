using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightGroupSyncTests
{
    [Fact]
    public void PropagateGroupStyleSync_updates_shared_location_peers()
    {
        var harborId = Guid.NewGuid();
        var forestId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Harbor", Color = "#111111", EntityId = harborId, EntityCategory = "Locations" },
            new() { Phrase = "Forest", Color = "#222222", EntityId = forestId, EntityCategory = "Locations" },
        };

        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared)
            .Clone();

        PhraseHighlightGroupSyncService.PropagateGroupStyleSync(rules, rules[0], profile);

        Assert.Equal("#111111", rules[1].Color, ignoreCase: true);
    }

    [Fact]
    public void PropagateGroupStyleSync_updates_extended_typography_on_shared_peers()
    {
        var harborId = Guid.NewGuid();
        var forestId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Harbor", Color = "#111111", EntityId = harborId, EntityCategory = "Locations" },
            new() { Phrase = "Forest", Color = "#222222", EntityId = forestId, EntityCategory = "Locations" },
        };

        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared)
            .Clone();

        rules[0].FontWeight = 700;
        rules[0].Italic = true;
        rules[0].TextTransform = "uppercase";
        rules[0].Opacity = 0.85;
        PhraseHighlightGroupSyncService.PropagateGroupStyleSync(rules, rules[0], profile);

        Assert.Equal(700, rules[1].FontWeight);
        Assert.True(rules[1].Italic);
        Assert.Equal("uppercase", rules[1].TextTransform);
        Assert.Equal(0.85, rules[1].Opacity);
    }

    [Fact]
    public void PropagateGroupStyleSync_skips_group_override_peers()
    {
        var harborId = Guid.NewGuid();
        var forestId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Harbor", Color = "#111111", EntityId = harborId, EntityCategory = "Locations" },
            new() { Phrase = "Forest", Color = "#222222", EntityId = forestId, EntityCategory = "Locations", GroupOverride = true },
        };

        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared)
            .Clone();

        PhraseHighlightGroupSyncService.PropagateGroupStyleSync(rules, rules[0], profile);

        Assert.Equal("#222222", rules[1].Color, ignoreCase: true);
    }

    [Fact]
    public void FormatGroupSummary_marks_shared_and_override()
    {
        var display = new PhraseHighlightGroupDisplay
        {
            IsGroupingActive = true,
            GroupName = "Locations",
            ShareColorWithinGroup = true,
        };

        Assert.Equal("Locations · shared", PhraseHighlightGroupSyncService.FormatGroupSummary(display, groupOverride: false));
        Assert.Equal("Locations · override", PhraseHighlightGroupSyncService.FormatGroupSummary(display, groupOverride: true));
    }

    [Fact]
    public void ReconcileSharedGroupColors_unifies_mismatched_shared_group()
    {
        var harborId = Guid.NewGuid();
        var forestId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Harbor", Color = "#AABBCC", EntityId = harborId, EntityCategory = "Locations" },
            new() { Phrase = "Forest", Color = "#112233", EntityId = forestId, EntityCategory = "Locations" },
        };

        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.CastDistinctLocationsShared)
            .Clone();

        PhraseHighlightGroupSyncService.ReconcileSharedGroupColors(rules, profile);

        Assert.Equal(rules[0].Color, rules[1].Color, ignoreCase: true);
    }
}

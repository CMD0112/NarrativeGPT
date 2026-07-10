using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class PhraseHighlightRuleListArrangementTests
{
    private static readonly Guid EntityId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ResolvePrimaryFamilyKey_uses_sync_target_for_alias()
    {
        var rules = new List<PhraseHighlightRule>
        {
            Primary("Mara Holt"),
            Alias("Mara", "Mara Holt"),
        };

        var key = PhraseHighlightRuleListArrangement.ResolvePrimaryFamilyKey(rules[1], rules);
        Assert.Equal("Mara Holt", key);
    }

    [Fact]
    public void ResolvePrimaryFamilyKey_uses_phrase_for_unlinked_rule()
    {
        var rules = new List<PhraseHighlightRule> { Rule("Standalone") };

        var key = PhraseHighlightRuleListArrangement.ResolvePrimaryFamilyKey(rules[0], rules);
        Assert.Equal("Standalone", key);
    }

    [Fact]
    public void ResolveStyleSyncGroup_includes_primary_and_aliases()
    {
        var rules = new List<PhraseHighlightRule>
        {
            Primary("Mara Holt"),
            Alias("Mara", "Mara Holt"),
            Alias("MH", "Mara Holt"),
        };

        var group = PhraseHighlightRuleListArrangement.ResolveStyleSyncGroup(rules, rules[1]);
        Assert.Equal(3, group.Count);
        Assert.Contains(group, r => r.Phrase == "Mara Holt");
        Assert.Contains(group, r => r.Phrase == "Mara");
        Assert.Contains(group, r => r.Phrase == "MH");
    }

    [Fact]
    public void HasExpandableStyleSyncGroup_false_for_override_rule()
    {
        var rules = new List<PhraseHighlightRule>
        {
            Primary("Mara Holt"),
            new()
            {
                Phrase = "Mara",
                EntityId = EntityId,
                EntityCategory = "Characters",
                SyncWithPhrase = "Mara Holt",
                SyncOverride = true,
            },
        };

        Assert.False(PhraseHighlightRuleListArrangement.HasExpandableStyleSyncGroup(rules, rules[1]));
    }

    [Fact]
    public void DescribeLinkType_maps_roles()
    {
        Assert.Equal("Primary", PhraseHighlightRuleListArrangement.DescribeLinkType(Primary("A")));
        Assert.Equal("Alias", PhraseHighlightRuleListArrangement.DescribeLinkType(Alias("B", "A")));
        Assert.Equal("Override", PhraseHighlightRuleListArrangement.DescribeLinkType(new()
        {
            Phrase = "B",
            SyncOverride = true,
        }));
        Assert.Equal("Unlinked", PhraseHighlightRuleListArrangement.DescribeLinkType(Rule("C")));
    }

    [Fact]
    public void ResolveMetadata_populates_group_labels_from_profile()
    {
        var rules = new List<PhraseHighlightRule> { Primary("Mara Holt") };
        var profile = HighlightColorGroupingProfileLibrary.BuiltInProfiles
            .First(p => p.Id == HighlightColorGroupingProfileIds.ByEntityCategory);

        var metadata = PhraseHighlightRuleListArrangement.ResolveMetadata(rules[0], rules, profile);

        Assert.Equal("Mara Holt", metadata.PrimaryFamilyKey);
        Assert.Equal(0, metadata.LinkTypeSortRank);
        Assert.Equal("Primary", metadata.LinkTypeGroupKey);
        Assert.NotEqual("—", metadata.EntityTypeSortKey);
    }

    private static PhraseHighlightRule Rule(string phrase) =>
        new() { Phrase = phrase, Color = "#FFD166" };

    private static PhraseHighlightRule Primary(string phrase) =>
        new()
        {
            Phrase = phrase,
            Color = "#FFD166",
            EntityId = EntityId,
            EntityCategory = "Characters",
        };

    private static PhraseHighlightRule Alias(string phrase, string primary) =>
        new()
        {
            Phrase = phrase,
            Color = "#FFD166",
            EntityId = EntityId,
            EntityCategory = "Characters",
            SyncWithPhrase = primary,
        };
}

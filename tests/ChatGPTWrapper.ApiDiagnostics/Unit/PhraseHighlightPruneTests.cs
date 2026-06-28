using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightPruneTests
{
    [Fact]
    public void PruneAmbiguousRules_removes_unlinked_aliases_and_epithets()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var bramId = Guid.Parse("4f2ebe84-f4cd-48e3-b834-9afe44123e51");

        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8" },
            new() { Phrase = "Bram Rusk", EntityId = bramId, EntityCategory = "Characters", Color = "#00FF88" },
            new() { Phrase = "Bram", Color = "#00FF88" },
            new() { Phrase = "Mara", Color = "#10DAD8" },
            new() { Phrase = "the boy left behind", Color = "#A1FFFF" },
            new() { Phrase = "Crown captain", Color = "#5BFB9E" },
            new() { Phrase = "Captain Orlan", Color = "#F42C7F" },
        };

        var report = PhraseHighlightRuleService.PruneAmbiguousRules(rules);

        Assert.Equal(5, report.RemovedCount);
        Assert.Contains("Bram", report.RemovedPhrases);
        Assert.Contains("Mara", report.RemovedPhrases);
        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void AlignRulesToEntityCardAliases_removes_derived_alias_not_on_entity_card()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8", Enabled = true },
            new() { Phrase = "Mara", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8", SyncWithPhrase = "Mara Holt" },
        };

        var bundle = AdventureDesignService.CreateDesigningAdventure("Align alias prune");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = holtId, Name = "Mara Holt", Aliases = [] });

        var catalog = EntityAliasCatalog.BuildFromBundle(bundle);
        var report = PhraseHighlightRuleService.AlignRulesToEntityCardAliases(rules, catalog);

        Assert.Contains("Mara", report.RemovedPhrases);
        Assert.Single(rules);
        Assert.Equal("Mara Holt", rules[0].Phrase);
    }

    [Fact]
    public void AlignRulesToEntityCardAliases_keeps_entity_card_alias_and_syncs_rule()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8", Enabled = true },
        };

        var bundle = AdventureDesignService.CreateDesigningAdventure("Align alias sync");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = holtId, Name = "Mara Holt", Aliases = ["Mara"] });

        var catalog = EntityAliasCatalog.BuildFromBundle(bundle);
        var report = PhraseHighlightRuleService.AlignRulesToEntityCardAliases(rules, catalog);

        Assert.Contains("Mara", report.AddedPhrases);
        var alias = Assert.Single(rules, r => r.Phrase == "Mara");
        Assert.Equal(holtId, alias.EntityId);
        Assert.Equal("Mara Holt", alias.SyncWithPhrase);
    }

    [Fact]
    public void PropagateStyleSync_updates_linked_alias_when_primary_changes()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8" },
            new() { Phrase = "Mara", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8", SyncWithPhrase = "Mara Holt" },
        };

        rules[0].Color = "#FF0000";
        PhraseHighlightRuleService.PropagateStyleSync(rules, rules[0]);

        Assert.Equal("#FF0000", rules[1].Color, ignoreCase: true);
    }

    [Fact]
    public void PropagateStyleSync_updates_extended_typography_on_linked_alias()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8" },
            new() { Phrase = "Mara", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8", SyncWithPhrase = "Mara Holt" },
        };

        rules[0].FontWeight = 650;
        rules[0].Underline = true;
        rules[0].FontSizeScale = 1.2;
        rules[0].LetterSpacingEm = 0.05;
        rules[0].BorderWidthPx = 2;
        PhraseHighlightRuleService.PropagateStyleSync(rules, rules[0]);

        Assert.Equal(650, rules[1].FontWeight);
        Assert.True(rules[1].Underline);
        Assert.Equal(1.2, rules[1].FontSizeScale);
        Assert.Equal(0.05, rules[1].LetterSpacingEm);
        Assert.Equal(2, rules[1].BorderWidthPx);
    }

    [Fact]
    public void PropagateStyleSync_respects_sync_override()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8" },
            new()
            {
                Phrase = "Mara",
                EntityId = holtId,
                EntityCategory = "Characters",
                Color = "#10DAD8",
                SyncWithPhrase = "Mara Holt",
                SyncOverride = true,
            },
        };

        rules[0].Color = "#FF0000";
        PhraseHighlightRuleService.PropagateStyleSync(rules, rules[0]);

        Assert.Equal("#10DAD8", rules[1].Color, ignoreCase: true);
    }

    [Fact]
    public void InferAliasLinkages_backfills_sync_with_phrase_for_entity_alias_rules()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8" },
            new() { Phrase = "Mara", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8" },
        };

        PhraseHighlightRuleService.InferAliasLinkages(rules);

        Assert.Equal("Mara Holt", rules[1].SyncWithPhrase);
    }

    [Fact]
    public void Maintenance_prune_then_align_does_not_restore_unlisted_first_name()
    {
        var holtId = Guid.Parse("8ffa1187-746d-4ca2-8ef0-86d4b76b6f26");
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Mara Holt", EntityId = holtId, EntityCategory = "Characters", Color = "#10DAD8", Enabled = true },
            new() { Phrase = "Mara", Color = "#10DAD8" },
        };

        PhraseHighlightRuleService.PruneAmbiguousRules(rules);

        var bundle = AdventureDesignService.CreateDesigningAdventure("Maintenance align");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = holtId, Name = "Mara Holt", Aliases = [] });
        var catalog = EntityAliasCatalog.BuildFromBundle(bundle);
        PhraseHighlightRuleService.AlignRulesToEntityCardAliases(rules, catalog);

        Assert.Single(rules);
        Assert.Equal("Mara Holt", rules[0].Phrase);
    }

    [Fact]
    public void SyncEntityAliasHighlightRules_adds_linked_rules_for_entity_aliases()
    {
        var entityId = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "Anwen Holt",
                EntityId = entityId,
                EntityCategory = "Characters",
                Color = "#10DAD8",
                Enabled = true,
            },
        };

        var report = PhraseHighlightRuleService.SyncEntityAliasHighlightRules(
            rules,
            "Characters",
            entityId,
            "Anwen Holt",
            ["Nessa"]);

        Assert.Single(report.AddedPhrases);
        Assert.Equal("Nessa", report.AddedPhrases[0]);
        var aliasRule = Assert.Single(rules, r => r.Phrase == "Nessa");
        Assert.Equal(entityId, aliasRule.EntityId);
        Assert.Equal("#10DAD8", aliasRule.Color, ignoreCase: true);
        Assert.Equal("Anwen Holt", aliasRule.SyncWithPhrase);
    }

    [Fact]
    public void PruneAmbiguousRules_keeps_entity_linked_proper_names()
    {
        var rules = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "Garran Holt",
                EntityId = Guid.Parse("00000000-0000-0000-0000-000000000000"),
                EntityCategory = "Player",
                Color = "#C45CFF",
            },
            new() { Phrase = "Anwen", EntityId = Guid.NewGuid(), EntityCategory = "Party", Color = "#FF5500" },
        };

        var report = PhraseHighlightRuleService.PruneAmbiguousRules(rules);

        Assert.Equal(0, report.RemovedCount);
        Assert.Equal(2, rules.Count);
    }
}

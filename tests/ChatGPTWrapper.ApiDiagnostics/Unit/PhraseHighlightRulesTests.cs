using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightRulesTests
{
    private static readonly JsonSerializerOptions RulesJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Fact]
    public void PhraseHighlightRules_json_roundtrip_preserves_75_rules()
    {
        var rules = Enumerable.Range(0, 75)
            .Select(i => new PhraseHighlightRule
            {
                Phrase = $"Name{i}",
                Color = "#FFD166",
                Enabled = i % 3 != 0,
            })
            .ToList();

        var json = JsonSerializer.Serialize(rules, RulesJsonOptions);
        var parsed = JsonSerializer.Deserialize<List<PhraseHighlightRule>>(json, RulesJsonOptions);

        Assert.NotNull(parsed);
        Assert.Equal(75, parsed.Count);
        Assert.Equal("Name60", parsed[60].Phrase);
        Assert.False(parsed[60].Enabled);
    }

    [Fact]
    public void RenameReconciliationService_updates_phrase_highlight_beyond_index_49()
    {
        var rules = Enumerable.Range(0, 60)
            .Select(i => new PhraseHighlightRule
            {
                Phrase = i == 55 ? "OldName" : $"Rule{i}",
                Color = "#FFD166",
            })
            .ToList();

        var bundle = AdventureDesignService.CreateDesigningAdventure("Rename highlight test");
        var id = Guid.NewGuid();
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "OldName", Description = "Test." });

        var context = new CanonEditContext
        {
            Category = "Characters",
            EntityId = id,
            PriorName = "OldName",
            NewName = "NewName",
        };
        var report = CanonReconciliationService.DetectDrift(bundle, context);

        RenameReconciliationService.Apply(
            bundle,
            context,
            report,
            new RenameReconciliationOptions { UpdatePhraseHighlights = true },
            rules);

        Assert.Equal("NewName", rules[55].Phrase);
        Assert.Equal("Rule54", rules[54].Phrase);
    }

    [Fact]
    public void PhraseHighlightRules_json_roundtrip_preserves_entity_linkage()
    {
        var id = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "Mara",
                Color = "#FFD166",
                EntityId = id,
                EntityCategory = "Characters",
            },
        };

        var json = JsonSerializer.Serialize(rules, RulesJsonOptions);
        var parsed = JsonSerializer.Deserialize<List<PhraseHighlightRule>>(json, RulesJsonOptions);

        Assert.NotNull(parsed);
        Assert.Equal(id, parsed[0].EntityId);
        Assert.Equal("Characters", parsed[0].EntityCategory);
    }

    [Fact]
    public void RenameReconciliationService_updates_linked_rule_by_entity_id_when_phrase_differs()
    {
        var id = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "OldAliasOnly",
                Color = "#FFD166",
                EntityId = id,
                EntityCategory = "Characters",
            },
        };

        var bundle = AdventureDesignService.CreateDesigningAdventure("Entity-linked rename");
        bundle.Entities.Characters.Add(new CharacterEntry { Id = id, Name = "NewName", Description = "Test." });

        var context = new CanonEditContext
        {
            Category = "Characters",
            EntityId = id,
            PriorName = "OldName",
            NewName = "NewName",
        };
        var report = CanonReconciliationService.DetectDrift(bundle, context);

        RenameReconciliationService.Apply(
            bundle,
            context,
            report,
            new RenameReconciliationOptions { UpdatePhraseHighlights = true },
            rules);

        Assert.Equal("NewName", rules[0].Phrase);
    }

    [Fact]
    public void ResolveForEntity_finds_phrase_only_rule_without_entity_linkage()
    {
        var id = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Anwen", Color = "#FFD166", Enabled = true },
        };

        var resolved = PhraseHighlightRuleService.ResolveForEntity(
            rules,
            "Characters",
            id,
            "Anwen");

        Assert.NotNull(resolved);
        Assert.True(resolved!.Enabled);
        Assert.Equal("#FFD166", resolved.Color);
    }

    [Fact]
    public void UpsertLinkedRule_adopts_phrase_only_rule_and_sets_entity_linkage()
    {
        var id = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new() { Phrase = "Anwen", Color = "#06D6A0", Enabled = true },
        };

        PhraseHighlightRuleService.UpsertLinkedRule(
            rules,
            "Anwen",
            "Characters",
            id,
            "#FFD166",
            enabled: true,
            "#161618");

        Assert.Single(rules);
        Assert.Equal(id, rules[0].EntityId);
        Assert.Equal("Characters", rules[0].EntityCategory);
        Assert.Equal("#FFD166", rules[0].Color);
    }

    [Fact]
    public void DisableLinkedRules_sets_enabled_false_without_removing_rule()
    {
        var id = Guid.NewGuid();
        var rules = new List<PhraseHighlightRule>
        {
            new()
            {
                Phrase = "Mara",
                Color = "#FFD166",
                Enabled = true,
                EntityId = id,
                EntityCategory = "Characters",
            },
            new() { Phrase = "Other", Color = "#06D6A0", Enabled = true },
        };

        PhraseHighlightRuleService.DisableLinkedRules(rules, "Characters", id);

        Assert.Equal(2, rules.Count);
        Assert.False(rules[0].Enabled);
        Assert.True(rules[1].Enabled);
    }

    [Fact]
    public void Phrase_highlight_asset_refreshes_existing_styles_without_schedule()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-phrase-highlights.js");
        Assert.Contains("refreshExistingHighlightStyles", js);
        Assert.Contains("opts.schedule === false", js);
    }

    [Fact]
    public void Phrase_highlight_asset_includes_possessive_suffix_matching()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-phrase-highlights.js");
        Assert.Contains("extendMatchForPossessive", js);
        Assert.Contains("compileRuleNeedles", js);
    }

    [Fact]
    public void Phrase_highlight_asset_matches_full_phrase_and_first_name_aliases()
    {
        var js = WrapperAssetTestHelpers.ReadAsset("continuous-phrase-highlights.js");
        Assert.Contains("classifyPhraseProfile", js);
        Assert.Contains("getFirstNameAlias", js);
        Assert.Contains("needles", js);
        Assert.DoesNotContain("getLastWordAlias", js);
    }

    [Fact]
    public void SanitizeForInjection_ensures_readable_color_on_serialize()
    {
        var canvas = ThemeRuntime.Current.GetHex("BgBase");
        var sanitized = PhraseHighlightRuleService.SanitizeForInjection(
            new PhraseHighlightRule { Phrase = "Test", Color = "#E8E8E8" },
            canvas);

        Assert.True(ThemeContrast.IsReadable(sanitized.Color, canvas));
    }
}

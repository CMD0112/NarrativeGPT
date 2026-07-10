using ChatGPTWrapper.Adventure;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PhraseHighlightEntitySourceCatalogTests
{
    [Fact]
    public void DescribeImportSources_lists_all_canon_import_kinds()
    {
        var sources = PhraseHighlightEntitySourceCatalog.DescribeImportSources(null);

        Assert.Contains(sources, s => s.SourceKey == CanonSchemaRegistry.PlayerKind);
        Assert.Contains(sources, s => s.SourceKey == CanonSchemaRegistry.NpcKind);
        Assert.Contains(sources, s => s.SourceKey == CanonSchemaRegistry.FactionKind);
        Assert.Contains(sources, s => s.SourceKey == CanonSchemaRegistry.QuestKind);
        Assert.Contains(sources, s => s.SourceKey == CanonSchemaRegistry.MysteryKind);
    }

    [Fact]
    public void DescribeEntityCategories_includes_dynamic_categories_from_bundle()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Category catalog");
        bundle.Entities.Factions.Add(new FactionEntry { Name = "Guild" });
        bundle.Entities.Quests.Add(new QuestEntry { Title = "Find the key" });

        var categories = PhraseHighlightEntitySourceCatalog.DescribeEntityCategories(bundle.Entities);

        Assert.Contains(categories, c => c.UiCategory == "Factions" && c.EntityCount == 1);
        Assert.Contains(categories, c => c.UiCategory == "Quests" && c.EntityCount == 1);
    }

    [Fact]
    public void BuildCandidates_imports_factions_when_source_included()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Faction import");
        bundle.Entities.Factions.Add(new FactionEntry { Name = "Iron Compact" });

        var result = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions
            {
                IncludedSourceKeys = new HashSet<string>([CanonSchemaRegistry.FactionKind], StringComparer.OrdinalIgnoreCase),
            });

        Assert.Contains(result.Candidates, c => c.Phrase == "Iron Compact");
    }

    [Fact]
    public void BuildCandidates_legacy_flags_keep_player_and_party_cast_separate()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Legacy import");
        bundle.Entities.Player.Name = "Ari";
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Mara", Role = "Guide" });

        var partyOnly = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = false, IncludeParty = true });

        Assert.DoesNotContain(partyOnly.Candidates, c => c.Phrase == "Ari");
        Assert.Contains(partyOnly.Candidates, c => c.Phrase == "Mara");

        var playerOnly = PhraseHighlightCastImportService.BuildCandidates(
            bundle,
            new CastPhraseImportOptions { IncludePlayer = true, IncludeParty = false });

        Assert.Contains(playerOnly.Candidates, c => c.Phrase == "Ari");
        Assert.DoesNotContain(playerOnly.Candidates, c => c.Phrase == "Mara");
    }
}

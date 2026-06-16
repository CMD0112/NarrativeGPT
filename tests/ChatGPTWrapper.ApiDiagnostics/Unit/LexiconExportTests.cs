using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class LexiconExportTests
{
    [Fact]
    public void Build_includes_default_rules_and_in_use_entities()
    {
        var bundle = AdventureStore.CreateNew("Lexicon test");
        bundle.Entities.Player.Name = "Kira Ashford";
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Tomas Venn",
            Role = "guide",
            Aliases = ["Tomas"],
        });
        bundle.Entities.Locations.Add(new LocationEntry { Name = "Harbor of Salt" });
        bundle.Entities.Factions.Add(new FactionEntry { Name = "Iron Covenant" });
        bundle.Scenario.LexiconPools = "### Coastal\nBranik, Sera, Oleg";
        bundle.Scenario.LexiconAvoid = "Marcus, Elena, Thorne";

        var content = LexiconExportService.Build(bundle);

        Assert.Contains("# Lexicon", content, StringComparison.Ordinal);
        Assert.Contains("## rules", content, StringComparison.Ordinal);
        Assert.Contains("## in-use", content, StringComparison.Ordinal);
        Assert.Contains("Kira Ashford", content, StringComparison.Ordinal);
        Assert.Contains("Tomas Venn", content, StringComparison.Ordinal);
        Assert.Contains("Harbor of Salt", content, StringComparison.Ordinal);
        Assert.Contains("Iron Covenant", content, StringComparison.Ordinal);
        Assert.Contains("## pools", content, StringComparison.Ordinal);
        Assert.Contains("Coastal", content, StringComparison.Ordinal);
        Assert.Contains("## avoid", content, StringComparison.Ordinal);
        Assert.Contains("Marcus", content, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_uses_design_workspace_fields_while_designing()
    {
        var bundle = AdventureDesignService.CreateDesigningAdventure("Design lexicon");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Lexicon, "lexiconRules", "Use short harsh names for villains.");
        AdventureDesignService.SetField(bundle, AdventureDesignStep.Lexicon, "lexiconAvoid", "Raven, Ash");

        var content = LexiconExportService.Build(bundle);

        Assert.Contains("Use short harsh names for villains.", content, StringComparison.Ordinal);
        Assert.Contains("Raven, Ash", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ExportForce_writes_lexicon_md()
    {
        var bundle = AdventureStore.CreateNew("Export lexicon");
        bundle.Entities.Characters.Add(new CharacterEntry { Name = "Mara Voss", Role = "captain" });

        ProjectSourceExportService.ExportForce(bundle);

        var path = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), SectionSchema.LexiconFile);
        Assert.True(File.Exists(path));
        var text = File.ReadAllText(path);
        Assert.Contains("Mara Voss", text, StringComparison.Ordinal);
    }
}

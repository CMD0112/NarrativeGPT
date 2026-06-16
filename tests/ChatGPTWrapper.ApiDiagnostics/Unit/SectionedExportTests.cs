using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class SectionedExportTests
{
    [Fact]
    public void Export_writes_cast_md_with_sections()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        bundle.Metadata.Settings.UseSectionInjection = true;
        bundle.Entities.Characters.Add(new CharacterEntry
        {
            Name = "Mara Voss",
            Description = "Runs the apothecary.",
            Aliases = ["Mara"],
            Pinned = true,
        });

        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var castPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), "cast.md");
            Assert.True(File.Exists(castPath));
            Assert.False(File.Exists(Path.Combine(ProjectSourceExportService.SourcesDirectory(bundle), "story-cards.md")));

            var castEntry = bundle.SourceManifest.Entries
                .First(e => e.RelativePath == SectionSchema.CastFile);
            Assert.Contains(castEntry.Sections, s => s.Id == "npcs/mara-voss");
            Assert.Equal("person", castEntry.Sections.First(s => s.Id == "npcs/mara-voss").Kind);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }

    [Fact]
    public void Export_manifest_includes_opening_and_rules_sections()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(entryCount: 4);
        try
        {
            AdventureTestData.WriteLocalSources(bundle);
            var scenario = bundle.SourceManifest.Entries.First(e => e.RelativePath == "scenario.md");
            var world = bundle.SourceManifest.Entries.First(e => e.RelativePath == "world.md");
            Assert.Contains(scenario.Sections, s => s.Id == "opening");
            Assert.Contains(world.Sections, s => s.Id == "rules");
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}

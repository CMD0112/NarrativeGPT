using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
public sealed class CastImportDiagnosticTests : IClassFixture<FileLockAwareFixture>
{
    [Fact(Skip = "Requires machine-local King in Red cast.md fixture")]
    public void King_in_red_cast_file_imports_all_headings()
    {
        var castPath = @"e:\Documents\ChatGPT Wrapper\Adventures\b9233735-fdfa-47fe-8f2c-e7122d562f83\sources\cast.md";
        if (!File.Exists(castPath))
            return;

        var markdown = File.ReadAllText(castPath);
        var doc = SectionMarkdownParser.Parse(markdown);

        var party = doc.Sections.FirstOrDefault(s => s.Id == "party");
        var npcs = doc.Sections.FirstOrDefault(s => s.Id == "npcs");
        var player = doc.Sections.FirstOrDefault(s => s.Id == "player");

        Assert.Null(player);
        Assert.NotNull(party);
        Assert.NotNull(npcs);
        Assert.Single(party!.Entries);
        Assert.Equal(12, npcs!.Entries.Count);

        var bundle = AdventureStore.CreateNew("Cast import diagnostic");
        try
        {
            var result = SectionedImportService.ImportCast(bundle, markdown);
            Assert.Equal(13, bundle.Entities.Party.Count + bundle.Entities.Characters.Count);
            Assert.Single(bundle.Entities.Party);
            Assert.Equal(12, bundle.Entities.Characters.Count);
            Assert.True(string.IsNullOrWhiteSpace(bundle.Entities.Player.Name));
            Assert.True(result.EntitiesAdded >= 13);
        }
        finally
        {
            AdventureTestData.DeleteBundle(bundle);
        }
    }
}

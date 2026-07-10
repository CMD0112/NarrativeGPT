using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntityDetailProfileExportTests
{
    [Fact]
    public void Player_cast_export_reimports_extended_fields_and_portrait_path()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Id = Guid.NewGuid(), Title = "Test" },
            Entities = new EntitiesDocument
            {
                Player = new PlayerCharacterSheet
                {
                    Name = "Ari",
                    Background = "Scholar",
                    Goals = "Find the archive",
                    ImagePath = "entity-media/ari.png",
                    Tags = ["hero", "mage"],
                    Aliases = ["The Seeker"],
                    ExtendedFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Arcane focus"] = "Obsidian rod",
                    },
                },
            },
        };

        var exported = SectionedExportService.BuildCast(bundle);
        var reloaded = new AdventureBundle
        {
            Metadata = bundle.Metadata,
            Entities = new EntitiesDocument(),
        };

        var result = SectionedImportService.ImportCast(reloaded, exported.Content);
        Assert.True(result.EntitiesUpdated >= 1);

        var player = reloaded.Entities.Player;
        Assert.Equal("Ari", player.Name);
        Assert.Equal("Scholar", player.Background);
        Assert.Equal("Find the archive", player.Goals);
        Assert.Equal("entity-media/ari.png", player.ImagePath);
        Assert.Contains("hero", player.Tags);
        Assert.Contains("The Seeker", player.Aliases);
        Assert.Equal("Obsidian rod", player.ExtendedFields["Arcane focus"]);
    }

    [Fact]
    public void Player_mapper_round_trips_new_profile_fields()
    {
        var adventureId = Guid.NewGuid();
        var entities = new EntitiesDocument
        {
            Player = new PlayerCharacterSheet
            {
                Name = "Kai",
                Background = "Soldier",
                ImagePath = "entity-media/kai.png",
                Tags = ["vet"],
                Aliases = ["Sarge"],
                Goals = "Get home",
            },
        };

        var model = EntityEditMapper.Load(entities, EntityEditMapper.PlayerEntityId, "Player", adventureId);
        Assert.NotNull(model);
        Assert.Equal("entity-media/kai.png", model!.ImagePath);
        Assert.Equal("vet", EntityEditMapper.ParseTags(model.TagsText).Single());
        Assert.Equal("Sarge", EntityEditMapper.ParseTags(model.AliasesText).Single());

        model.Name = "Kai Updated";
        model.TagsText = "vet, leader";
        Assert.True(EntityEditMapper.Apply(entities, model));

        Assert.Equal("Kai Updated", entities.Player.Name);
        Assert.Equal(2, entities.Player.Tags.Count);
    }
}

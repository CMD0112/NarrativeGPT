using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

public sealed class EntityEditMapperTests
{
    [Fact]
    public void PlayGridCategories_includes_player_party_and_characters()
    {
        var filters = CanonEntityResolver.PlayReferenceFilters;
        Assert.Contains("Player", filters, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Party", filters, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Characters", filters, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Player_load_apply_round_trip()
    {
        var entities = new EntitiesDocument();
        entities.Player.Name = "Ari";
        entities.Player.Background = "Scholar";

        var loaded = EntityEditMapper.Load(entities, EntityEditMapper.PlayerEntityId, "Player", Guid.NewGuid());
        Assert.NotNull(loaded);
        loaded!.Name = "Ari Updated";
        loaded.SecondaryValue = "Wanderer";
        SetField(loaded, "personality", "Curious");

        Assert.True(EntityEditMapper.Apply(entities, loaded));
        Assert.Equal("Ari Updated", entities.Player.Name);
        Assert.Equal("Wanderer", entities.Player.Background);
        Assert.Equal("Curious", entities.Player.Personality);
    }

    [Fact]
    public void Npc_load_apply_round_trip()
    {
        var id = Guid.NewGuid();
        var entities = new EntitiesDocument
        {
            Characters =
            [
                new CharacterEntry
                {
                    Id = id,
                    Name = "Merchant",
                    Role = "Trader",
                    Description = "Runs the stall",
                },
            ],
        };

        var adventureId = Guid.NewGuid();
        var loaded = EntityEditMapper.Load(entities, id, "Characters", adventureId);
        Assert.NotNull(loaded);
        loaded!.SecondaryValue = "Shopkeeper";
        loaded.Description = "Updated description";

        Assert.True(EntityEditMapper.Apply(entities, loaded));
        var character = entities.Characters.Single();
        Assert.Equal("Shopkeeper", character.Role);
        Assert.Equal("Updated description", character.Description);
    }

    [Fact]
    public void Party_load_apply_round_trip()
    {
        var id = Guid.NewGuid();
        var entities = new EntitiesDocument
        {
            Party =
            [
                new CompanionEntry
                {
                    Id = id,
                    Name = "Nessa",
                    Condition = "Healthy",
                    Relationship = "Ally",
                    Attitude = "Friendly",
                },
            ],
        };

        var loaded = EntityEditMapper.Load(entities, id, "Party", Guid.NewGuid());
        Assert.NotNull(loaded);
        loaded!.Description = "Close friend";
        SetField(loaded, "attitude", "Cautious");

        Assert.True(EntityEditMapper.Apply(entities, loaded));
        var companion = entities.Party.Single();
        Assert.Equal("Close friend", companion.Relationship);
        Assert.Equal("Cautious", companion.Attitude);
    }

    [Fact]
    public void Delete_removes_character_from_entities()
    {
        var id = Guid.NewGuid();
        var entities = new EntitiesDocument
        {
            Characters = [new CharacterEntry { Id = id, Name = "Temp" }],
        };

        EntityEditMapper.Delete(entities, id, "Characters");

        Assert.Empty(entities.Characters);
    }

    private static void SetField(EntityEditModel model, string key, string value)
    {
        var field = model.Fields.FirstOrDefault(f => f.Key == key);
        if (field is not null)
            field.Value = value;
    }
}

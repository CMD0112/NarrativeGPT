using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class EntitiesCanonHygieneServiceTests
{
    [Fact]
    public void PruneCrossCategoryConceptDuplicates_removes_concept_when_name_exists_as_character()
    {
        var entities = new EntitiesDocument();
        entities.Characters.Add(new CharacterEntry { Name = "Silverhaven" });
        entities.Concepts.Add(new ConceptEntry { Name = "Silverhaven", Description = "A place concept duplicate" });
        entities.Concepts.Add(new ConceptEntry { Name = "The Veil", Description = "Unique lore" });

        var result = EntitiesCanonHygieneService.PruneCrossCategoryConceptDuplicates(entities);

        Assert.Equal(1, result.ConceptsRemoved);
        Assert.Equal("Silverhaven", result.RemovedConcepts[0].Name);
        Assert.Equal("Characters", result.RemovedConcepts[0].DuplicateOfCategory);
        Assert.Single(entities.Concepts);
        Assert.Equal("The Veil", entities.Concepts[0].Name);
    }

    [Fact]
    public void NameOwnedByOtherCategory_detects_location_collision()
    {
        var entities = new EntitiesDocument();
        entities.Locations.Add(new LocationEntry { Name = "Harbor District" });

        Assert.True(EntitiesCanonHygieneService.NameOwnedByOtherCategory(entities, "Harbor District", out var category));
        Assert.Equal("Locations", category);
    }
}

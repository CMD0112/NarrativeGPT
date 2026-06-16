using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class ProcessTurnResponseTests
{
    [Fact]
    public void ApplyProcessTurn_applies_memories_and_entities()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        const string json = """
            {
              "memories": [{"text":"Player counted aloud.","tags":["event"],"pinned":false,"anchor":{"pairOffset":0,"playerHint":"count"}}],
              "entities": [{"entityType":"concept","name":"The Silence","description":"Nothing answers.","category":"metaphysical"}]
            }
            """;

        var result = GenerationJobHandlers.ApplyResponse(bundle, GenerationJobId.ProcessTurn, json);

        Assert.Equal(2, result.ProposalCount);
        Assert.Null(result.Error);
        Assert.Single(bundle.Memory.ReviewQueue);
        Assert.Single(bundle.Entities.ReviewQueue);
    }

    [Fact]
    public void IsSettledProcessTurnResponse_accepts_valid_object()
    {
        const string json = """{"memories":[],"entities":[]}""";
        Assert.True(GenerationJobHandlers.IsSettledJobResponse(
            GenerationJobId.ProcessTurn,
            json,
            streamComplete: true));
    }

    [Fact]
    public void GetUtilityJobId_maps_expand_entity_to_extract_entities()
    {
        Assert.Equal(
            GenerationJobId.ExtractEntities,
            GenerationJobHandlers.GetUtilityJobId(GenerationJobId.ExpandEntity));
    }
}

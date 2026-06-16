using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityResponseParseTests
{
    private const string UserWrappedMemories = """
        [[cgw:utility-response job="propose_memories" v="1"]][{"text":"The player is in an ordinary room with plain walls, counting aloud into the silence; nothing has answered, opened, or changed.","tags":["state","room","counting"],"pinned":false},{"text":"A brief narration drift to a rain-soaked castle occurred, but the scene has corrected back to the ordinary room and should remain there unless the player changes it.","tags":["continuity","correction","room"],"pinned":false}][[/cgw:utility-response]]
        """;

    [Fact]
    public void UnwrapUtilityJobResponse_extracts_json_array_body()
    {
        var unwrapped = ContextTagFormat.UnwrapUtilityJobResponse(UserWrappedMemories);
        Assert.StartsWith("[{", unwrapped);
        Assert.EndsWith("}]", unwrapped);
        Assert.DoesNotContain("[[cgw:", unwrapped);
    }

    [Fact]
    public void NormalizeUtilityCapturedAssistantText_unwraps_response_instead_of_stripping_body()
    {
        var normalized = AdventureTurnService.NormalizeUtilityCapturedAssistantText(UserWrappedMemories);
        Assert.StartsWith("[{", normalized);
        Assert.DoesNotContain("[[cgw:", normalized);
        Assert.Equal(2, GenerationJobHandlers.ApplyResponse(
            new AdventureBundle { Metadata = new AdventureMetadata() },
            GenerationJobId.ProposeMemories,
            normalized).ProposalCount);
    }

    [Fact]
    public void ApplyResponse_parses_wrapped_propose_memories()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };
        var result = GenerationJobHandlers.ApplyResponse(
            bundle,
            GenerationJobId.ProposeMemories,
            UserWrappedMemories);

        Assert.Equal(2, result.ProposalCount);
        Assert.Null(result.Error);
        Assert.Equal(2, bundle.Memory.ReviewQueue.Count);
    }
}

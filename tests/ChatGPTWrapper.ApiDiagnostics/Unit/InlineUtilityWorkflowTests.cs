using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class InlineUtilityWorkflowTests
{
    [Fact]
    public void IsUtilityUserMessage_detects_wrapped_job_packet()
    {
        var text = ContextTagFormat.WrapUtilityJob("propose_memories", "=== MEMORY PROPOSAL JOB ===");
        Assert.True(ConversationStreamParser.IsUtilityUserMessage(text));
        Assert.Equal("propose_memories", ConversationStreamParser.ExtractUtilityJobId(text));
    }

    [Fact]
    public void ExtractTranscriptPlayerText_returns_null_for_utility_messages()
    {
        var text = ContextTagFormat.WrapUtilityJob("extract_entities", "=== ENTITY EXTRACTION JOB ===");
        Assert.Null(ConversationStreamParser.ExtractTranscriptPlayerText(text));
    }

    [Theory]
    [InlineData(UtilityDeliveryMode.InlinePlayThread, true)]
    [InlineData(UtilityDeliveryMode.SeparateThread, true)]
    public void UtilityDeliveryModeService_UsesInlineDelivery(UtilityDeliveryMode mode, bool expected)
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                Settings = new AdventureSettings { UtilityDeliveryMode = mode },
            },
        };

        Assert.Equal(expected, UtilityDeliveryModeService.UsesInlineDelivery(bundle));
    }

    [Fact]
    public void IsSettledJobResponse_accepts_empty_array_for_memories()
    {
        Assert.True(GenerationJobHandlers.IsSettledJobResponse(
            GenerationJobId.ProposeMemories,
            "[]",
            streamComplete: true));
    }

    [Fact]
    public void IsUtilityAssistantMessage_detects_wrapped_response()
    {
        var wrapped = ContextTagFormat.WrapUtilityResponse("generate_recap", "A brief recap.");
        Assert.True(ConversationStreamParser.IsUtilityAssistantMessage(wrapped));
    }

    [Fact]
    public void IsSettledJobResponse_unwraps_inline_utility_response_tag()
    {
        var wrapped = ContextTagFormat.WrapUtilityResponse(GenerationJobId.ProposeMemories, "[]");
        Assert.True(GenerationJobHandlers.IsSettledJobResponse(
            GenerationJobId.ProposeMemories,
            wrapped,
            streamComplete: true));
    }
}

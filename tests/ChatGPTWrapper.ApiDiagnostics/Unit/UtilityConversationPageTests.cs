using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityConversationPageTests
{
    [Fact]
    public void IsProjectHomePage_detects_project_landing_without_conversation()
    {
        const string gizmoId = "g-p-6a220fab2eb48191a75b9d88d85a3d91";
        const string convId = "805de2b3-59cd-4934-b455-9a3dbd981865";

        Assert.True(UtilityConversationPageService.IsProjectHomePage(
            $"https://chatgpt.com/g/{gizmoId}/project"));
        Assert.False(UtilityConversationPageService.IsProjectHomePage(
            ChatGptUrls.BuildProjectConversationUrl(convId, gizmoId)));
        Assert.False(UtilityConversationPageService.IsProjectHomePage(
            $"https://chatgpt.com/c/{convId}"));
    }

    [Theory]
    [InlineData(100, 1400)]
    [InlineData(7409, 9490)]
    public void ComputeComposerStableWaitMs_scales_with_packet_size(int length, int expectedMs)
    {
        Assert.Equal(expectedMs, AdventureTurnService.ComputeComposerStableWaitMs(length));
    }

    [Fact]
    public void MatchesTargetConversation_accepts_project_query_and_canonical_paths()
    {
        const string convId = "805de2b3-59cd-4934-b455-9a3dbd981865";
        const string gizmoId = "g-p-6a220fab2eb48191a75b9d88d85a3d91";

        var withProject = ChatGptUrls.BuildProjectConversationUrl(convId, gizmoId);
        var canonical = $"https://chatgpt.com/g/g-p-6a220fab2eb48191a75b9d88d85a3d91/c/{convId}";

        Assert.True(UtilityConversationPageService.MatchesTargetConversation(withProject, convId, gizmoId));
        Assert.True(UtilityConversationPageService.MatchesTargetConversation(canonical, convId, gizmoId));
        Assert.False(UtilityConversationPageService.MatchesTargetConversation("https://chatgpt.com/", convId, gizmoId));
        Assert.False(UtilityConversationPageService.MatchesTargetConversation(withProject, "other-conv", gizmoId));
    }

    [Fact]
    public void ApplyResponse_uses_capture_error_instead_of_generic_empty_response()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();

        var result = GenerationJobHandlers.ApplyResponse(
            bundle,
            GenerationJobId.ProposeMemories,
            responseText: null,
            captureError: "capture_timeout");

        Assert.Equal(0, result.ProposalCount);
        Assert.Equal("capture_timeout", result.Error);
        Assert.True(GenerationJobHandlers.IsCaptureFailureError(result.Error));
    }

    [Fact]
    public void IsCaptureFailureError_treats_parse_failures_as_actionable()
    {
        Assert.True(GenerationJobHandlers.IsCaptureFailureError("capture_timeout"));
        Assert.True(GenerationJobHandlers.IsCaptureFailureError("utility_page_not_ready"));
        Assert.True(GenerationJobHandlers.IsCaptureFailureError("capture_no_assistant"));
        Assert.True(GenerationJobHandlers.IsCaptureFailureError("submit_not_observed"));
        Assert.True(GenerationJobHandlers.IsCaptureFailureError("bridge_not_ready"));
        Assert.True(GenerationJobHandlers.IsCaptureFailureError("conversation_unregistered"));
        Assert.False(GenerationJobHandlers.IsCaptureFailureError("no_proposals_parsed"));
        Assert.False(GenerationJobHandlers.IsCaptureFailureError("parse_failed"));
    }

    [Theory]
    [InlineData("utility_page_not_ready")]
    [InlineData("capture_no_assistant")]
    public void ApplyResponse_preserves_specific_capture_errors(string captureError)
    {
        var bundle = AdventureTestData.CreateLinkedBundle();

        var result = GenerationJobHandlers.ApplyResponse(
            bundle,
            GenerationJobId.ProposeMemories,
            responseText: null,
            captureError: captureError);

        Assert.Equal(captureError, result.Error);
        Assert.True(GenerationJobHandlers.IsCaptureFailureError(result.Error));
    }
}

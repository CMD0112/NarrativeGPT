using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityConversationReadinessTests
{
    [Theory]
    [InlineData("http_404", true)]
    [InlineData("HTTP_404", true)]
    [InlineData("conversation_fetch_failed: http_404", true)]
    [InlineData("http_429", true)]
    [InlineData("conversation_fetch_failed: http_429", true)]
    [InlineData("http_403", false)]
    [InlineData("missing_parent_message_id", false)]
    [InlineData(null, false)]
    public void IsDomCapableFetchError_detects_dom_capable_api_errors(string? fetchError, bool expected)
    {
        Assert.Equal(expected, UtilityConversationReadinessService.IsDomCapableFetchError(fetchError));
    }

    [Theory]
    [InlineData("http_404", true)]
    [InlineData("http_429", false)]
    [InlineData("http_403", false)]
    public void IsDomOnlyFetchError_still_detects_404_only(string? fetchError, bool expected)
    {
        Assert.Equal(expected, UtilityConversationReadinessService.IsDomOnlyFetchError(fetchError));
    }

    [Theory]
    [InlineData("http_429", true)]
    [InlineData("HTTP_429", true)]
    [InlineData("http_404", false)]
    [InlineData("http_403", false)]
    public void IsRateLimitFetchError_detects_429(string? fetchError, bool expected)
    {
        Assert.Equal(expected, UtilityConversationReadinessService.IsRateLimitFetchError(fetchError));
    }

    [Theory]
    [InlineData(1000, 120_000)]
    [InlineData(4001, 122_008)]
    [InlineData(20_000, 180_000)]
    public void ComputeUtilityJobTimeoutMs_scales_large_packets(int messageLength, int expected)
    {
        Assert.Equal(expected, AdventureTurnService.ComputeUtilityJobTimeoutMs(messageLength));
    }

    [Theory]
    [InlineData("submit_not_verified", "submit_not_observed")]
    [InlineData("timeout", "capture_timeout")]
    [InlineData("composer_not_found", "utility_page_not_ready")]
    [InlineData("other", "other")]
    [InlineData(null, "capture_no_assistant")]
    public void MapUtilityBridgeError_normalizes_bridge_errors(string? input, string expected)
    {
        Assert.Equal(expected, AdventureTurnService.MapUtilityBridgeError(input));
    }

    [Theory]
    [InlineData("capture_premature", true)]
    [InlineData("rate_limited", true)]
    [InlineData("parse_failed", false)]
    [InlineData("no_proposals_parsed", false)]
    public void IsCaptureFailureError_classifies_capture_and_rate_errors(string? error, bool expected)
    {
        Assert.Equal(expected, GenerationJobHandlers.IsCaptureFailureError(error));
    }

    [Theory]
    [InlineData(GenerationJobId.ProposeMemories, "[]", false)]
    [InlineData(GenerationJobId.ProposeMemories, "[{\"text\":\"room\",\"tags\":[],\"pinned\":false}]", false)]
    [InlineData(GenerationJobId.ProposeMemories, "Eight.", true)]
    [InlineData(GenerationJobId.ProposeMemories, "", true)]
    [InlineData(null, "short", true)]
    [InlineData(null, "this is long enough text", false)]
    public void IsUtilityCapturePremature_detects_settled_vs_short_responses(
        string? jobId,
        string assistantText,
        bool expectedPremature)
    {
        Assert.Equal(expectedPremature, AdventureTurnService.IsUtilityCapturePremature(jobId, assistantText));
    }

    [Theory]
    [InlineData("capture_premature (Pin a utility Project tab for more reliable jobs.)", "capture_premature", true)]
    [InlineData("capture_premature", "capture_premature", true)]
    [InlineData("conversation_mismatch", "capture_premature", false)]
    public void GenerationJobService_matches_utility_send_error_prefix(
        string? error,
        string code,
        bool expected)
    {
        Assert.Equal(expected, GenerationJobService.IsUtilitySendError(error, code));
    }
}

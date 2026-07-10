using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayUtilityRetrievalServiceTests
{
    [Fact]
    public void StripUtilityResponsesForNarrator_removes_response_tags()
    {
        var text = """
            The room is dark.

            [[cgw:utility-response job="propose_memories" v="1"]][{"text":"memory"}][[/cgw:utility-response]]
            """;

        var stripped = PlayUtilityRetrievalService.StripUtilityResponsesForNarrator(text);

        Assert.Contains("The room is dark", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("utility-response", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void ProcessAssistantResponse_applies_dispatched_job()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        bundle.Metadata.LastDispatchedUtilityJobs =
        [
            new PendingUtilityInjection
            {
                JobId = GenerationJobId.UpdateSummary,
                Channel = UtilityExecutionChannel.AutoBackground,
            },
        ];

        var response = ContextTagFormat.WrapUtilityResponse(
            GenerationJobId.UpdateSummary,
            """{"rollingSummary":"A test summary."}""");

        var result = PlayUtilityRetrievalService.ProcessAssistantResponse(bundle, response);

        Assert.Equal(1, result.ProcessedCount);
        Assert.Empty(bundle.Metadata.LastDispatchedUtilityJobs);
    }
}

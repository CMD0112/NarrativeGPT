using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityProposalInferenceTaggingTests
{
    [Theory]
    [InlineData(null, "all", true)]
    [InlineData(null, UtilityProposalInferenceTagging.ChatGptUtilityFilter, true)]
    [InlineData(null, UtilityLane.LocalLlm, false)]
    [InlineData(UtilityLane.LocalLlm, UtilityLane.LocalLlm, true)]
    [InlineData(UtilityLane.LocalLlm, UtilityProposalInferenceTagging.ChatGptUtilityFilter, false)]
    [InlineData(UtilityLane.PlayLegacyInline, UtilityProposalInferenceTagging.ChatGptUtilityFilter, true)]
  [InlineData(null, UtilityLane.PlayLegacyInline, true)]
    public void MatchesSourceFilter_handles_untagged_chatgpt_proposals(
        string? itemSource,
        string filter,
        bool expected) =>
        Assert.Equal(expected, UtilityProposalInferenceTagging.MatchesSourceFilter(itemSource, filter));
}

[Trait("Category", "Unit")]
public sealed class UtilityDualRunReviewServiceTests
{
    [Fact]
    public void IsDuplicateMemory_allows_same_text_from_different_inference_sources_when_dual_run()
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata(),
            Memory = new MemoryDocument
            {
                ReviewQueue =
                [
                    new MemoryEntry
                    {
                        Text = "Marta warned about the basement.",
                        InferenceSource = UtilityLane.LocalLlm,
                    },
                ],
            },
        };

        var candidate = new MemoryEntry
        {
            Text = "Marta warned about the basement.",
            InferenceSource = UtilityLane.PlayLegacyInline,
        };

        var context = new GenerationJobContext
        {
            AllowCrossSourceDuplicates = true,
            InferenceSource = UtilityLane.PlayLegacyInline,
        };

        Assert.False(UtilityTranscriptScopeService.IsDuplicateMemory(bundle.Memory, candidate, context));
    }

    [Fact]
    public void QueueProposal_dual_run_stores_separate_summary_proposals_per_source()
    {
        var bundle = new AdventureBundle { Metadata = new AdventureMetadata() };
        var localContext = new GenerationJobContext
        {
            AllowCrossSourceDuplicates = true,
            InferenceSource = UtilityLane.LocalLlm,
        };
        var remoteContext = new GenerationJobContext
        {
            AllowCrossSourceDuplicates = true,
            InferenceSource = UtilityLane.PlayLegacyInline,
        };

        SummaryReviewService.QueueProposal(bundle, "Local digest", localContext);
        SummaryReviewService.QueueProposal(bundle, "ChatGPT digest", remoteContext);

        Assert.Equal(2, bundle.Summary.SourceProposals.Count(p => !p.Resolved));
        Assert.Equal(2, SummaryReviewService.GetPendingCount(bundle.Summary));
    }
}

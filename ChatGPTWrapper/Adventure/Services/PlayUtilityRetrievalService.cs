using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class PlayUtilityRetrievalResult
{
    public int ProcessedCount { get; init; }

    public int SuccessCount { get; init; }

    public List<GenerationJobResult> ApplyResults { get; init; } = [];

    public static PlayUtilityRetrievalResult Empty { get; } = new();

    public bool AnyProcessed => ProcessedCount > 0;

    public GenerationJobResult? PrimaryResult =>
        ApplyResults.Count > 0 ? ApplyResults[0] : null;
}

/// <summary>Captures and applies utility responses from play-thread assistant messages (CMD-332).</summary>
internal static class PlayUtilityRetrievalService
{
    public static string StripUtilityResponsesForNarrator(string? assistantText) =>
        ContextTagFormat.StripUtilityResponseBlocks(assistantText);

    public static PlayUtilityRetrievalResult ProcessAssistantResponse(
        AdventureBundle bundle,
        string? assistantText,
        string? conversationId = null)
    {
        var dispatched = bundle.Metadata.LastDispatchedUtilityJobs ?? [];
        if (dispatched.Count == 0 || string.IsNullOrWhiteSpace(assistantText))
            return PlayUtilityRetrievalResult.Empty;

        var blocks = ContextTagFormat.ExtractUtilityResponseBlocks(assistantText);
        var successCount = 0;
        var applyResults = new List<GenerationJobResult>();

        foreach (var pending in dispatched)
        {
            var block = blocks.FirstOrDefault(b =>
                             string.Equals(b.JobId, pending.JobId, StringComparison.OrdinalIgnoreCase));
            var responseText = block.Body;
            if (string.IsNullOrWhiteSpace(responseText) && blocks.Count == 1 && dispatched.Count == 1)
                responseText = blocks[0].Body;
            var validation = UtilityResponseSchemaRegistry.Validate(pending.JobId, responseText);
            var applyPayload = validation.Payload ?? ContextTagFormat.UnwrapUtilityJobResponse(responseText);
            var applyError = validation.Ok ? null : validation.Error;
            var applyResult = GenerationJobHandlers.ApplyResponse(
                bundle,
                pending.JobId,
                applyPayload,
                applyError);

            UtilityJobResultStore.Save(
                bundle,
                pending,
                responseText,
                validation,
                applyResult,
                conversationId);

            UtilityParseLogService.Append(
                bundle,
                pending.JobId,
                applyPayload,
                applyResult.ProposalCount,
                applyResult.Error ?? validation.Error,
                applyResult.ProposalIds);

            bundle.UtilityExchanges.Exchanges.Add(new UtilityExchangeRecord
            {
                JobId = pending.JobId,
                ResponseText = applyPayload,
                ConversationId = conversationId,
            });

            bundle.Metadata.UtilityJobLastErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var utilityJobId = GenerationJobHandlers.GetUtilityJobId(pending.JobId);
            if (!applyResult.Success || (applyResult.ProposalCount == 0 && applyResult.Error is not null))
                bundle.Metadata.UtilityJobLastErrors[utilityJobId] =
                    applyResult.Error ?? applyResult.SkippedReason ?? validation.Error ?? "failed";
            else
                bundle.Metadata.UtilityJobLastErrors.Remove(utilityJobId);

            if (applyResult.Success)
                successCount++;

            applyResults.Add(new GenerationJobResult
            {
                Success = applyResult.Success,
                ProposalCount = applyResult.ProposalCount,
                Error = applyResult.Error,
                SkippedReason = applyResult.SkippedReason,
                DisplayText = applyResult.DisplayText,
                ProposalIds = applyResult.ProposalIds,
            });
        }

        bundle.Metadata.LastDispatchedUtilityJobs = [];
        return new PlayUtilityRetrievalResult
        {
            ProcessedCount = dispatched.Count,
            SuccessCount = successCount,
            ApplyResults = applyResults,
        };
    }
}

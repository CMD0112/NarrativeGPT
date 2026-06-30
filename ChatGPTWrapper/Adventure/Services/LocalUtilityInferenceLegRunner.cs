using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal sealed class LocalUtilityInferenceLegResult
{
    public bool Attempted { get; init; }

    public bool Success { get; init; }

    public string? PromptHash { get; init; }

    public GenerationJobResult? ApplyResult { get; init; }
}

internal static class LocalUtilityInferenceLegRunner
{
    public static async Task<LocalUtilityInferenceLegResult> TryRunAsync(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        UtilityExecutionChannel channel,
        string? conversationId,
        CancellationToken cancellationToken = default)
    {
        if (!LocalUtilityInferencePolicy.ShouldRunLocalLeg(bundle, jobId, context))
            return new LocalUtilityInferenceLegResult();

        var attempt = await LocalUtilityInferenceService.TryCompleteAsync(
            bundle,
            jobId,
            context,
            cancellationToken);
        if (!attempt.Attempted)
            return new LocalUtilityInferenceLegResult();

        if (!attempt.Success)
        {
            return new LocalUtilityInferenceLegResult
            {
                Attempted = true,
                Success = false,
                ApplyResult = new GenerationJobResult
                {
                    Success = false,
                    Error = attempt.Error ?? "local_inference_failed",
                    RanOnLocalInference = true,
                },
            };
        }

        var runId = Guid.NewGuid();
        context.InferenceSource = UtilityLane.LocalLlm;
        context.UtilityRunId = runId;

        var applyResult = UtilityJobLocalInferenceApplicator.ApplyLocalLeg(
            bundle,
            jobId,
            context,
            channel,
            runId,
            attempt.Prompt!,
            attempt.PromptHash!,
            attempt.ResponseText,
            conversationId);

        return new LocalUtilityInferenceLegResult
        {
            Attempted = true,
            Success = true,
            PromptHash = attempt.PromptHash,
            ApplyResult = applyResult,
        };
    }

    public static GenerationJobResult MergeDualRunResults(
        GenerationJobResult? localResult,
        GenerationJobResult remoteResult)
    {
        var localCount = localResult?.ProposalCount ?? 0;
        var remoteCount = remoteResult.ProposalCount;
        var localInferenceOk = localResult?.RanOnLocalInference == true && localResult.Success;
        var localParseOk = localResult is null || string.IsNullOrWhiteSpace(localResult.Error);
        var remoteOk = remoteResult.Success && remoteResult.Error is null;

        var errors = new List<string>();
        if (localResult is { RanOnLocalInference: true, Success: false } && !string.IsNullOrWhiteSpace(localResult.Error))
            errors.Add($"local: {localResult.Error}");
        if (!remoteOk && !string.IsNullOrWhiteSpace(remoteResult.Error))
            errors.Add($"utility: {remoteResult.Error}");
        if (localInferenceOk && !localParseOk && !string.IsNullOrWhiteSpace(localResult?.Error))
            errors.Add($"local parse: {localResult.Error}");

        return new GenerationJobResult
        {
            Success = localInferenceOk || remoteOk,
            ProposalCount = localCount + remoteCount,
            Error = errors.Count > 0 ? string.Join("; ", errors) : null,
            SkippedReason = remoteResult.SkippedReason,
            DisplayText = remoteResult.DisplayText,
            ProposalIds = (localResult?.ProposalIds ?? []).Concat(remoteResult.ProposalIds).ToList(),
            StoryContextSource = remoteResult.StoryContextSource ?? localResult?.StoryContextSource,
            StoryContextTurnPairs = remoteResult.StoryContextTurnPairs != 0
                ? remoteResult.StoryContextTurnPairs
                : localResult?.StoryContextTurnPairs ?? 0,
            StoryContextCharCount = remoteResult.StoryContextCharCount != 0
                ? remoteResult.StoryContextCharCount
                : localResult?.StoryContextCharCount ?? 0,
            StoryContextStatusHint = remoteResult.StoryContextStatusHint ?? localResult?.StoryContextStatusHint,
            RanOnLocalInference = localResult is not null,
            RanOnUtilityWorker = remoteResult.RanOnUtilityWorker,
            RanDualInference = true,
        };
    }
}

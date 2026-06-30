using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityJobLocalInferenceApplicator
{
    public static GenerationJobResult AttachStoryContext(
        GenerationJobResult result,
        UtilityStoryContextBuildResult storyContext) =>
        WithStoryContext(result, storyContext);

    public static GenerationJobResult ApplyLocalLeg(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        UtilityExecutionChannel channel,
        Guid runId,
        string prompt,
        string promptHash,
        string? responseText,
        string? conversationId)
    {
        var validation = UtilityResponseSchemaRegistry.Validate(jobId, responseText);
        var applyPayload = validation.Payload ?? ContextTagFormat.UnwrapUtilityJobResponse(responseText);
        var applyError = validation.Ok ? null : validation.Error;
        var applyResult = GenerationJobHandlers.ApplyResponse(bundle, jobId, applyPayload, applyError, context);
        applyResult = EnrichLocalApplyResult(jobId, responseText, applyResult);

        var pending = new PendingUtilityInjection
        {
            RunId = runId,
            JobId = jobId,
            Channel = channel,
            TurnIndex = context.Turn?.Index,
            LinkedTurnId = context.Turn?.Id,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            CardId = context.CardId,
            QueuedAt = DateTimeOffset.UtcNow,
            ContextManifest = context.UtilityContextManifest?.ToRecord(),
        };

        UtilityJobResultStore.SaveRun(
            bundle,
            pending,
            responseText,
            validation,
            applyResult,
            conversationId,
            promptHash,
            sentMessageId: null,
            assistantMessageId: null,
            UtilityLane.LocalLlm,
            streamComplete: true,
            pushedAt: DateTimeOffset.UtcNow,
            context.UtilityContextManifest?.ToRecord(),
            context.DualRunGroupId);

        UtilityParseLogService.Append(
            bundle,
            jobId,
            applyPayload,
            applyResult.ProposalCount,
            applyResult.Error ?? validation.Error,
            applyResult.ProposalIds);

        bundle.UtilityExchanges.Exchanges.Add(new UtilityExchangeRecord
        {
            JobId = jobId,
            PromptHash = promptHash,
            ResponseText = responseText,
            ConversationId = conversationId,
        });

        RecordLastError(bundle, jobId, applyResult, validation.Error);

        if (applyResult.ProposalCount > 0)
            AdventureStore.SaveReviewDomains(bundle);

        AdventureStore.Save(bundle);

        return new GenerationJobResult
        {
            Success = applyResult.Success,
            ProposalCount = applyResult.ProposalCount,
            Error = applyResult.Error,
            SkippedReason = applyResult.SkippedReason,
            DisplayText = applyResult.DisplayText,
            ProposalIds = applyResult.ProposalIds,
            RanOnLocalInference = true,
        };
    }

    public static void SaveInlineUtilityRun(
        AdventureBundle bundle,
        string jobId,
        GenerationJobContext context,
        Guid runId,
        string prompt,
        string promptHash,
        string? responseText,
        string? conversationId,
        GenerationJobResult applyResult)
    {
        var validation = UtilityResponseSchemaRegistry.Validate(jobId, responseText);
        var pending = new PendingUtilityInjection
        {
            RunId = runId,
            JobId = jobId,
            Channel = UtilityExecutionChannel.ManualBackground,
            TurnIndex = context.Turn?.Index,
            LinkedTurnId = context.Turn?.Id,
            EntityId = context.EntityId,
            EntityKind = context.EntityKind,
            CardId = context.CardId,
            QueuedAt = DateTimeOffset.UtcNow,
            ContextManifest = context.UtilityContextManifest?.ToRecord(),
        };

        UtilityJobResultStore.SaveRun(
            bundle,
            pending,
            responseText,
            validation,
            applyResult,
            conversationId,
            promptHash,
            sentMessageId: null,
            assistantMessageId: null,
            UtilityLane.PlayLegacyInline,
            streamComplete: true,
            pushedAt: DateTimeOffset.UtcNow,
            context.UtilityContextManifest?.ToRecord(),
            context.DualRunGroupId);
    }

    public static GenerationJobResult ApplyWorker(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        string prompt,
        string promptHash,
        string? responseText,
        string? conversationId)
    {
        var validation = UtilityResponseSchemaRegistry.Validate(entry.JobId, responseText);
        var applyPayload = validation.Payload ?? ContextTagFormat.UnwrapUtilityJobResponse(responseText);
        var applyError = validation.Ok ? null : validation.Error;
        var applyResult = GenerationJobHandlers.ApplyResponse(
            bundle,
            entry.JobId,
            applyPayload,
            applyError,
            context);

        var pending = new PendingUtilityInjection
        {
            RunId = entry.RunId,
            JobId = entry.JobId,
            Channel = entry.Channel,
            LinkedTurnId = entry.LinkedTurnId,
            TurnIndex = entry.TurnIndex,
            EntityId = entry.EntityId,
            EntityKind = entry.EntityKind,
            CardId = entry.CardId,
            QueuedAt = entry.QueuedAt,
        };

        UtilityJobResultStore.SaveRun(
            bundle,
            pending,
            responseText,
            validation,
            applyResult,
            conversationId,
            promptHash,
            sentMessageId: null,
            assistantMessageId: null,
            UtilityLane.Worker,
            streamComplete: true,
            pushedAt: DateTimeOffset.UtcNow,
            context.UtilityContextManifest?.ToRecord(),
            context.DualRunGroupId);

        UtilityParseLogService.Append(
            bundle,
            entry.JobId,
            applyPayload,
            applyResult.ProposalCount,
            applyResult.Error ?? validation.Error,
            applyResult.ProposalIds);

        UtilityWorkerSessionService.RecordJobCompleted(bundle.Metadata, applyResult.Success);
        RecordLastError(bundle, entry.JobId, applyResult, validation.Error);

        if (applyResult.ProposalCount > 0)
            AdventureStore.SaveReviewDomains(bundle);

        AdventureStore.Save(bundle);

        return new GenerationJobResult
        {
            Success = applyResult.Success,
            ProposalCount = applyResult.ProposalCount,
            Error = applyResult.Error ?? validation.Error,
            SkippedReason = applyResult.SkippedReason,
            DisplayText = applyResult.DisplayText,
            ProposalIds = applyResult.ProposalIds,
            RanOnUtilityWorker = true,
            RanOnLocalInference = false,
        };
    }

    private static GenerationJobResult EnrichLocalApplyResult(
        string jobId,
        string? responseText,
        GenerationJobResult applyResult)
    {
        if (applyResult.ProposalCount > 0 || string.IsNullOrWhiteSpace(responseText))
            return applyResult;

        var assessment = LocalUtilityResponseDiagnostics.Assess(jobId, responseText, applyResult.ProposalCount);
        if (assessment.ComplianceLabel is "compliant")
            return applyResult;

        var error = applyResult.Error ?? "no_proposals_parsed";
        if (assessment.ComplianceLabel is "schema_mismatch" && !string.IsNullOrWhiteSpace(assessment.ComplianceHint))
            error = $"{error}; {assessment.ComplianceHint}";

        return new GenerationJobResult
        {
            Success = applyResult.Success,
            ProposalCount = applyResult.ProposalCount,
            Error = error,
            SkippedReason = applyResult.SkippedReason,
            DisplayText = applyResult.DisplayText ?? responseText,
            ProposalIds = applyResult.ProposalIds,
            RanOnLocalInference = true,
        };
    }

    private static GenerationJobResult WithStoryContext(
        GenerationJobResult result,
        UtilityStoryContextBuildResult storyContext) =>
        new()
        {
            Success = result.Success,
            ProposalCount = result.ProposalCount,
            Error = result.Error,
            SkippedReason = result.SkippedReason,
            DisplayText = result.DisplayText,
            ProposalIds = result.ProposalIds,
            StoryContextSource = storyContext.TranscriptSource,
            StoryContextTurnPairs = storyContext.TurnPairCount,
            StoryContextCharCount = storyContext.Text.Length,
            StoryContextStatusHint = storyContext.Text.Length > 0 ? storyContext.FormatStatusHint() : null,
            RanOnLocalInference = result.RanOnLocalInference,
            RanDualInference = result.RanDualInference,
            RanOnUtilityWorker = result.RanOnUtilityWorker,
        };

    private static void RecordLastError(
        AdventureBundle bundle,
        string jobId,
        GenerationJobResult applyResult,
        string? validationError)
    {
        bundle.Metadata.UtilityJobLastErrors ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var utilityJobId = GenerationJobHandlers.GetUtilityJobId(jobId);
        if (!applyResult.Success || (applyResult.ProposalCount == 0 && applyResult.Error is not null))
            bundle.Metadata.UtilityJobLastErrors[utilityJobId] =
                applyResult.Error ?? applyResult.SkippedReason ?? validationError ?? "failed";
        else
            bundle.Metadata.UtilityJobLastErrors.Remove(utilityJobId);
    }
}

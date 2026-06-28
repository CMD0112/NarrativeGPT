using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Drains the utility worker outbox via unified transport.</summary>
internal static class UtilityWorkerOrchestrator
{
    public static async Task<GenerationJobResult?> ProcessNextAsync(
        AdventureBundle bundle,
        CoreWebView2 workerCore,
        CoreWebView2? playCore,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService? playTurnService,
        AdventureTurnService? workerTurnService = null,
        IUtilityWorkerHost? workerHost = null,
        CancellationToken cancellationToken = default)
    {
        var entry = UtilityOutboxService.PeekNext(bundle);
        if (entry is null)
            return null;

        if (!UtilityWorkerCapabilityGate.IsGreen(bundle))
        {
            return new GenerationJobResult
            {
                Success = false,
                SkippedReason = "worker_not_ready",
                Error = bundle.Metadata.UtilityWorkerCapabilities?.LastProbeError ?? "worker_capabilities_not_green",
                RanOnUtilityWorker = true,
            };
        }

        var context = BuildJobContext(bundle, entry);

        if (UtilityJobContextAssembler.IsEnabled(bundle, UtilityExecutionChannel.WorkerBackground))
        {
            UtilityStoryContextBuilder? storyBuilder = null;
            if (playCore is not null && playTurnService is not null)
            {
                var transcriptService = new PlayThreadTranscriptService(conversationSend, playTurnService);
                storyBuilder = new UtilityStoryContextBuilder(transcriptService);
            }

            var assembler = new UtilityJobContextAssembler(storyBuilder);
            var assembly = await assembler.AssembleAsync(
                bundle,
                entry.JobId,
                new UtilityContextAssemblyRequest
                {
                    Channel = UtilityExecutionChannel.WorkerBackground,
                    JobContext = context,
                    PlayCore = playCore,
                },
                cancellationToken);
            assembly.ApplyTo(context);
        }
        else
        {
            string? storyBlock = null;
            var storyHasTranscript = false;
            if (playCore is not null && playTurnService is not null)
            {
                var transcriptService = new PlayThreadTranscriptService(conversationSend, playTurnService);
                var builder = new UtilityStoryContextBuilder(transcriptService);
                var story = await builder.BuildAsync(
                    bundle,
                    entry.JobId,
                    playCore,
                    cancellationToken,
                    domOnlyCapture: true);
                storyBlock = story.Text;
                storyHasTranscript = story.TurnPairCount > 0;
            }

            context.StoryContextBlock = storyBlock;
            context.StoryContextHasTranscript = storyHasTranscript;
        }

        if (entry.State == UtilityJobRunState.Queued)
        {
            var push = await UtilityMessagePushService.PushAsync(
                workerCore,
                bundle,
                entry,
                context,
                conversationSend,
                workerTurnService,
                workerHost,
                cancellationToken: cancellationToken);

            entry.PromptHash = push.PromptHash;
            if (!push.Success)
            {
                entry.State = UtilityJobRunState.Failed;
                entry.PushError = push.Error;
                entry.CompletedAt = DateTimeOffset.UtcNow;
                UtilityOutboxService.Update(bundle, entry);
                AdventureStore.Save(bundle);
                return new GenerationJobResult
                {
                    Success = false,
                    Error = push.Error,
                    RanOnUtilityWorker = true,
                };
            }

            entry.State = UtilityJobRunState.Pushed;
            entry.SentMessageId = push.SentMessageId;
            entry.AssistantMessageId = push.AssistantMessageId;
            entry.PartialAssistantText = push.AssistantText;
            entry.StreamComplete = push.StreamComplete;
            entry.PushedAt = DateTimeOffset.UtcNow;
            UtilityOutboxService.Update(bundle, entry);
        }

        entry.State = UtilityJobRunState.Pulling;
        UtilityOutboxService.Update(bundle, entry);

        var conversationId = UtilityWorkerSessionService.GetWorkerConversationId(bundle)!;
        var pull = await UtilityMessagePullService.PullAndApplyAsync(
            workerCore,
            bundle,
            entry,
            context,
            conversationSend,
            conversationId,
            entry.SentMessageId,
            entry.PartialAssistantText,
            entry.StreamComplete,
            cancellationToken);

        if (pull.Success)
        {
            entry.State = UtilityJobRunState.Complete;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            UtilityOutboxService.RemoveCompleted(bundle, entry.RunId);
        }
        else
        {
            entry.State = UtilityJobRunState.Failed;
            entry.PullError = pull.Error;
            entry.CompletedAt = DateTimeOffset.UtcNow;
            UtilityOutboxService.Update(bundle, entry);
        }

        AdventureStore.Save(bundle);
        if (pull.ApplyResult?.ProposalCount > 0)
            AdventureStore.Save(bundle, AdventureSaveScope.Summary);

        return pull.ApplyResult ?? new GenerationJobResult
        {
            Success = false,
            Error = pull.Error,
            RanOnUtilityWorker = true,
        };
    }

    public static GenerationJobContext BuildJobContext(AdventureBundle bundle, UtilityOutboxEntry entry)
    {
        TurnRecord? turn = null;
        if (entry.LinkedTurnId is { } turnId)
            turn = bundle.Log.Turns.FirstOrDefault(t => t.Id == turnId);

        UtilityTranscriptScope? scope = null;
        if (entry.JobId is GenerationJobId.ExtractEntities
            or GenerationJobId.ProposeMemories
            or GenerationJobId.ProcessTurn)
        {
            scope = UtilityTranscriptScopeService.ResolveFromLocalLog(bundle)
                    ?? UtilityTranscriptScopeService.ResolveFallbackTurn(bundle);
        }

        return new GenerationJobContext
        {
            Turn = turn,
            Scope = scope,
            EntityId = entry.EntityId,
            EntityKind = entry.EntityKind,
            CardId = entry.CardId,
            SuppressInlineGuide = true,
        };
    }
}

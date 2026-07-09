using ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.Adventure.Services.UtilityWorker;

using ChatGPTWrapper.Adventure.Stores;

using ChatGPTWrapper.ChatGptApi;

using Microsoft.Web.WebView2.Core;



namespace ChatGPTWrapper.Adventure.Services;



/// <summary>Drains the utility worker outbox via API transport and local story context.</summary>

internal static class UtilityWorkerOrchestrator

{

    public static Task<GenerationJobResult?> ProcessNextAsync(

        AdventureBundle bundle,

        CoreWebView2 workerCore,

        CoreWebView2? playCore,

        ChatGptConversationSendService conversationSend,

        AdventureTurnService? playTurnService,

        AdventureTurnService? workerTurnService = null,

        IUtilityWorkerHost? workerHost = null,

        CancellationToken cancellationToken = default) =>

        UtilityWorkerJobRunner.RunNextAsync(

            bundle,

            workerCore,

            conversationSend,

            workerTurnService ?? playTurnService
            ?? throw new InvalidOperationException("Utility worker turn service required."),

            workerHost,

            cancellationToken);



    public static Task<GenerationJobResult> RunDirectJobAsync(

        AdventureBundle bundle,

        string jobId,

        GenerationJobContext context,

        UtilityExecutionChannel channel,

        CoreWebView2 workerCore,

        CoreWebView2? playCore,

        ChatGptConversationSendService conversationSend,

        AdventureTurnService? playTurnService,

        AdventureTurnService? workerTurnService = null,

        IUtilityWorkerHost? workerHost = null,

        bool skipLocalLeg = false,

        CancellationToken cancellationToken = default) =>

        UtilityWorkerJobRunner.RunDirectAsync(

            bundle,

            jobId,

            context,

            channel,

            workerCore,

            conversationSend,

            workerTurnService ?? playTurnService
            ?? throw new InvalidOperationException("Utility worker turn service required."),

            workerHost,

            skipLocalLeg,

            cancellationToken);



    public static GenerationJobContext BuildJobContext(AdventureBundle bundle, UtilityOutboxEntry entry)

    {

        TurnRecord? turn = null;

        if (entry.LinkedTurnId is { } turnId)

            turn = bundle.Log.Turns.FirstOrDefault(t => t.Id == turnId);



        UtilityTranscriptScope? scope = null;

        if (entry.JobId is GenerationJobId.ExtractEntities
            or GenerationJobId.ProposeMemories
            or GenerationJobId.ProcessTurn
            or GenerationJobId.ProposeEntitiesFile)
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
            UserPrompt = entry.UserPrompt,
            AttachmentReferenceNote = entry.AttachmentReferenceNote,
            SuppressInlineGuide = true,
            UtilityRunId = entry.RunId,

            JobAttachments = entry.Attachments is { Count: > 0 }
                ? UtilityJobAttachmentStaging.ToAttachmentContext(bundle.Metadata.Id, entry.Attachments)
                : null,

        };

    }

}



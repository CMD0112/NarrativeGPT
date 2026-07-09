using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>
/// Utility job dispatch logging: play-thread ingest at job boundary, context projection artifacts,
/// flight-recorder correlation, and ephemeral job capture (L1).
/// </summary>
internal static class UtilityJobLoggingHooks
{
    public static void BeforeDispatch(AdventureBundle bundle, string jobId, GenerationJobContext context)
    {
        if (context.UtilityRunId is not { } runId || runId == Guid.Empty)
        {
            runId = Guid.NewGuid();
            context.UtilityRunId = runId;
        }

        var playEntry = ThreadConversationLogReader.GetActiveEntry(bundle, AdventureThreadKind.Play);
        if (playEntry is null)
            return;

        var priorProjection = ThreadProjectionService.Resolve(bundle.Metadata.Id, playEntry.Id);
        if (priorProjection.Messages.Count == 0)
            return;

        var correlation = new ThreadSnapshotCorrelation
        {
            UtilityRunId = runId,
            TurnId = context.Turn?.Id,
        };

        var ingest = ThreadIngestService.RecordBranchProjectionIngest(
            bundle,
            playEntry,
            priorProjection.Messages,
            ThreadConversationLogCaptureSource.WorkerDispatch,
            ThreadConversationLogSnapshotTrigger.WorkerDispatch,
            correlation,
            synthetic: false);

        var projection = ThreadProjectionService.Resolve(bundle.Metadata.Id, playEntry.Id);
        context.PlayThreadEntryId = playEntry.Id;
        context.PlayThreadIngestEventId = ingest.EventId;
        context.PlayThreadRawPath = ingest.RawPath ?? projection.RawPath;
        context.PlayThreadProjectionPath = ingest.ProjectionPath ?? projection.ProjectionPath;

        var contextPath = UtilityJobResultStore.WriteContextProjection(
            bundle.Metadata.Id,
            runId,
            jobId,
            playEntry.Id,
            ingest.EventId,
            context.PlayThreadRawPath,
            context.PlayThreadProjectionPath,
            projection,
            context.Turn?.Id);
        context.ContextProjectionPath = contextPath;

        if (context.UtilityContextManifest is { } manifest)
        {
            context.UtilityContextManifest = manifest.WithThreadProjection(projection, playEntry.Id);
        }
    }

    public static void ApplyLoggingMetadata(UtilityJobRunRecord record, GenerationJobContext context)
    {
        record.PlayThreadIngestEventId = context.PlayThreadIngestEventId;
        record.PlayThreadEntryId = context.PlayThreadEntryId;
        record.PlayThreadRawPath = context.PlayThreadRawPath;
        record.PlayThreadProjectionPath = context.PlayThreadProjectionPath;
        record.ContextProjectionPath = context.ContextProjectionPath;
        record.SourceIoInputPath = context.SourceIoInputPath;
        record.EphemeralCapturePath = context.EphemeralCapturePath;

        if (context.UtilityContextManifest?.ThreadIngestEventId is { } manifestIngest
            && record.PlayThreadIngestEventId is null)
        {
            record.PlayThreadIngestEventId = manifestIngest;
        }
    }

    public static void RecordEphemeralJobCapture(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        GenerationJobContext context,
        EphemeralProjectChatResult ephemeralResult,
        string? packetText)
    {
        var capturePath = UtilityJobResultStore.WriteEphemeralCapture(
            bundle.Metadata.Id,
            entry.RunId,
            entry.JobId,
            ephemeralResult.ConversationId,
            entry.PromptHash,
            ephemeralResult.ResponseText,
            ephemeralResult.StreamComplete);
        context.EphemeralCapturePath = capturePath;

        if (!string.IsNullOrWhiteSpace(ephemeralResult.ConversationId)
            && !string.IsNullOrWhiteSpace(packetText))
        {
            FlightRecordCaptureService.CaptureWorkerUtilitySend(
                bundle,
                entry,
                new UtilityPushResult
                {
                    Success = ephemeralResult.Success,
                    Error = ephemeralResult.Error,
                    PromptHash = entry.PromptHash,
                    PacketText = packetText,
                    StreamComplete = ephemeralResult.StreamComplete,
                    DeliveryLane = UtilityAttachmentDeliveryLane.None,
                });
            AdventureStore.Save(bundle);
        }
    }

    public static void LinkWorkerFlightRecord(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        UtilityPushResult push)
    {
        if (string.IsNullOrWhiteSpace(push.PacketText) && string.IsNullOrWhiteSpace(push.PromptHash))
            return;

        FlightRecordCaptureService.CaptureWorkerUtilitySend(bundle, entry, push);
        AdventureStore.Save(bundle);
    }
}

using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;

namespace ChatGPTWrapper.Adventure.Services;

internal static class FlightRecordCaptureService
{
    public static PromptHistoryEntry CapturePlaySend(
        AdventureBundle bundle,
        TurnRecord turn,
        PreparedSendArtifact artifact,
        FlightDeliverySnapshot delivery,
        Guid? playSendTraceRunId = null,
        IReadOnlyList<PendingUtilityInjection>? utilityDispatches = null)
    {
        var dispatches = utilityDispatches ?? [];
        var entry = new PromptHistoryEntry
        {
            TurnId = turn.Id,
            At = DateTimeOffset.UtcNow,
            Kind = FlightRecordKind.PlaySend,
            PlayerLine = artifact.PlayerLine,
            PacketText = artifact.MergedText,
            PacketHash = artifact.Hash,
            Injection = BuildInjectionSnapshot(artifact),
            Delivery = delivery,
            PlaySendTraceRunId = playSendTraceRunId?.ToString("D"),
            UtilityJobIds = dispatches.Select(d => d.RunId).ToList(),
            UtilityRuns = dispatches.Select(ToUtilityRunSnapshot).ToList(),
        };

        bundle.PromptHistory.Entries.Add(entry);
        PromptHistoryMigration.EnsureCurrentSchema(bundle.PromptHistory);
        turn.PromptPacketHash = artifact.Hash;
        LinkUtilityRunsToFlightRecord(bundle, entry);
        return entry;
    }

    public static PromptHistoryEntry CaptureWorkerUtilitySend(
        AdventureBundle bundle,
        UtilityOutboxEntry entry,
        UtilityPushResult push)
    {
        var fileNames = entry.Attachments?.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList()
                        ?? [];
        var entryRecord = new PromptHistoryEntry
        {
            Kind = FlightRecordKind.WorkerUtilitySend,
            At = DateTimeOffset.UtcNow,
            WorkerJobId = entry.JobId,
            PlayerLine = $"worker:{entry.JobId}",
            PacketText = push.PacketText ?? "",
            PacketHash = push.PromptHash,
            AttachmentDeliveryLane = UtilityAttachmentDeliveryClassifier.FormatLaneLabel(push.DeliveryLane),
            AttachmentFiles = fileNames,
            UtilityJobIds = [entry.RunId],
            Delivery = new FlightDeliverySnapshot
            {
                Channel = push.DeliveryLane.ToString(),
                Outcome = push.Success ? "pushed" : "failed",
                FailureCode = push.Error,
                ConversationId = UtilityWorkerSession.GetConversationId(bundle),
                Verified = false,
            },
        };

        bundle.PromptHistory.Entries.Add(entryRecord);
        PromptHistoryMigration.EnsureCurrentSchema(bundle.PromptHistory);
        UtilityJobResultStore.TryLinkFlightRecord(bundle.Metadata.Id, entry.RunId, entryRecord.Id);
        return entryRecord;
    }

    public static FlightInjectionSnapshot BuildInjectionSnapshot(PreparedSendArtifact artifact) =>
        new()
        {
            Profile = artifact.Profile.ToString(),
            DelegationMode = artifact.DelegationMode.ToString(),
            AttachmentMode = artifact.AttachmentSendMode.ToString(),
            WasTrimmed = artifact.WasTrimmed,
            MergedCharCount = artifact.MergedText.Length,
            ContextCharCount = artifact.ContextText.Length,
            HasUtilityInjection = artifact.HasUtilityInjection,
            UtilitySectionCount = artifact.UtilitySectionCount,
            Sections = artifact.Sections.Select(ToSectionRecord).ToList(),
            Trimmed = artifact.Trimmed.Select(t => new FlightTrimmedSectionRecord
            {
                Id = t.Id,
                Reason = t.Reason,
            }).ToList(),
            BaselinePointers = artifact.BaselinePointers.Select(ToPointerRecord).ToList(),
            ThisTurnPointers = artifact.ThisTurnPointers.Select(ToPointerRecord).ToList(),
        };

    private static FlightContextPointerRecord ToPointerRecord(ContextPointer pointer) =>
        new()
        {
            MachineId = pointer.MachineId,
            FileName = pointer.FileName,
            SectionId = pointer.SectionId,
            Title = pointer.Title,
            Kind = pointer.Kind,
            Score = pointer.Score,
            Source = pointer.Source.ToString(),
            Mode = pointer.Mode.ToString(),
        };

    private static FlightInjectionSectionRecord ToSectionRecord(InjectionSection section) =>
        new()
        {
            Id = section.Id,
            Kind = section.Kind.ToString(),
            Mandatory = section.Mandatory,
            Included = section.Included,
            Note = section.Note,
            CharEstimate = section.CharEstimate,
            OmissionReason = section.OmissionReason.ToString(),
        };

    private static FlightUtilityRunSnapshot ToUtilityRunSnapshot(PendingUtilityInjection pending) =>
        new()
        {
            RunId = pending.RunId,
            JobId = pending.JobId,
            Channel = pending.Channel.ToString(),
            ContextManifest = pending.ContextManifest,
        };

    private static void LinkUtilityRunsToFlightRecord(AdventureBundle bundle, PromptHistoryEntry entry)
    {
        foreach (var runId in entry.UtilityJobIds)
            UtilityJobResultStore.TryLinkFlightRecord(bundle.Metadata.Id, runId, entry.Id);
    }
}

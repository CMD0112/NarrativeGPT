using System.IO;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

internal static class UtilityJobResultStore
{
    private static string ResultsDirectory(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "utility-results");

    private static string IndexPath(Guid adventureId) =>
        Path.Combine(AppDirectories.AdventureDirectory(adventureId), "utility-results-index.json");

    public static void Save(
        AdventureBundle bundle,
        PendingUtilityInjection pending,
        string? rawResponse,
        UtilitySchemaValidation validation,
        GenerationJobResult applyResult,
        string? conversationId = null,
        string? promptHash = null) =>
        SaveRun(
            bundle,
            pending,
            rawResponse,
            validation,
            applyResult,
            conversationId,
            promptHash,
            sentMessageId: null,
            assistantMessageId: null,
            lane: pending.Channel == UtilityExecutionChannel.WorkerBackground
                ? UtilityLane.Worker
                : UtilityLane.PlayInjection,
            streamComplete: true,
            pushedAt: null,
            contextManifest: pending.ContextManifest);

    public static void SaveRun(
        AdventureBundle bundle,
        PendingUtilityInjection pending,
        string? rawResponse,
        UtilitySchemaValidation validation,
        GenerationJobResult applyResult,
        string? conversationId,
        string? promptHash,
        string? sentMessageId,
        string? assistantMessageId,
        string lane,
        bool streamComplete,
        DateTimeOffset? pushedAt,
        UtilityContextManifestRecord? contextManifest = null,
        Guid? dualRunGroupId = null)
    {
        var record = new UtilityJobRunRecord
        {
            RunId = pending.RunId == Guid.Empty ? Guid.NewGuid() : pending.RunId,
            JobId = pending.JobId,
            SchemaVersion = ContextTagFormat.UtilityTagSchemaVersion,
            Trigger = ChannelToTrigger(pending.Channel),
            LinkedTurnIndex = pending.TurnIndex,
            ConversationId = conversationId,
            PromptHash = promptHash,
            RawResponse = rawResponse,
            ParsedPayload = validation.Payload,
            ProposalIds = applyResult.ProposalIds,
            ProposalCount = applyResult.ProposalCount,
            Error = applyResult.Error ?? validation.Error,
            SentMessageId = sentMessageId,
            AssistantMessageId = assistantMessageId,
            Lane = lane,
            StreamComplete = streamComplete,
            PushedAt = pushedAt,
            State = applyResult.Success ? UtilityJobRunState.Complete : UtilityJobRunState.Failed,
            ContextManifest = contextManifest ?? pending.ContextManifest,
            DualRunGroupId = dualRunGroupId,
        };

        WriteRecord(bundle.Metadata.Id, record);
        TryLinkFlightRecordFromBundle(bundle, pending.RunId);
    }

    public static void TryLinkFlightRecord(Guid adventureId, Guid utilityRunId, Guid flightRecordId)
    {
        var record = LoadRun(adventureId, utilityRunId);
        if (record is null || record.LinkedFlightRecordId == flightRecordId)
            return;

        record.LinkedFlightRecordId = flightRecordId;
        WriteRecordFile(adventureId, record);
    }

    private static void TryLinkFlightRecordFromBundle(AdventureBundle bundle, Guid utilityRunId)
    {
        var flightRecordId = FlightRecordCorrelationService.FindFlightRecordIdForUtilityRun(bundle, utilityRunId);
        if (flightRecordId is not Guid entryId)
            return;

        TryLinkFlightRecord(bundle.Metadata.Id, utilityRunId, entryId);
    }

    private static void WriteRecord(Guid adventureId, UtilityJobRunRecord record)
    {
        WriteRecordFile(adventureId, record);

        var index = LoadIndex(adventureId);
        if (!index.RunsByJobId.TryGetValue(record.JobId, out var runs))
        {
            runs = [];
            index.RunsByJobId[record.JobId] = runs;
        }

        if (!runs.Contains(record.RunId))
            runs.Add(record.RunId);

        if (runs.Count > 50)
            runs.RemoveRange(0, runs.Count - 50);

        File.WriteAllText(IndexPath(adventureId), JsonSerializer.Serialize(index, AdventureJson.Options));
    }

    private static void WriteRecordFile(Guid adventureId, UtilityJobRunRecord record)
    {
        var dir = ResultsDirectory(adventureId);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{record.RunId}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(record, AdventureJson.Options));
    }

    public static UtilityJobRunRecord? LoadRun(Guid adventureId, Guid runId)
    {
        var path = Path.Combine(ResultsDirectory(adventureId), $"{runId}.json");
        if (!File.Exists(path))
            return null;

        try
        {
            return JsonSerializer.Deserialize<UtilityJobRunRecord>(File.ReadAllText(path), AdventureJson.Options);
        }
        catch
        {
            return null;
        }
    }

    public static UtilityJobResultsIndex LoadIndex(Guid adventureId)
    {
        var path = IndexPath(adventureId);
        if (!File.Exists(path))
            return new UtilityJobResultsIndex();

        try
        {
            return JsonSerializer.Deserialize<UtilityJobResultsIndex>(File.ReadAllText(path), AdventureJson.Options)
                   ?? new UtilityJobResultsIndex();
        }
        catch
        {
            return new UtilityJobResultsIndex();
        }
    }

    public static void MarkRunReviewResolved(Guid adventureId, Guid runId)
    {
        var run = LoadRun(adventureId, runId);
        if (run is null || run.ReviewResolvedAt.HasValue)
            return;

        run.ReviewResolvedAt = DateTimeOffset.UtcNow;
        WriteRecordFile(adventureId, run);
    }

    public static void MarkReviewResolved(Guid adventureId, string jobId)
    {
        var index = LoadIndex(adventureId);
        if (!index.RunsByJobId.TryGetValue(jobId, out var runIds))
            return;

        foreach (var runId in runIds)
        {
            var run = LoadRun(adventureId, runId);
            if (run is null
                || run.ReviewResolvedAt.HasValue
                || run.ProposalCount <= 0
                || run.State != UtilityJobRunState.Complete)
            {
                continue;
            }

            run.ReviewResolvedAt = DateTimeOffset.UtcNow;
            WriteRecordFile(adventureId, run);
        }
    }

    private static string ChannelToTrigger(UtilityExecutionChannel channel) =>
        channel switch
        {
            UtilityExecutionChannel.AutoBackground => "auto",
            UtilityExecutionChannel.ManualBackground => "manual",
            UtilityExecutionChannel.WorkerBackground => "worker",
            _ => "manual",
        };
}

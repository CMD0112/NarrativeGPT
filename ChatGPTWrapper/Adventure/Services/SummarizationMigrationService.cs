using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ChatGPTWrapper.Adventure.Models;

namespace ChatGPTWrapper.Adventure.Services;

public sealed class SummarizationMigrationCheckpoint
{
    public string CheckpointHash { get; init; } = "";

    public int TurnCount { get; init; }

    public int ThreadMessageCount { get; init; }

    public string RollingSummary { get; init; } = "";

    public string MigrationPacket { get; init; } = "";
}

public static class SummarizationMigrationService
{
    public static SummarizationMigrationCheckpoint BuildCheckpoint(AdventureBundle bundle)
    {
        var turns = bundle.Log.Turns.Count(t => t.Status == TurnStatus.Accepted);
        var summary = bundle.Summary.RollingSummary ?? "";
        var messages = ThreadMetadataService.ActiveMessages(bundle).Count;

        var packet = $"""
            === MIGRATION CHECKPOINT ===
            Adventure: {bundle.Metadata.Title}
            Accepted turns: {turns}
            Thread messages: {messages}

            === ROLLING SUMMARY ===
            {summary}

            === RECENT TRANSCRIPT ===
            {UtilityStoryContextBuilder.FormatTranscript(
                ThreadMetadataService.ToTranscriptPairs(bundle),
                bundle.Metadata.Settings.UtilityStoryContext)}
            """;

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packet)));

        return new SummarizationMigrationCheckpoint
        {
            CheckpointHash = hash,
            TurnCount = turns,
            ThreadMessageCount = messages,
            RollingSummary = summary,
            MigrationPacket = packet,
        };
    }

    public static string ComputePacketHash(string packet) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(packet ?? "")));

    public static void SaveCheckpointSidecar(AdventureBundle bundle, SummarizationMigrationCheckpoint checkpoint)
    {
        var path = Path.Combine(bundle.DirectoryPath, "migration-checkpoint.json");
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(checkpoint, AdventureJson.Options));
    }
}

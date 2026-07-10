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
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var options = new PlayHandoffOptions();
        var handoff = PlayHandoffService.BuildCheckpoint(bundle, snapshot, options);

        return new SummarizationMigrationCheckpoint
        {
            CheckpointHash = handoff.CheckpointHash,
            TurnCount = handoff.TurnCount,
            ThreadMessageCount = handoff.ThreadMessageCount,
            RollingSummary = handoff.RollingSummary,
            MigrationPacket = handoff.HandoffPacket,
        };
    }

    public static string ComputePacketHash(string packet) =>
        PlayHandoffService.ComputePacketHash(packet);

    public static void SaveCheckpointSidecar(AdventureBundle bundle, SummarizationMigrationCheckpoint checkpoint)
    {
        PlayHandoffService.SaveCheckpointSidecar(
            bundle,
            new PlayHandoffCheckpoint
            {
                SchemaVersion = 2,
                CheckpointHash = checkpoint.CheckpointHash,
                TurnCount = checkpoint.TurnCount,
                ThreadMessageCount = checkpoint.ThreadMessageCount,
                RollingSummary = checkpoint.RollingSummary,
                HandoffPacket = checkpoint.MigrationPacket,
            });
    }
}

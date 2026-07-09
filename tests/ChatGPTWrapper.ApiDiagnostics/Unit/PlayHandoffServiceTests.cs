using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Collection(FileLockAwareCollectionNames.Name)]
[Trait("Category", "Unit")]
public sealed class PlayHandoffServiceTests : IClassFixture<FileLockAwareFixture>
{
    private static AdventureBundle CreateBundleWithTurns(string conversationId, int turnCount)
    {
        var bundle = AdventureStore.CreateNew("Handoff test");
        bundle.Metadata.LinkedProjectId = "g-p-handoff";
        bundle.Metadata.LinkedConversationId = conversationId;
        AdventureSessionService.EnsureSession(bundle);

        for (var i = 0; i < turnCount; i++)
        {
            bundle.Log.Turns.Add(new TurnRecord
            {
                Index = i + 1,
                PlayerText = $"Player line {i + 1}",
                NarratorText = $"Narrator line {i + 1}",
                Status = TurnStatus.Accepted,
                ConversationId = conversationId,
                SessionId = bundle.CurrentSessionId,
            });
        }

        bundle.Summary.RollingSummary = "The party reached the old mill.";
        return bundle;
    }

    [Fact]
    public void CaptureSnapshot_includes_prior_thread_turns()
    {
        var bundle = CreateBundleWithTurns("conv-a", 4);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);

        Assert.Equal("conv-a", snapshot.PriorConversationId);
        Assert.Equal(4, snapshot.AcceptedTurnCount);
        Assert.Equal(4, snapshot.TranscriptTurns.Count);
        Assert.Equal(4, snapshot.AdventureTurnOrdinal);
    }

    [Fact]
    public void BuildHandoffPacket_includes_continuation_meta()
    {
        var bundle = CreateBundleWithTurns("conv-a", 3);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var packet = PlayHandoffService.BuildHandoffPacket(bundle, snapshot, new PlayHandoffOptions());

        Assert.Contains("continuation=\"true\"", packet, StringComparison.Ordinal);
        Assert.Contains("turn=\"1\"", packet, StringComparison.Ordinal);
        Assert.Contains("adventureTurn=\"3\"", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHandoffPacket_summary_only_omits_transcript()
    {
        var bundle = CreateBundleWithTurns("conv-a", 5);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var packet = PlayHandoffService.BuildHandoffPacket(
            bundle,
            snapshot,
            new PlayHandoffOptions { Mode = PlayHandoffMode.SummaryOnly });

        Assert.Contains("The party reached the old mill.", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("Player line 1", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildHandoffPacket_transcript_mode_includes_pairs()
    {
        var bundle = CreateBundleWithTurns("conv-a", 8);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var packet = PlayHandoffService.BuildHandoffPacket(
            bundle,
            snapshot,
            new PlayHandoffOptions { Mode = PlayHandoffMode.SummaryWithTranscript });

        Assert.Contains("Player line 8", packet, StringComparison.Ordinal);
        Assert.Contains("Narrator line 8", packet, StringComparison.Ordinal);
        Assert.DoesNotContain("Player line 1", packet, StringComparison.Ordinal);
    }

    [Fact]
    public void ReleaseThenHandoff_turn_scope_empty_after_rotation()
    {
        var bundle = CreateBundleWithTurns("conv-a", 3);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var packet = PlayHandoffService.BuildHandoffPacket(bundle, snapshot, new PlayHandoffOptions());

        Assert.Contains("Player line 3", packet, StringComparison.Ordinal);

        PlayThreadRotationService.ReleasePlayThread(bundle);

        Assert.Empty(PlayTurnScopeService.GetPacketContextTurns(bundle));
        Assert.NotEmpty(packet);
    }

    [Fact]
    public void Checkpoint_roundtrip_hash_verify()
    {
        var bundle = CreateBundleWithTurns("conv-a", 2);
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var checkpoint = PlayHandoffService.BuildCheckpoint(bundle, snapshot, new PlayHandoffOptions());

        PlayHandoffService.SaveCheckpointSidecar(bundle, checkpoint);
        Assert.True(PlayHandoffService.TryLoadCheckpointSidecar(bundle, out var loaded));
        Assert.NotNull(loaded);
        Assert.True(PlayHandoffService.VerifyCheckpointHash(loaded!, loaded!.HandoffPacket));
        Assert.Equal(checkpoint.TurnCount, loaded!.TurnCount);
    }

    [Fact]
    public void TryRollbackPendingHandoff_restores_prior_binding()
    {
        var bundle = CreateBundleWithTurns("conv-a", 2);
        bundle.Metadata.PinnedPlayTabKey = "tab-1";
        bundle.Metadata.PinnedPlayTabTitle = "Play";
        var snapshot = PlayHandoffService.CaptureSnapshot(bundle);
        var checkpoint = PlayHandoffService.BuildCheckpoint(bundle, snapshot, new PlayHandoffOptions());
        PlayHandoffService.SaveCheckpointSidecar(bundle, checkpoint);

        PlayThreadRotationService.ReleasePlayThread(bundle);
        Assert.Null(bundle.Metadata.LinkedConversationId);

        Assert.True(PlayHandoffService.TryRollbackPendingHandoff(bundle));
        Assert.Equal("conv-a", PlayThreadBindingService.GetActiveConversationId(bundle));
        Assert.Equal("tab-1", PlayTabPinService.GetPlayPinKey(bundle));
    }
}

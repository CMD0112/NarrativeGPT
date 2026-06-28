using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PreparedSendArtifactTests
{
    [Fact]
    public void Builder_produces_artifact_with_packet_hash()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-artifact", inSync: true);
        const string playerLine = "open the gate";

        var artifact = PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = playerLine,
            ApplySurfaceActions = false,
            PriorThreadUserMessageCount = 2,
            ResolvePlayerLine = (_, _, text) => text ?? "",
        });

        Assert.NotNull(artifact);
        Assert.Equal(playerLine, artifact!.PlayerLine);
        Assert.Contains(playerLine, artifact.MergedText);
        Assert.False(string.IsNullOrWhiteSpace(artifact.Hash));
        Assert.False(string.IsNullOrWhiteSpace(artifact.SettingsFingerprint));
    }

    [Fact]
    public void Store_marks_stale_after_settings_change()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-stale", inSync: true);
        var store = new PreparedSendArtifactStore();
        store.Bind(bundle);

        var artifact = PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = "probe the room",
            ResolvePlayerLine = (_, _, text) => text ?? "",
        });
        Assert.NotNull(artifact);
        store.Set(artifact);

        Assert.True(store.CanSend);

        bundle.Metadata.Settings.MaxPacketChars = bundle.Metadata.Settings.MaxPacketChars - 1;
        Assert.True(store.IsStale);
        Assert.False(store.CanSend);
        Assert.Null(store.RequireForSend());
    }

    [Fact]
    public void Builder_matches_PlayPacketPrepareSession_output()
    {
        var bundle = AdventureTestData.CreateLinkedBundle(projectId: "g-p-parity", inSync: true);
        const string playerLine = "listen at the door";

        var session = PlayPacketPrepareSession.Prepare(
            new PlayPacketPrepareRequest
            {
                Bundle = bundle,
                ComposeText = playerLine,
                ApplySurfaceActions = false,
                PriorThreadUserMessageCount = 1,
            },
            (_, _, _) => playerLine);

        var artifact = PreparedSendArtifactBuilder.TryBuild(new PreparedSendArtifactRequest
        {
            Bundle = bundle,
            ComposeText = playerLine,
            ApplySurfaceActions = false,
            PriorThreadUserMessageCount = 1,
            ResolvePlayerLine = (_, _, text) => text ?? "",
        });

        Assert.NotNull(artifact);
        Assert.Equal(session.Prepared.MergedText, artifact!.MergedText);
        Assert.Equal(session.Prepared.Hash, artifact.Hash);
    }
}

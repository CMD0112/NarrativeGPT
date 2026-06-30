using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class PlayPacketContextSnapshotBuilderTests
{
    [Fact]
    public void Build_detects_summary_state_and_transcript_in_context_text()
    {
        const string context = """
            [[cgw:summary v="1"]]
            The hero entered the crypt.
            [[/cgw:summary]]

            [[cgw:state v="1"]]
            Location: crypt
            [[/cgw:state]]

            [[cgw:transcript v="1"]]
            PLAYER: go in
            NARRATOR: darkness
            [[/cgw:transcript]]
            """;

        var snapshot = PlayPacketContextSnapshotBuilder.Build(context, playPacketText: context);

        Assert.True(snapshot.IncludesRollingSummary);
        Assert.True(snapshot.IncludesState);
        Assert.True(snapshot.TranscriptTailChars > 0);
    }

    [Fact]
    public void Build_detects_legacy_fat_packet_markers()
    {
        const string play = """
            === STORY SO FAR ===
            A long campaign.

            === CURRENT STATE ===
            Location: tower

            === RECENT TRANSCRIPT ===
            PLAYER: knock
            NARRATOR: echo
            """;

        var snapshot = PlayPacketContextSnapshotBuilder.Build(contextText: "", play);

        Assert.True(snapshot.IncludesRollingSummary);
        Assert.True(snapshot.IncludesState);
        Assert.True(snapshot.TranscriptTailChars > 0);
    }
}

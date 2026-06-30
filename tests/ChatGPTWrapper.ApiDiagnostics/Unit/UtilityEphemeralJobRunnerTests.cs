using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.UtilityWorker;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

[Trait("Category", "Unit")]
public sealed class UtilityEphemeralJobRunnerTests
{
    [Fact]
    public void ResolveApplyError_prefers_capture_error_over_empty_response_validation()
    {
        var validation = UtilityResponseSchemaRegistry.Validate(
            GenerationJobId.ProcessTurn,
            responseText: null);

        Assert.False(validation.Ok);
        Assert.Equal("empty_response", validation.Error);

        var resolved = UtilityEphemeralJobRunner.ResolveApplyError(
            validation,
            captureError: "create:http_405",
            responseText: null);

        Assert.Equal("create:http_405", resolved);
    }

    [Fact]
    public void ResolveApplyError_returns_null_when_response_valid()
    {
        var validation = UtilityResponseSchemaRegistry.Validate(
            GenerationJobId.ProcessTurn,
            """{"ok":true}""");

        var resolved = UtilityEphemeralJobRunner.ResolveApplyError(
            validation,
            captureError: null,
            responseText: """{"ok":true}""");

        Assert.Null(resolved);
    }

    [Fact]
    public void FormatEphemeralFailure_includes_phase_and_error()
    {
        var formatted = UtilityEphemeralJobRunner.FormatEphemeralFailure(
            new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Create,
                Error = "http_405",
            });

        Assert.Equal("create:http_405", formatted);
    }

    [Fact]
    public void TryBuildEmbedOnlyFallbackPacket_returns_false_for_packet_embed_lane()
    {
        var bundle = AdventureTestData.CreateLinkedBundle();
        var entry = new UtilityOutboxEntry
        {
            RunId = Guid.NewGuid(),
            JobId = GenerationJobId.ExtractEntities,
            Channel = UtilityExecutionChannel.ManualBackground,
        };
        var context = new GenerationJobContext();
        var packet = new UtilityEphemeralAttachmentSendService.PreparedPacket(
            "wrapped",
            "hash",
            UtilityAttachmentDeliveryLane.PacketEmbed,
            null,
            ForceDomAttach: false);

        var ok = UtilityEphemeralJobRunner.TryBuildEmbedOnlyFallbackPacket(
            bundle,
            entry,
            context,
            packet,
            out _);

        Assert.False(ok);
    }
}

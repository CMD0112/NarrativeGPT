using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

internal static class DeliveryVerifier
{
    public static async Task<DeliveryVerification> VerifyAsync(
        AdventureTurnService turnService,
        CoreWebView2 core,
        PreparedSendArtifact artifact,
        AdventureTurnResult result,
        int priorUserTurnCount,
        PlayDeliveryChannel channel,
        CancellationToken cancellationToken = default)
    {
        var traceChannel = channel == PlayDeliveryChannel.Api ? "api" : "dom";
        PlaySendTraceMapper.LogVerifyStart(traceChannel, artifact.Hash, priorUserTurnCount);

        if (!result.Success)
        {
            return LogAndReturn(
                DeliveryVerification.Failed("delivery_failed", traceChannel),
                artifact.Hash);
        }

        var afterCount = await turnService.GetUserTurnCountAsync(core, cancellationToken);
        var delta = afterCount - priorUserTurnCount;
        if (delta < 1)
        {
            return LogAndReturn(
                DeliveryVerification.Failed("turn_count_unchanged", traceChannel),
                artifact.Hash);
        }

        if (channel != PlayDeliveryChannel.Api
            && !string.IsNullOrWhiteSpace(artifact.Hash)
            && !string.IsNullOrWhiteSpace(result.PacketText)
            && !string.Equals(
                PlayHandoffService.ComputePacketHash(result.PacketText),
                artifact.Hash,
                StringComparison.OrdinalIgnoreCase))
        {
            return LogAndReturn(
                DeliveryVerification.Failed("packet_hash_mismatch", traceChannel),
                artifact.Hash);
        }

        return LogAndReturn(DeliveryVerification.Ok(delta, traceChannel), artifact.Hash);
    }

    private static DeliveryVerification LogAndReturn(DeliveryVerification verification, string? artifactHash)
    {
        PlaySendTraceMapper.LogVerifyResult(verification, artifactHash);
        return verification;
    }
}

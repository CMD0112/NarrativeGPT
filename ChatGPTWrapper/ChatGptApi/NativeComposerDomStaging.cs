using ChatGPTWrapper.Bridges;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ChatGptApi;

/// <summary>
/// Shared bridge + CDP staging for native composer DOM attachments (Play + utility worker).
/// </summary>
public static class NativeComposerDomStaging
{
    public sealed class StageResult
    {
        public bool HostCdpStaged { get; init; }

        public string? CdpError { get; init; }
    }

    public static async Task<StageResult> StageAttachmentsAsync(
        ChatGptAdventureBridgeInjection bridge,
        CoreWebView2 core,
        IReadOnlyList<DomAttachmentPayload> domAttachments,
        bool attachmentsPreStaged,
        CancellationToken cancellationToken = default)
    {
        if (attachmentsPreStaged || domAttachments is not { Count: > 0 })
            return new StageResult { HostCdpStaged = attachmentsPreStaged };

        await bridge.StageDomFallbackAttachmentsAsync(core, domAttachments);

        var cdpStage = await NativeComposerFileStaging.StageAsync(
            core,
            domAttachments,
            cancellationToken);

        return new StageResult
        {
            HostCdpStaged = cdpStage.Success,
            CdpError = cdpStage.Success ? null : cdpStage.Error,
        };
    }
}

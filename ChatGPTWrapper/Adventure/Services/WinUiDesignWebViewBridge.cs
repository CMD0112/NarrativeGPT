using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.WinUiBridge;

namespace ChatGPTWrapper.Adventure.Services;

/// <summary>Object-core entry points for WinUI design WebView hosts (CMD-555).</summary>
internal static class WinUiDesignWebViewBridge
{
    public static bool TryGetDesignConversationId(
        AdventureBundle bundle,
        object coreWebView2,
        out string? conversationId,
        out string? error) =>
        AdventureDesignDomChatService.TryGetDesignConversationId(
            bundle,
            WinUiWebView2CoreRuntime.RequireTypedCore(coreWebView2),
            out conversationId,
            out error);

    public static async Task<DesignChatSendResult> SendDesignPromptAsync(
        object coreWebView2,
        AdventureBundle bundle,
        AdventureTurnService turnService,
        string promptText,
        CancellationToken cancellationToken = default) =>
        await AdventureDesignDomChatService.SendPromptAsync(
            WinUiWebView2CoreRuntime.RequireTypedCore(coreWebView2),
            bundle,
            turnService,
            promptText,
            cancellationToken);

    public static async Task<GenerationJobResult> RunDesignExtractAsync(
        GenerationJobService service,
        object designCoreWebView2,
        AdventureBundle bundle,
        AdventureDesignStep step,
        AdventureTurnService designTurn,
        object? playCoreWebView2,
        AdventureTurnService? playTurn,
        CancellationToken cancellationToken = default) =>
        await service.RunJobAsync(
            WinUiWebView2CoreRuntime.RequireTypedCore(designCoreWebView2),
            bundle,
            GenerationJobId.DesignExtractStep,
            new GenerationJobContext
            {
                DesignStep = step,
                SuppressInlineGuide = true,
            },
            designTurn,
            playCoreWebView2 is null ? null : WinUiWebView2CoreRuntime.RequireTypedCore(playCoreWebView2),
            playTurn,
            cancellationToken: cancellationToken);

    public static async Task<DesignExtractResult> ExtractStepForWinUiAsync(
        Guid adventureId,
        AdventureDesignStep step,
        object designCoreWebView2,
        object? playCoreWebView2,
        ChatGptProjectApiService projectApi,
        ChatGptConversationSendService conversationSend,
        AdventureTurnService designTurn,
        AdventureTurnService? playTurn,
        CancellationToken cancellationToken = default)
    {
        var bundle = AdventureStore.Load(adventureId)
                     ?? throw new InvalidOperationException("Adventure not found.");

        var service = new GenerationJobService(projectApi, conversationSend);
        var result = await RunDesignExtractAsync(
            service,
            designCoreWebView2,
            bundle,
            step,
            designTurn,
            playCoreWebView2,
            playTurn,
            cancellationToken);

        return new DesignExtractResult
        {
            Success = result.Success,
            ProposalCount = result.ProposalCount,
            Error = result.Error ?? result.SkippedReason,
        };
    }
}

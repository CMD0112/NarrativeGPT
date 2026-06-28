using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services.PlaySend;

using ChatGPTWrapper;

/// <summary>
/// UI/runtime surface the play send orchestrator needs from <see cref="MainWindow"/>.
/// </summary>
internal interface IPlaySendHost
{
    Guid? ActiveAdventureId { get; }

    PreparedSendArtifactStore ArtifactStore { get; }

    Task<bool> TryAcquireSendGateAsync();

    void ReleaseSendGate();

    int IncrementActiveSendCount();

    void DecrementActiveSendCount();

    void OnSendFinally(ChatGptPlayComposeInjection? composeInjection);

    ChatGptPlayComposeInjection? GetActiveComposeInjection();

    PlayTabCapabilities ResolveCapabilities(AdventureBundle bundle, WebView2 webView);

    string ResolvePlayerInput(AdventureBundle bundle, bool consumeQueue, string? composeText);

    AttachmentContext? BuildAttachmentContext(
        PlayComposeSendEventArgs? sendRequest,
        IReadOnlyList<PlayComposePendingAttachment> pendingAttachments);

    void SyncPlayThreadScopeForPacket(AdventureBundle bundle);

    Task SyncComposeUiAsync(PlayComposeUiState state, ChatGptPlayComposeInjection? injection);

    Task SetComposeBusyAsync(bool busy, string? message, ChatGptPlayComposeInjection? injection);

    void SetComposeStatus(string? text, ChatGptPlayComposeInjection? injection);

    Task RestoreComposeInputAsync(string text, ChatGptPlayComposeInjection? injection);

    void SetMergedPreview(AdventureBundle bundle, string? mergedText);

    void ClearComposePrompt(ChatGptPlayComposeInjection? injection);

    Task EnsurePlayWebViewReadyAsync(
        Guid adventureId,
        bool selectTab,
        bool prepareContext,
        bool navigateToBrowseTarget);

    WebView2? PlayWebView { get; set; }

    AdventureTurnService? GetOrCreateTurnService(WebView2 webView);

    Task<PlayContextResult?> RequireLinkedPlayThreadForSendAsync(
        AdventureBundle bundle,
        CoreWebView2 core);

    Task PrefetchSendWarmupAsync(CoreWebView2 core, AdventureBundle bundle);

    PlaySendSourcesPromptResult PromptSourcesInlineFallback(string warnMessage);

    Task<AdventureTurnResult> DeliverPacketAsync(PlaySendDeliveryRequest request);

    Task<string> CompleteTurnAfterSendAsync(PlaySendTurnCompletionRequest request);

    void OnSendSucceeded(PlaySendSuccessRequest request);

    void ShowSendError(string message, bool isWarning = true);

    void CopyToClipboard(string text);

    void InvalidatePlayContext(Guid adventureId);

    string FormatMergedPreview(AdventureBundle bundle, string mergedText);
}

internal enum PlaySendSourcesPromptResult
{
    CancelSend,
    SendWithInlineFallback,
}

internal sealed record PlaySendDeliveryRequest(
    CoreWebView2 Core,
    AdventureBundle Bundle,
    PlayTabCapabilities Capabilities,
    AdventureTurnService TurnService,
    string PacketText,
    string? DisplayPlayerLine,
    string? PacketHash,
    IReadOnlyList<DomAttachmentPayload>? DomAttachments,
    bool AttachmentsPreStaged);

internal sealed record PlaySendTurnCompletionRequest(
    AdventureBundle Bundle,
    TurnRecord Turn,
    AdventureTurnResult SendResult,
    CoreWebView2 Core,
    AdventureTurnService TurnService,
    ChatGptPlayComposeInjection? ComposeInjection,
    int AssistantBaselineCount);

internal sealed record PlaySendSuccessRequest(
    Guid AdventureId,
    AdventureBundle Bundle,
    WebView2 PlayWebView,
    ChatGptPlayComposeInjection? ComposeInjection,
    string SuccessStatus,
    int MergedLength);

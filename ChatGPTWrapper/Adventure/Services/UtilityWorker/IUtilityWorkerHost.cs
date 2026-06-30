using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>
/// Shell-provided WebView and UI hooks for <see cref="UtilityWorkerCoordinator"/>.
/// Keeps bridge/WebView work off MainWindow gate logic.
/// </summary>
internal interface IUtilityWorkerHost
{
    ChatGptConversationSendService ConversationSend { get; }

    ChatGptProjectApiService? ProjectApi { get; }

    AdventureTurnService GetTurnService(WebView2 webView);

    void RegisterWorkerTab(WebView2 webView);

    Task<WebView2?> ResolveWorkerWebViewAsync(AdventureBundle bundle, CancellationToken cancellationToken = default);

    Task<WebView2?> EnsureWorkerTabReadyAsync(AdventureBundle bundle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps the worker WebView in an off-screen host so API work continues while play tab stays selected.
    /// When <paramref name="apiOnlyWarm"/> is true, only the HTTP bridge is warmed (production drain path).
    /// </summary>
    Task EnsureWorkerWebViewBackgroundHostedAsync(
        WebView2 workerWebView,
        bool apiOnlyWarm = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs worker DOM work on the shadow-compositor hosted WebView without switching the user's selected tab.
    /// </summary>
    Task<T> WithUtilityWebViewActivatedAsync<T>(
        CoreWebView2 workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);

    /// <summary>Prevents parking churn and activates shadow compositor hosting during DOM attachment sends.</summary>
    IDisposable BeginDomAttachmentSend();

    /// <summary>
    /// Legacy: unparks the utility tab for visible composer attach. Prefer <see cref="BeginDomAttachmentSend"/>.
    /// </summary>
    Task<T> WithUtilityComposerVisibleAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);

    WebView2? GetPlayWebView();

    void SetStatus(string message);

    void OnOutboxBatchCompleted(Guid adventureId, IReadOnlyList<UtilityOutboxJobResult> results);

    void RefreshPlayJobButtons();

    /// <summary>Opens project composer on the worker WebView for ephemeral per-job chats.</summary>
    Task<string?> TryCreateEphemeralConversationViaUiAsync(
        AdventureBundle bundle,
        CoreWebView2 core,
        CancellationToken cancellationToken = default);
}

internal sealed record UtilityOutboxJobResult(string JobId, GenerationJobResult? Result);

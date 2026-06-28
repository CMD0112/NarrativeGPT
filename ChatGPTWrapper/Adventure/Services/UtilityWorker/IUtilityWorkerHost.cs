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

    AdventureTurnService GetTurnService(WebView2 webView);

    void RegisterWorkerTab(WebView2 webView);

    Task<WebView2?> ResolveWorkerWebViewAsync(AdventureBundle bundle, CancellationToken cancellationToken = default);

    Task<WebView2?> EnsureWorkerTabReadyAsync(AdventureBundle bundle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps the worker WebView in an off-screen host so API/bridge work while play tab stays selected.
    /// </summary>
    Task EnsureWorkerWebViewBackgroundHostedAsync(
        WebView2 workerWebView,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserved for hosts that need a visible worker page; background outbox does not select tabs.
    /// </summary>
    Task<T> WithUtilityWebViewActivatedAsync<T>(
        CoreWebView2 workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);

    WebView2? GetPlayWebView();

    void SetStatus(string message);

    void OnOutboxBatchCompleted(Guid adventureId, IReadOnlyList<UtilityOutboxJobResult> results);

    void RefreshPlayJobButtons();
}

internal sealed record UtilityOutboxJobResult(string JobId, GenerationJobResult? Result);

using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;

/// <summary>
/// Shell-provided WebView and UI hooks for <see cref="UtilityWorkerCoordinator"/>.
/// Keeps bridge/WebView work off MainWindow gate logic.
/// </summary>
internal interface IUtilityWorkerHost
{
    ChatGptConversationSendService ConversationSend { get; }

    ChatGptProjectApiService? ProjectApi { get; }

    AdventureTurnService GetTurnService(object webView);

    void RegisterWorkerTab(object webView);

    Task<object?> ResolveWorkerWebViewAsync(AdventureBundle bundle, CancellationToken cancellationToken = default);

    Task<object?> EnsureWorkerTabReadyAsync(AdventureBundle bundle, CancellationToken cancellationToken = default);

    Task EnsureWorkerWebViewBackgroundHostedAsync(
        object workerWebView,
        bool apiOnlyWarm = false,
        CancellationToken cancellationToken = default);

    Task<T> WithUtilityWebViewActivatedAsync<T>(
        object workerCore,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);

    IDisposable BeginDomAttachmentSend();

    Task<T> WithUtilityComposerVisibleAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);

    object? GetPlayWebView();

    void SetStatus(string message);

    void OnOutboxBatchCompleted(Guid adventureId, IReadOnlyList<UtilityOutboxJobResult> results);

    void RefreshPlayJobButtons();

    Task<string?> TryCreateEphemeralConversationViaUiAsync(
        AdventureBundle bundle,
        object core,
        CancellationToken cancellationToken = default);

    object? GetWorkerCookieSource();

    Task<IReadOnlyList<object>> GetWorkerChatGptCookiesAsync(
        CancellationToken cancellationToken = default);
}

internal sealed record UtilityOutboxJobResult(string JobId, GenerationJobResult? Result);

using ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.Adventure.Services;

using ChatGPTWrapper.ChatGptApi;



namespace ChatGPTWrapper.Adventure.Services.UtilityWorker;



/// <summary>

/// Routes parallel-slot utility work through the slot WebView while reusing main-window API services.

/// </summary>

internal sealed class UtilityWorkerParallelJobHost : IUtilityWorkerHost

{

    private static readonly NoOpDisposable DomSendScope = new();



    private readonly IUtilityWorkerHost _main;

    private readonly UtilityWorkerParallelSlotLease _slot;



    public UtilityWorkerParallelJobHost(IUtilityWorkerHost main, UtilityWorkerParallelSlotLease slot)

    {

        _main = main;

        _slot = slot;

    }



    public ChatGptConversationSendService ConversationSend => _main.ConversationSend;



    public ChatGptProjectApiService? ProjectApi => _main.ProjectApi;



    public AdventureTurnService GetTurnService(object webView) => _slot.TurnService;



    public void RegisterWorkerTab(object webView)

    {

    }



    public Task<object?> ResolveWorkerWebViewAsync(AdventureBundle bundle, CancellationToken cancellationToken = default) =>

        Task.FromResult<object?>(_slot.WebView);



    public Task<object?> EnsureWorkerTabReadyAsync(AdventureBundle bundle, CancellationToken cancellationToken = default) =>

        Task.FromResult<object?>(_slot.WebView);



    public Task EnsureWorkerWebViewBackgroundHostedAsync(

        object workerWebView,

        bool apiOnlyWarm = false,

        CancellationToken cancellationToken = default) =>

        Task.CompletedTask;



    public Task<T> WithUtilityWebViewActivatedAsync<T>(

        object workerCore,

        Func<Task<T>> action,

        CancellationToken cancellationToken = default) =>

        _slot.Host.RunOnUiAsync(action, cancellationToken);



    public IDisposable BeginDomAttachmentSend() => DomSendScope;



    public Task<T> WithUtilityComposerVisibleAsync<T>(

        Func<Task<T>> action,

        CancellationToken cancellationToken = default) =>

        action();



    public object? GetPlayWebView() => _main.GetPlayWebView();



    public void SetStatus(string message) => _main.SetStatus(message);



    public void OnOutboxBatchCompleted(Guid adventureId, IReadOnlyList<UtilityOutboxJobResult> results) =>

        _main.OnOutboxBatchCompleted(adventureId, results);



    public void RefreshPlayJobButtons() => _main.RefreshPlayJobButtons();



    public Task<string?> TryCreateEphemeralConversationViaUiAsync(

        AdventureBundle bundle,

        object core,

        CancellationToken cancellationToken = default)

    {

        var projectApi = ProjectApi;

        if (projectApi is null)

            return Task.FromResult<string?>(null);



        return _slot.Host.RunOnUiAsync(

            () => UtilityEphemeralUiCreateService.TryOpenComposerAsync(

                bundle,

                _slot.Core,

                projectApi,

                _slot.TurnService,

                cancellationToken),

            cancellationToken);

    }



    public object? GetWorkerCookieSource() => _main.GetWorkerCookieSource();



    public Task<IReadOnlyList<object>> GetWorkerChatGptCookiesAsync(

        CancellationToken cancellationToken = default) =>

        _main.GetWorkerChatGptCookiesAsync(cancellationToken);



    private sealed class NoOpDisposable : IDisposable

    {

        public void Dispose()

        {

        }

    }

}



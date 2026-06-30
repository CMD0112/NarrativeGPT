using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private EphemeralProjectChatService? _ephemeralProjectChatService;

    /// <summary>
    /// One-shot linked-project chat for isolated testing (create → send → capture → delete).
    /// Does not update play thread, utility worker, or design session bindings.
    /// </summary>
    public async Task<EphemeralProjectChatResult> RunEphemeralProjectChatAsync(
        string messageText,
        WebView2? webView = null,
        bool useUiCreate = true,
        CancellationToken cancellationToken = default)
    {
        if (_activeAdventureId is not { } adventureId)
        {
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Create,
                Error = "no_active_adventure",
            };
        }

        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
        {
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Create,
                Error = "adventure_not_found",
            };
        }

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
        {
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Create,
                Error = "no_linked_project",
            };
        }

        var wv = webView ?? FindProjectApiWebView() ?? GetActiveWebView();
        if (wv?.CoreWebView2 is not { } core)
        {
            return new EphemeralProjectChatResult
            {
                Success = false,
                FailedPhase = EphemeralProjectChatPhase.Create,
                Error = "webview_not_ready",
            };
        }

        var service = GetOrCreateEphemeralProjectChatService(wv);
        Func<CoreWebView2, CancellationToken, Task<string?>>? tryUiCreate = useUiCreate
            ? (c, ct) => TryCreateEphemeralConversationViaUiAsync(adventureId, c, ct)
            : null;

        return await service.RunOnceAsync(
            new EphemeralProjectChatRequest
            {
                Core = core,
                GizmoId = gizmoId,
                MessageText = messageText,
                TryUiCreate = tryUiCreate,
                TurnService = GetOrCreateTurnService(wv),
                UiCreateOnly = false,
                WarmSession = true,
            },
            cancellationToken);
    }

    private EphemeralProjectChatService GetOrCreateEphemeralProjectChatService(WebView2 wv)
    {
        if (_ephemeralProjectChatService is not null)
            return _ephemeralProjectChatService;

        WireProjectServices(wv);
        _ephemeralProjectChatService = new EphemeralProjectChatService(
            _projectApiService ?? throw new InvalidOperationException("Project API service not ready."),
            _conversationSendService ?? throw new InvalidOperationException("Conversation send service not ready."));
        return _ephemeralProjectChatService;
    }

    private async Task<string?> TryCreateEphemeralConversationViaUiAsync(
        Guid adventureId,
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        var bundle = AdventureStore.Load(adventureId);
        if (bundle is null)
            return null;

        var gizmoId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(gizmoId))
            return null;

        gizmoId = ChatGptUrls.NormalizeGizmoId(gizmoId);

        var wv = FindWebViewForCore(core) ?? FindProjectApiWebView() ?? GetActiveWebView();
        if (wv is null)
            return null;

        await Dispatcher.InvokeAsync(() => SelectTabForWebView(wv));
        var turnService = GetOrCreateTurnService(wv);

        if (_projectApiService is not null)
            await _projectApiService.EnsureProjectPageAsync(core, gizmoId, cancellationToken);

        if (!await turnService.EnsureUtilityBridgeReadyAsync(core, cancellationToken))
            return null;

        var ui = await turnService.StartProjectChatAsync(core, cancellationToken);
        var conversationId = ui.ConversationId ?? await turnService.GetConversationIdAsync(core);

        ProjectLinkDiagnostics.Log(
            string.IsNullOrWhiteSpace(conversationId)
                ? "Ephemeral UI opened project composer (no /c/ URL yet)"
                : $"Ephemeral UI create succeeded: {conversationId}");
        return conversationId;
    }
}

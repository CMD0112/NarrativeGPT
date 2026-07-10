using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ChatGptApi;

public interface IChatGptSessionHost
{
    IChatGptProjectHost Project { get; }

    ChatGptConversationSendService ConversationSend { get; }

    AdventureTurnService GetTurnService(WebView2 webView);

    Task<ProjectSessionStatus> EnsureReadyAsync(
        Guid? adventureIdForFallbackTab = null,
        bool showBrowserPane = false,
        CancellationToken cancellationToken = default);
}

public sealed class ChatGptSessionHost : IChatGptSessionHost
{
    private readonly ChatGptProjectHost _projectHost;
    private readonly Func<WebView2, ChatGptAdventureBridgeInjection> _getAdventureBridge;
    private readonly Dictionary<WebView2, AdventureTurnService> _turnServices = new();

    public ChatGptSessionHost(
        ChatGptProjectHostDependencies dependencies,
        Func<WebView2, ChatGptAdventureBridgeInjection> getAdventureBridge,
        ChatGptConversationSendService conversationSend)
    {
        _projectHost = new ChatGptProjectHost(dependencies);
        _getAdventureBridge = getAdventureBridge;
        ConversationSend = conversationSend;
    }

    public IChatGptProjectHost Project => _projectHost;

    public ChatGptConversationSendService ConversationSend { get; private set; }

    public void SetConversationSendService(ChatGptConversationSendService conversationSend)
    {
        ConversationSend = conversationSend;
        foreach (var turnService in _turnServices.Values)
            turnService.SetConversationSendService(conversationSend);
    }

    public AdventureTurnService GetTurnService(object webViewHost) =>
        webViewHost is WebView2 wv ? GetTurnService(wv) : throw new ArgumentException("Expected WPF WebView2.", nameof(webViewHost));

    public AdventureTurnService GetTurnService(WebView2 webView)
    {
        if (!_turnServices.TryGetValue(webView, out var service))
        {
            service = new AdventureTurnService(_getAdventureBridge(webView));
            service.SetConversationSendService(ConversationSend);
            _turnServices[webView] = service;
        }
        else
        {
            service.SetConversationSendService(ConversationSend);
        }

        return service;
    }

    public Task<ProjectSessionStatus> EnsureReadyAsync(
        Guid? adventureIdForFallbackTab = null,
        bool showBrowserPane = false,
        CancellationToken cancellationToken = default) =>
        _projectHost.EnsureReadyAsync(adventureIdForFallbackTab, showBrowserPane, cancellationToken);
}

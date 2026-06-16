using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper;

public partial class MainWindow
{
    private ChatGptSessionHost? _sessionHost;

    public IChatGptSessionHost SessionHost => _sessionHost ??= CreateSessionHost();

    public IChatGptProjectHost ProjectHost => SessionHost.Project;

    private ChatGptSessionHost CreateSessionHost() =>
        new(
            CreateProjectHostDependencies(),
            GetOrRegisterAdventureBridge,
            _conversationSendService ?? new ChatGptConversationSendService(GetOrRegisterApiBridge(FindProjectApiWebView() ?? throw new InvalidOperationException("No API WebView available."))));

    private ChatGptProjectHostDependencies CreateProjectHostDependencies() =>
        new()
        {
            GetEnvironment = () => _chatWebViewEnvironment,
            FindWebView = FindProjectApiWebView,
            EnsureAdventureTabAsync = (id, select) => EnsurePlayWebViewForHostAsync(id, select),
            GetOrRegisterBridge = GetOrRegisterApiBridge,
            SelectTab = SelectTabForWebView,
            RequestShowBrowserPane = ShowProjectBrowserPane,
            WireServices = WireProjectServices,
        };

    private async Task<WebView2?> EnsurePlayWebViewForHostAsync(Guid adventureId, bool selectTab)
    {
        await EnsurePlaySessionAsync(adventureId, selectTab);
        return _playWebView;
    }
}

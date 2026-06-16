using ChatGPTWrapper.Adventure.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ChatGptApi;

public interface IChatGptProjectHost
{
    WebView2? ApiWebView { get; }

    CoreWebView2? ApiCore { get; }

    ChatGptProjectApiService Api { get; }

    AdventureProjectBindingService Binding { get; }

    ProjectSourceSyncService Sync { get; }

    ProjectFileSyncOrchestrator FileSync { get; }

    ProjectSessionStatus? LastSessionStatus { get; }

    bool TryEnterOperation();

    void ExitOperation();

    Task<ProjectSessionStatus> EnsureReadyAsync(
        Guid? adventureIdForFallbackTab = null,
        bool showBrowserPane = false,
        CancellationToken cancellationToken = default);

    Task<ProjectDiscoveryResult> DiscoverProjectsAsync(CancellationToken cancellationToken = default);

    Task<ApiProbeResult> ProbeSidebarAsync(CancellationToken cancellationToken = default);

    string GetDiagnosticsText();
}

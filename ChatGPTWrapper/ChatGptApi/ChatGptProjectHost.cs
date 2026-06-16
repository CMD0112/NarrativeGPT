using System.Text.Json;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ChatGptApi;

public sealed class ChatGptProjectHost : IChatGptProjectHost
{
    private readonly ChatGptProjectHostDependencies _deps;
    private readonly SemaphoreSlim _ensureGate = new(1, 1);
    private int _activeOperations;

    private WebView2? _apiWebView;
    private ChatGptApiBridgeInjection? _bridge;
    private ChatGptProjectApiService? _api;
    private AdventureProjectBindingService? _binding;
    private ProjectSourceSyncService? _sync;
    private ProjectFileSyncOrchestrator? _fileSync;
    private readonly ProjectDiscoveryService _discovery = new();

    public ChatGptProjectHost(ChatGptProjectHostDependencies deps)
    {
        _deps = deps ?? throw new ArgumentNullException(nameof(deps));
    }

    public WebView2? ApiWebView => _apiWebView;

    public CoreWebView2? ApiCore => _apiWebView?.CoreWebView2;

    public ChatGptProjectApiService Api =>
        _api ?? throw new InvalidOperationException("Call EnsureReadyAsync first.");

    public AdventureProjectBindingService Binding =>
        _binding ?? throw new InvalidOperationException("Call EnsureReadyAsync first.");

    public ProjectSourceSyncService Sync =>
        _sync ?? throw new InvalidOperationException("Call EnsureReadyAsync first.");

    public ProjectFileSyncOrchestrator FileSync =>
        _fileSync ?? throw new InvalidOperationException("Call EnsureReadyAsync first.");

    public ProjectSessionStatus? LastSessionStatus { get; private set; }

    public bool TryEnterOperation()
    {
        if (Interlocked.CompareExchange(ref _activeOperations, 1, 0) != 0)
            return false;

        return true;
    }

    public void ExitOperation() => Interlocked.Exchange(ref _activeOperations, 0);

    public async Task<ProjectSessionStatus> EnsureReadyAsync(
        Guid? adventureIdForFallbackTab = null,
        bool showBrowserPane = false,
        CancellationToken cancellationToken = default)
    {
        await _ensureGate.WaitAsync(cancellationToken);
        try
        {
            ProjectLinkDiagnostics.Log(
                $"EnsureReady adventure={adventureIdForFallbackTab?.ToString() ?? "none"} showPane={showBrowserPane}");

            if (!showBrowserPane
                && _apiWebView?.CoreWebView2 is { } cachedCore
                && _bridge?.IsRegistered == true
                && LastSessionStatus?.IsReady == true
                && IsChatGptPage(cachedCore))
            {
                try
                {
                    var ping = await _bridge.SendAsync(
                        cachedCore,
                        new { action = "ping" },
                        timeoutMs: 3000,
                        cancellationToken: cancellationToken,
                        skipReadyWait: true);
                    if (BridgeScriptJson.IsBridgeSuccess(ping))
                    {
                        ProjectLinkDiagnostics.Log("EnsureReady fast-path ok");
                        return LastSessionStatus!;
                    }
                }
                catch
                {
                    /* fall through to full prepare */
                }
            }

            if (showBrowserPane)
                _deps.RequestShowBrowserPane?.Invoke(adventureIdForFallbackTab);

            WebView2? wv = null;
            if (adventureIdForFallbackTab is { } linkedAdvId)
            {
                var bundle = AdventureStore.Load(linkedAdvId);
                if (PlayTabPinService.PreferPinnedPlayWebView(true, bundle)
                    || AdventurePlayContextService.PreferPinnedPlayWebView(true, bundle))
                {
                    ProjectLinkDiagnostics.Log("Play mode; ensuring pinned play tab for API");
                    wv = await _deps.EnsureAdventureTabAsync(linkedAdvId, true);
                }
            }

            wv ??= _deps.FindWebView();
            if (wv is null && adventureIdForFallbackTab is { } advId)
            {
                ProjectLinkDiagnostics.Log("No ChatGPT tab; ensuring play tab");
                wv = await _deps.EnsureAdventureTabAsync(advId, true);
            }

            if (wv is null)
            {
                LastSessionStatus = new ProjectSessionStatus
                {
                    IsReady = false,
                    Error = "No ChatGPT browser tab is available.",
                };
                return LastSessionStatus;
            }

            var env = _deps.GetEnvironment();
            if (env is null)
            {
                LastSessionStatus = new ProjectSessionStatus
                {
                    IsReady = false,
                    Error = "Browser is still starting. Wait a moment and try again.",
                };
                return LastSessionStatus;
            }

            if (wv.CoreWebView2 is null)
                await wv.EnsureCoreWebView2Async(env);

            var core = wv.CoreWebView2
                       ?? throw new InvalidOperationException("WebView2 failed to initialize.");

            if (!IsChatGptPage(core))
            {
                ProjectLinkDiagnostics.Log($"Navigating to chatgpt.com (was {core.Source})");
                core.Navigate("https://chatgpt.com");
                await WaitForChatGptNavigationAsync(core, 45000, cancellationToken);
            }

            await WaitForDocumentReadyAsync(core, cancellationToken);

            _apiWebView = wv;
            _deps.WireServices?.Invoke(wv);
            _bridge = _deps.GetOrRegisterBridge(wv);
            if (!_bridge.IsRegistered)
                _bridge.Register();

            await _bridge.WaitForBridgeReadyAsync(core, 45000, cancellationToken);

            _api = new ChatGptProjectApiService(_bridge);
            _sync = new ProjectSourceSyncService(_api);
            _binding = new AdventureProjectBindingService(_api, _sync);
            _fileSync = new ProjectFileSyncOrchestrator(_api, _sync);

            var ctxMsg = await _bridge.SendAsync(
                core,
                new { action = "getApiContext" },
                timeoutMs: 15000,
                cancellationToken: cancellationToken,
                skipReadyWait: true);

            var auth = false;
            var hasDevice = false;
            var hasAccount = false;
            string? userId = null;
            string? email = null;

            if (ctxMsg.Json is { } ctx)
            {
                auth = ReadBool(ctx, "authenticated");
                hasDevice = ReadBool(ctx, "hasDeviceId");
                hasAccount = ReadBool(ctx, "hasAccountId");
                userId = ReadString(ctx, "userId");
                email = ReadString(ctx, "email");
            }

            if (!hasAccount
                && ChatGptApiClientProfile.LoadHeaders().TryGetValue("ChatGPT-Account-Id", out var accountHdr)
                && !string.IsNullOrWhiteSpace(accountHdr))
                hasAccount = true;

            ProjectLinkDiagnostics.Log(
                $"ApiContext auth={auth} device={hasDevice} account={hasAccount} href={core.Source} via=script");

            string? error = null;
            if (!auth)
                error = "Not signed in to ChatGPT. Use the ChatGPT tab to log in, then click Test connection.";
            else if (!hasDevice)
                error =
                    "ChatGPT session is missing device cookies. Refresh chatgpt.com in the ChatGPT tab, then test again.";

            LastSessionStatus = new ProjectSessionStatus
            {
                IsReady = auth && hasDevice,
                IsAuthenticated = auth,
                HasDeviceId = hasDevice,
                HasAccountId = hasAccount,
                UserId = userId,
                Email = email,
                WebViewSource = core.Source,
                Error = error,
            };

            if (!string.IsNullOrEmpty(error))
                throw new ChatGptApiException(error, ChatGptApiEndpoints.Session, auth ? null : 401);

            _deps.SelectTab?.Invoke(wv);
            return LastSessionStatus;
        }
        catch (TimeoutException ex)
        {
            ProjectLinkDiagnostics.Log($"EnsureReady timeout: {ex.Message}");
            LastSessionStatus = new ProjectSessionStatus
            {
                IsReady = false,
                Error =
                    "ChatGPT did not respond in time. Open the ChatGPT browser tab, wait until the page finishes loading (and sign in if needed), then click Test connection.",
                WebViewSource = _apiWebView?.CoreWebView2?.Source,
            };
            throw new ChatGptApiException(LastSessionStatus.Error!, ChatGptApiEndpoints.Session);
        }
        catch (ChatGptApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            ProjectLinkDiagnostics.Log($"EnsureReady error: {ex.Message}");
            LastSessionStatus = new ProjectSessionStatus
            {
                IsReady = false,
                Error = ex.Message,
                WebViewSource = _apiWebView?.CoreWebView2?.Source,
            };
            throw;
        }
        finally
        {
            _ensureGate.Release();
        }
    }

    public Task<ProjectDiscoveryResult> DiscoverProjectsAsync(CancellationToken cancellationToken = default)
    {
        var core = ApiCore ?? throw new InvalidOperationException("WebView not ready.");
        return _discovery.DiscoverAsync(_api!, _bridge!, core, cancellationToken);
    }

    public Task<ApiProbeResult> ProbeSidebarAsync(CancellationToken cancellationToken = default)
    {
        var core = ApiCore ?? throw new InvalidOperationException("WebView not ready.");
        return _api!.ProbeSidebarAsync(core, cancellationToken);
    }

    public string GetDiagnosticsText() => ProjectLinkDiagnostics.BuildReport(LastSessionStatus);

    private static bool IsChatGptPage(CoreWebView2 core) =>
        Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
        && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri);

    private static async Task WaitForDocumentReadyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var raw = await core.ExecuteScriptAsync(
                    "(() => document.readyState === 'complete' || document.readyState === 'interactive')");
                if (raw.Contains("true", StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch
            {
                /* page may still be loading */
            }

            await Task.Delay(300, cancellationToken);
        }
    }

    private static async Task WaitForChatGptNavigationAsync(
        CoreWebView2 core,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (IsChatGptPage(core))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && IsChatGptPage(core))
                tcs.TrySetResult();
        }

        core.NavigationCompleted += Handler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        }
        catch (TimeoutException)
        {
            throw new ChatGptApiException(
                "ChatGPT did not finish loading in time. Open the ChatGPT tab, sign in, then try again.",
                ChatGptApiEndpoints.ProjectsSidebar);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static bool ReadBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static async Task SeedClientHeadersAsync(CoreWebView2 core)
    {
        var headers = ChatGptApiClientProfile.LoadHeaders();
        if (headers.Count == 0)
            return;

        var json = System.Text.Json.JsonSerializer.Serialize(headers);
        await core.ExecuteScriptAsync($"globalThis.__CHATGPT_CLIENT_HEADERS__ = {json};");
    }
}

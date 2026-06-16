using System.Diagnostics;
using System.Text.Json;
using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ApiDiagnostics.Reporting;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.ApiDiagnostics.Live;

public sealed class LiveApiDiagnosticRunner
{
    private readonly WebView2DiagnosticHost _host;

    public LiveApiDiagnosticRunner(WebView2DiagnosticHost host) => _host = host;

    public async Task<ApiDiagnosticReport> RunAsync(CancellationToken cancellationToken = default)
    {
        var report = new ApiDiagnosticReport
        {
            UserDataFolder = AppDirectories.WebView2UserDataDirectory,
        };

        try
        {
            await _host.RunOnUiAsync(
                () => RunChecklistOnUiAsync(report, cancellationToken),
                cancellationToken);
        }
        finally
        {
            report.WriteToDisk();
        }

        return report;
    }

    private async Task RunChecklistOnUiAsync(ApiDiagnosticReport report, CancellationToken cancellationToken)
    {
        await RunStep(report, "webview_init", () =>
        {
            _ = RequireCore();
            return Task.FromResult("CoreWebView2 ready");
        });

        await RunStep(report, "page_injectable", async () =>
        {
            var core = RequireCore();
            await NavigateChatGptAsync(core, cancellationToken);
            report.WebViewSource = core.Source;

            var readyDetail = await WaitForPageReadyAsync(core, cancellationToken);

            if (!Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
                || !ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            {
                throw new InvalidOperationException($"Not on chatgpt.com: {core.Source}");
            }

            return $"{core.Source} ({readyDetail})";
        });

        await RunStep(report, "bridge_asset_on_disk", () =>
        {
            var dir = Path.GetDirectoryName(typeof(ChatGptApiBridgeInjection).Assembly.Location)!;
            var path = Path.Combine(dir, "wrapper-assets", "chatgpt-api-bridge.js");
            if (!File.Exists(path))
                throw new FileNotFoundException("Bridge asset missing", path);
            return Task.FromResult<string>(path);
        });

        var scriptPingOk = false;

        await RunStep(report, "bridge_inject", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            await bridge.InjectAsync(core);
            var smoke = await core.ExecuteScriptAsync(
                "(function(){return JSON.stringify({type:'pong',ok:true});})()");
            var smokeJson = BridgeScriptJson.Normalize(smoke);
            var invokeType = await core.ExecuteScriptAsync("typeof globalThis.__cgwApiInvoke");
            if (!invokeType.Contains("function", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"__cgwApiInvoke type: {invokeType}");
            return $"smoke={smokeJson} invoke={invokeType.Trim()}";
        });

        await RunStep(report, "bridge_ping", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            try
            {
                await bridge.WaitForBridgeReadyAsync(core, 20000, cancellationToken);
                scriptPingOk = true;
                return "ping_ok";
            }
            catch (Exception ex)
            {
                scriptPingOk = false;
                throw new InvalidOperationException(ex.Message, ex);
            }
        });

        await RunStep(report, "bridge_echo", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var msg = await bridge.SendAsync(
                core,
                new { action = "echo", probe = "live" },
                timeoutMs: 10000,
                skipReadyWait: true,
                cancellationToken: cancellationToken);

            if (!msg.Ok || msg.Json is not { } json || !json.TryGetProperty("probe", out _))
                throw new InvalidOperationException(msg.Error ?? msg.Message ?? $"echo failed: {msg.RawJson}");

            return "echo_ok";
        });

        await RunStep(report, "bridge_postmessage_fallback", async () =>
        {
            if (scriptPingOk)
                return "skipped (script ping already succeeded)";

            var core = RequireCore();
            var bridge = RequireBridge();
            await bridge.InjectAsync(core);
            var msg = await bridge.SendViaPostMessageAsync(
                core,
                new { action = "ping" },
                10000,
                cancellationToken);

            if (!BridgeScriptJson.IsBridgeSuccess(msg))
            {
                throw new InvalidOperationException(
                    msg.Error ?? msg.Message ?? $"postMessage ping failed: {msg.Type ?? msg.RawJson}");
            }

            return $"type={msg.Type}";
        });

        await RunStep(report, "api_context", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var msg = await bridge.SendAsync(
                core,
                new { action = "getApiContext" },
                timeoutMs: 20000,
                skipReadyWait: true,
                cancellationToken: cancellationToken);

            if (!msg.Ok || msg.Json is not { } json)
                throw new InvalidOperationException(msg.Error ?? msg.Message ?? $"getApiContext failed: {msg.RawJson}");

            var auth = ReadBool(json, "authenticated");
            var device = ReadBool(json, "hasDeviceId");
            return $"auth={auth} device={device} href={ReadString(json, "href")}";
        });

        await RunStep(report, "session_endpoint", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var msg = await bridge.SendAsync(
                core,
                new { action = "apiRequest", method = "GET", path = ChatGptApiEndpoints.Session },
                timeoutMs: 20000,
                cancellationToken: cancellationToken);

            if (!msg.Ok)
                throw new InvalidOperationException(msg.Error ?? msg.Message ?? $"session status={msg.Status} raw={msg.RawJson}");

            return $"status={msg.Status}";
        });

        await RunStep(report, "device_cookie", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var msg = await bridge.SendAsync(
                core,
                new { action = "getApiContext" },
                timeoutMs: 20000,
                skipReadyWait: true,
                cancellationToken: cancellationToken);

            if (msg.Json is not { } json || !ReadBool(json, "hasDeviceId"))
                throw new InvalidOperationException("oai-did / hasDeviceId is false");

            return "hasDeviceId=true";
        });

        await RunStep(report, "probe_sidebar", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var api = new ChatGptProjectApiService(bridge);
            var probe = await api.ProbeSidebarAsync(core, cancellationToken);
            if (!probe.Ok)
                throw new InvalidOperationException(probe.Error ?? "sidebar probe failed");
            return $"status={probe.Status} items={probe.ItemCount} keys=[{string.Join(", ", probe.JsonKeys)}]";
        });

        await RunStep(report, "list_bootstrap", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var api = new ChatGptProjectApiService(bridge);
            var list = await api.ListProjectsFromBootstrapAsync(core, cancellationToken);
            return $"count={list.Count}";
        });

        await RunStep(report, "list_dom", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var api = new ChatGptProjectApiService(bridge);
            var list = await api.ListProjectsFromDomAsync(bridge, core, cancellationToken);
            return $"count={list.Count}";
        });

        await RunStep(report, "discovery_merge", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var api = new ChatGptProjectApiService(bridge);
            var discovery = new ProjectDiscoveryService();
            var result = await discovery.DiscoverAsync(api, bridge, core, cancellationToken);
            return $"projects={result.Projects.Count} strategies=[{string.Join(", ", result.StrategiesUsed)}] diag={result.Diagnostics}";
        });

        await RunStep(report, "project_attach_smoke", async () =>
        {
            var core = RequireCore();
            var bridge = RequireBridge();
            var api = new ChatGptProjectApiService(bridge);
            var projects = await api.ListProjectsViaSidebarOnlyAsync(core, cancellationToken);
            if (projects.Count == 0)
                return "skipped=no_projects";

            var target = projects[0];
            await api.EnsureProjectPageAsync(core, target.Id, cancellationToken);

            var testName = $"cgw-diag-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.md";
            var content = System.Text.Encoding.UTF8.GetBytes("# CGW attach diagnostic\n");
            var fileId = await api.UploadProjectFileAsync(
                core,
                target.Id,
                testName,
                content,
                "text/markdown",
                projectTitle: target.Title,
                cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(fileId))
                throw new InvalidOperationException("upload returned no file id");

            var remote = await api.GetProjectFilesDirectAsync(core, target.Id, cancellationToken);
            if (!remote.Any(f => string.Equals(f.FileId, fileId, StringComparison.Ordinal)))
                throw new InvalidOperationException($"file {fileId} not in remote list ({remote.Count} files)");

            return $"gizmo={target.Id} fileId={fileId} remoteCount={remote.Count}";
        });

        await RunStep(report, "client_profile", async () =>
        {
            var path = ChatGptApiClientProfile.ProfilePath;
            if (!File.Exists(path))
                throw new FileNotFoundException("api-client-profile.json missing", path);

            var text = await File.ReadAllTextAsync(path, cancellationToken);
            if (text.Length < 10)
                throw new InvalidOperationException("api-client-profile.json is empty");

            return $"{path} ({text.Length} chars)";
        });

        await RunStep(report, "existing_logs", () =>
        {
            var lines = new List<string>();
            AppendLogTail(lines, ProjectLinkDiagnostics.LogPath, 8);
            AppendLogTail(lines, ProjectDiscoveryService.TracePath, 5);
            var detail = lines.Count == 0 ? "no log files yet" : string.Join(" | ", lines);
            return Task.FromResult(detail);
        });
    }

    private async Task RunStep(ApiDiagnosticReport report, string id, Func<Task<string>> work)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var detail = await work();
            report.AddStep(new ApiDiagnosticStep
            {
                Id = id,
                Pass = true,
                DurationMs = sw.ElapsedMilliseconds,
                Detail = detail,
            });
        }
        catch (Exception ex)
        {
            report.WebViewSource ??= RequireCore().Source;

            report.AddStep(new ApiDiagnosticStep
            {
                Id = id,
                Pass = false,
                DurationMs = sw.ElapsedMilliseconds,
                Error = ex.Message,
                RawSnippet = BridgeScriptJson.Truncate(ex.ToString(), 300),
            });
        }
    }

    private static void AppendLogTail(List<string> lines, string path, int maxLines)
    {
        if (!File.Exists(path))
            return;

        var tail = File.ReadLines(path).TakeLast(maxLines).ToList();
        if (tail.Count == 0)
            return;

        lines.Add($"{Path.GetFileName(path)}: {tail[^1]}");
    }

    private static async Task NavigateChatGptAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
                return;

            if (Uri.TryCreate(core.Source, UriKind.Absolute, out var u)
                && ChatGptUrls.IsTrustedChatGptTopLevelUri(u))
                tcs.TrySetResult();
        }

        core.NavigationCompleted += Handler;
        try
        {
            core.Navigate("https://chatgpt.com");
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(90), cancellationToken);
        }
        finally
        {
            core.NavigationCompleted -= Handler;
        }
    }

    private static async Task<string> WaitForPageReadyAsync(CoreWebView2 core, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await core.ExecuteScriptAsync(
                "JSON.stringify({ready:document.readyState==='complete'||document.readyState==='interactive'," +
                "body:!!document.body,state:document.readyState,href:location.href})");

            if (TryParsePageProbe(raw, out var probe)
                && (probe.Ready || probe.HasBody))
                return $"readyState={probe.State} href={probe.Href}";

            await Task.Delay(500, cancellationToken);
        }

        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri)
            && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri))
            return $"fallback:trusted_url_without_readyState href={core.Source}";

        throw new TimeoutException(
            $"Page did not become ready within 60s (source={core.Source})");
    }

    private static bool TryParsePageProbe(string raw, out PageProbe probe)
    {
        probe = default;
        try
        {
            var json = BridgeScriptJson.Normalize(raw);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            probe = new PageProbe(
                root.TryGetProperty("ready", out var r) && r.ValueKind == JsonValueKind.True,
                root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.True,
                root.TryGetProperty("state", out var s) ? s.GetString() : null,
                root.TryGetProperty("href", out var h) ? h.GetString() : null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct PageProbe(bool Ready, bool HasBody, string? State, string? Href);

    private CoreWebView2 RequireCore() =>
        _host.Core ?? throw new InvalidOperationException("WebView2 core not ready");

    private ChatGptApiBridgeInjection RequireBridge() =>
        _host.Bridge ?? throw new InvalidOperationException("Bridge not registered");

    private static bool ReadBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
        && (p.ValueKind == JsonValueKind.True
            || (p.ValueKind == JsonValueKind.String
                && string.Equals(p.GetString(), "true", StringComparison.OrdinalIgnoreCase)));

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}

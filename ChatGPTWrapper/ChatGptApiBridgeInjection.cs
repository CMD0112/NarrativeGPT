using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChatGPTWrapper;

public sealed class ChatGptApiBridgeInjection
{
    /// <summary>Max CDP / script wait for long-running bridge work (upload finalize, batch attach).</summary>
    internal const int MaxBridgeTimeoutMs = 300_000;
    internal const string BridgeChannel = "cgw-api";

    private static string? _cachedScript;
    private static long _cachedStamp;

    /// <summary>When set (tests only), replaces wrapper-assets/chatgpt-api-bridge.js content.</summary>
    internal static string? TestBridgeScriptOverride { get; set; }

    private readonly WebView2 _webView;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ApiBridgeMessage>> _pending = new();
    private CoreWebView2? _registeredCore;
    private bool _messageHandlerAttached;
    private static string? _documentCreatedBootstrapVersion;
    private string? _documentCreatedScriptId;
    private readonly SemaphoreSlim _docScriptGate = new(1, 1);
    private static readonly ConditionalWeakTable<CoreWebView2, BridgeWarmState> WarmStates = new();

    private sealed class BridgeWarmState
    {
        public bool IsWarm { get; set; }
    }

    public bool IsWarm(CoreWebView2 core) =>
        WarmStates.TryGetValue(core, out var state) && state.IsWarm;

    public async Task EnsureWarmAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        if (IsWarm(core))
            return;

        await WaitForInjectablePageAsync(core, cancellationToken: cancellationToken);
        await InjectAsync(core);
        MarkWarm(core);
    }

    private static void MarkWarm(CoreWebView2 core)
    {
        var state = WarmStates.GetOrCreateValue(core);
        state.IsWarm = true;
    }

    private static void ClearWarm(CoreWebView2 core)
    {
        if (WarmStates.TryGetValue(core, out var state))
            state.IsWarm = false;
    }

    public bool IsRegistered => _messageHandlerAttached;

    public event EventHandler<ApiBridgeMessage>? MessageReceived;

    public ChatGptApiBridgeInjection(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
    }

    public void Register()
    {
        var core = _webView.CoreWebView2
                   ?? throw new InvalidOperationException("Call after CoreWebView2 is ready.");

        _registeredCore = core;
        core.Settings.IsWebMessageEnabled = true;

        if (!_messageHandlerAttached)
        {
            core.WebMessageReceived += OnWebMessageReceived;
            _messageHandlerAttached = true;
        }

        core.NavigationCompleted -= OnNavigationCompleted;
        core.NavigationCompleted += OnNavigationCompleted;
        ChatGptApiDiscovery.Register(core);
        _ = EnsureDocumentCreatedScriptAsync(core);
        _ = InjectAsync(core);
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 core || !e.IsSuccess)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        ClearWarm(core);
        await InjectAsync(core);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                json = e.WebMessageAsJson;

            if (string.IsNullOrWhiteSpace(json))
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var hasChannel = root.TryGetProperty("channel", out var ch)
                             && ch.ValueKind == JsonValueKind.String;
            if (hasChannel)
            {
                var channel = ch.GetString();
                if (!string.Equals(channel, BridgeChannel, StringComparison.Ordinal)
                    && !string.Equals(channel, "api", StringComparison.Ordinal))
                    return;
            }
            if (!hasChannel && root.TryGetProperty("type", out var t)
                && t.ValueKind == JsonValueKind.String
                && string.Equals(t.GetString(), "apiBridgeReady", StringComparison.Ordinal))
            {
                /* ready ping without channel */
            }
            else if (!hasChannel)
                return;

            var msg = new ApiBridgeMessage(json);

            if (root.TryGetProperty("id", out var idEl)
                && idEl.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(idEl.GetString()))
            {
                var id = idEl.GetString()!;
                if (_pending.TryRemove(id, out var tcs))
                    tcs.TrySetResult(msg);
            }
            else if (string.Equals(msg.Type, "apiBridgeReady", StringComparison.OrdinalIgnoreCase))
            {
                ProjectLinkDiagnostics.LogBridgeEvent("ready");
            }

            MessageReceived?.Invoke(this, msg);
        }
        catch
        {
            /* ignore malformed messages */
        }
    }

    public async Task InjectAsync(CoreWebView2? core = null)
    {
        core ??= _webView.CoreWebView2;
        if (core is null || !ChatGptPageGate.IsInjectable(core.Source))
            return;

        await SeedClientHeadersAsync(core);

        var bridgeState = await core.ExecuteScriptAsync(
            "JSON.stringify({invoke:typeof globalThis.__cgwApiInvoke==='function'," +
            "queue:typeof globalThis.__cgwApiStartCommand==='function'})");
        var needsInject = true;
        try
        {
            var stateJson = BridgeScriptJson.Normalize(bridgeState);
            if (!string.IsNullOrWhiteSpace(stateJson))
            {
                using var doc = JsonDocument.Parse(stateJson);
                var root = doc.RootElement;
                needsInject = !(root.TryGetProperty("invoke", out var invoke) && invoke.ValueKind == JsonValueKind.True
                                && root.TryGetProperty("queue", out var queue) && queue.ValueKind == JsonValueKind.True);
            }
        }
        catch
        {
            needsInject = true;
        }

        if (needsInject)
        {
            var script = GetBridgeScript();
            await core.ExecuteScriptAsync(script);
        }

        await core.ExecuteScriptAsync(
            "typeof globalThis.__cgwApiForceAttachListener==='function'&&globalThis.__cgwApiForceAttachListener()");
    }

    private static async Task SeedClientHeadersAsync(CoreWebView2 core)
    {
        var headers = ChatGptApiClientProfile.LoadHeaders();
        if (headers.Count == 0)
            return;

        var json = JsonSerializer.Serialize(headers);
        await core.ExecuteScriptAsync($"globalThis.__CHATGPT_CLIENT_HEADERS__ = {json};");
    }

    /// <summary>
    /// Waits until the WebView is on chatgpt.com and the API bridge can run in-page fetch.
    /// </summary>
    public async Task WaitForInjectablePageAsync(
        CoreWebView2 core,
        int timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ChatGptPageGate.IsInjectable(core.Source))
            {
                await InjectAsync(core);
                return;
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new ChatGptApiException(
            "ChatGPT has not finished loading. Open the Adventure tab, sign in, then click Refresh.",
            ChatGptApiEndpoints.ProjectsSidebar);
    }

    public async Task WaitForBridgeReadyAsync(
        CoreWebView2 core,
        int timeoutMs = 45000,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        Exception? lastError = null;
        var attempts = 0;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempts++;

            await WaitForInjectablePageAsync(core, Math.Min(5000, timeoutMs), cancellationToken);
            await InjectAsync(core);

            try
            {
                var pong = await InvokeViaScriptAsync(core, new { action = "ping" }, cancellationToken);

                if (BridgeScriptJson.IsBridgeSuccess(pong))
                {
                    ProjectLinkDiagnostics.LogBridgeEvent($"ping_ok attempts={attempts}");
                    return;
                }

                lastError = new InvalidOperationException(
                    pong.Error ?? pong.Message ?? $"unexpected_ping_response:{BridgeScriptJson.Truncate(pong.RawJson, 200)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }

            if (attempts <= 3 || attempts % 5 == 0)
                ProjectLinkDiagnostics.LogBridgeEvent($"ping_fail:{lastError?.Message}");

            await Task.Delay(400, cancellationToken);
        }

        throw new ChatGptApiException(
            lastError is TimeoutException
                ? "ChatGPT API bridge did not respond. Select the ChatGPT tab, wait for the page to finish loading, then click Test connection again."
                : "ChatGPT API bridge is not ready.",
            ChatGptApiEndpoints.Session);
    }

    public async Task<ApiBridgeMessage> SendAsync(
        CoreWebView2 core,
        object command,
        int timeoutMs = 60000,
        CancellationToken cancellationToken = default,
        bool skipReadyWait = false)
    {
        if (!skipReadyWait)
            await WaitForInjectablePageAsync(core, cancellationToken: cancellationToken);

        if (!skipReadyWait || !IsWarm(core))
            await InjectAsync(core);

        if (!IsWarm(core))
            MarkWarm(core);

        var syncMsg = await InvokeViaScriptAsync(core, command, cancellationToken);
        if (ShouldUseScriptResult(syncMsg))
            return syncMsg;

        return await InvokeViaScriptQueueAsync(core, command, timeoutMs, cancellationToken);
    }

    private static bool ShouldUseScriptResult(ApiBridgeMessage msg)
    {
        if (string.Equals(msg.Error, "async_use_postmessage", StringComparison.Ordinal))
            return false;

        if (string.Equals(msg.RawJson, "{}", StringComparison.Ordinal))
            return false;

        if (BridgeScriptJson.IsBridgeSuccess(msg))
            return true;

        return !string.IsNullOrEmpty(msg.Type)
               || !string.IsNullOrEmpty(msg.Error)
               || !string.IsNullOrEmpty(msg.Message);
    }

    private Task<ApiBridgeMessage> InvokeViaScriptAsync(
        CoreWebView2 core,
        object command,
        CancellationToken cancellationToken) =>
        InvokeViaScriptCoreAsync(core, command, syncOnly: true, scriptTimeoutMs: 60_000, cancellationToken);

    private Task<ApiBridgeMessage> InvokeViaScriptAsyncAwait(
        CoreWebView2 core,
        object command,
        int timeoutMs,
        CancellationToken cancellationToken) =>
        InvokeViaScriptCoreAsync(
            core,
            command,
            syncOnly: false,
            scriptTimeoutMs: Math.Clamp(timeoutMs, 5_000, MaxBridgeTimeoutMs),
            cancellationToken);

    private static async Task<ApiBridgeMessage> InvokeViaScriptCoreAsync(
        CoreWebView2 core,
        object command,
        bool syncOnly,
        int scriptTimeoutMs,
        CancellationToken cancellationToken)
    {
        var cmdJson = JsonSerializer.Serialize(command);
        string script;
        if (syncOnly)
        {
            script =
                "(function(){try{if(typeof globalThis.__cgwApiInvoke!=='function')" +
                "return JSON.stringify({type:'apiError',ok:false,error:'bridge_not_injected'});" +
                $"var r=globalThis.__cgwApiInvoke({cmdJson});" +
                "if(r&&typeof r.then==='function')" +
                "return JSON.stringify({type:'apiError',ok:false,error:'async_use_postmessage'});" +
                "if(r==null||r===undefined)return JSON.stringify({type:'apiError',ok:false,error:'null_result'});" +
                "return JSON.stringify(r);" +
                "}catch(e){return JSON.stringify({type:'apiError',ok:false,error:'script_exception'," +
                "message:e&&e.message?String(e.message):'unknown'});}})()";
        }
        else
        {
            // Return plain objects; WebView2 JSON-serializes the resolved promise value.
            script =
                "(async function(){try{if(typeof globalThis.__cgwApiInvoke!=='function')" +
                "return{type:'apiError',ok:false,error:'bridge_not_injected'};" +
                $"var r=await globalThis.__cgwApiInvoke({cmdJson});" +
                "if(r==null||r===undefined)return{type:'apiError',ok:false,error:'null_result'};" +
                "return r;" +
                "}catch(e){return{type:'apiError',ok:false,error:'script_exception'," +
                "message:e&&e.message?String(e.message):'unknown'};}})()";
        }

        var raw = await core.ExecuteScriptAsync(script)
            .WaitAsync(TimeSpan.FromMilliseconds(scriptTimeoutMs), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(raw) || raw == "null" || raw == "undefined")
        {
            throw new ChatGptApiException(
                "Bridge script returned no data. Refresh the ChatGPT tab and try again.",
                ChatGptApiEndpoints.Session);
        }

        var json = BridgeScriptJson.Normalize(raw);
        if (string.IsNullOrWhiteSpace(json))
        {
            ProjectLinkDiagnostics.LogBridgeEvent($"script_empty_raw:{BridgeScriptJson.Truncate(raw, 200)}");
            throw new ChatGptApiException(
                "Bridge returned an empty response. Refresh the ChatGPT tab and try again.",
                ChatGptApiEndpoints.Session);
        }

        if (json == "{}")
            ProjectLinkDiagnostics.LogBridgeEvent($"script_empty_object_raw:{BridgeScriptJson.Truncate(raw, 200)}");

        return new ApiBridgeMessage(json);
    }

    private async Task<ApiBridgeMessage> InvokeViaScriptQueueAsync(
        CoreWebView2 core,
        object command,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var cmdJson = JsonSerializer.Serialize(command);
        var waitMs = Math.Clamp(timeoutMs, 1000, MaxBridgeTimeoutMs);
        var cdpBudgetMs = waitMs + 15_000;

        var script =
            "(async function(){try{if(typeof globalThis.__cgwApiInvoke!=='function')" +
            "return JSON.stringify({type:'apiError',ok:false,error:'bridge_not_injected'});" +
            $"var r=await globalThis.__cgwApiInvoke({cmdJson});" +
            "if(r==null||r===undefined)return JSON.stringify({type:'apiError',ok:false,error:'null_result'});" +
            "return JSON.stringify(r);" +
            "}catch(e){return JSON.stringify({type:'apiError',ok:false,error:'script_exception'," +
            "message:e&&e.message?String(e.message):'unknown'});}})()";

        var raw = await EvaluateJavaScriptAwaitAsync(core, script, cdpBudgetMs, cancellationToken);

        if (string.IsNullOrWhiteSpace(raw) || raw is "null" or "undefined")
        {
            ProjectLinkDiagnostics.LogBridgeEvent($"script_queue_empty:{BridgeScriptJson.Truncate(cmdJson, 120)}");
            throw new ChatGptApiException(
                "ChatGPT API bridge did not respond. Refresh the ChatGPT tab and try again.",
                ChatGptApiEndpoints.Session);
        }

        var json = BridgeScriptJson.Normalize(raw);
        if (string.IsNullOrWhiteSpace(json) || json == "{}")
        {
            ProjectLinkDiagnostics.LogBridgeEvent(
                $"script_queue_empty_object:{BridgeScriptJson.Truncate(raw, 120)}");
            throw new ChatGptApiException(
                "ChatGPT API bridge did not respond. Refresh the ChatGPT tab and try again.",
                ChatGptApiEndpoints.Session);
        }

        return new ApiBridgeMessage(json);
    }

    private static async Task<string> EvaluateJavaScriptAwaitAsync(
        CoreWebView2 core,
        string expression,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var parameters = JsonSerializer.Serialize(new
        {
            expression,
            awaitPromise = true,
            returnByValue = true,
            userGesture = true,
        });

        var responseJson = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", parameters)
            .WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        using var doc = JsonDocument.Parse(responseJson);
        var root = doc.RootElement;
        if (!root.TryGetProperty("result", out var resultObj))
            return "{}";

        if (resultObj.TryGetProperty("exceptionDetails", out var ex)
            && ex.ValueKind == JsonValueKind.Object
            && ex.TryGetProperty("text", out var exText))
        {
            throw new ChatGptApiException(
                exText.GetString() ?? "Bridge script threw an exception.",
                ChatGptApiEndpoints.Session);
        }

        string? raw = null;
        if (TryReadCdpReturnValue(resultObj, out var direct))
            raw = direct;
        else if (resultObj.TryGetProperty("result", out var nested)
                 && TryReadCdpReturnValue(nested, out var nestedRaw))
            raw = nestedRaw;

        if (string.IsNullOrWhiteSpace(raw))
        {
            ProjectLinkDiagnostics.LogBridgeEvent(
                $"script_cdp_unparsed:{BridgeScriptJson.Truncate(responseJson, 300)}");
            return "{}";
        }

        return raw;
    }

    private static bool TryReadCdpReturnValue(JsonElement remoteObject, out string raw)
    {
        raw = "";
        if (remoteObject.TryGetProperty("value", out var val))
        {
            raw = val.ValueKind switch
            {
                JsonValueKind.String => val.GetString() ?? "",
                JsonValueKind.Null => "null",
                JsonValueKind.Undefined => "undefined",
                _ => val.GetRawText(),
            };
            return !string.IsNullOrWhiteSpace(raw);
        }

        if (remoteObject.TryGetProperty("description", out var desc)
            && desc.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(desc.GetString()))
        {
            raw = desc.GetString()!;
            return true;
        }

        return false;
    }

    internal async Task<ApiBridgeMessage> SendViaPostMessageAsync(
        CoreWebView2 core,
        object command,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<ApiBridgeMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        await using var reg = cancellationToken.Register(() =>
            tcs.TrySetCanceled(cancellationToken));

        var payload = new Dictionary<string, object?> { ["channel"] = BridgeChannel, ["id"] = id };
        foreach (var prop in JsonSerializer.SerializeToElement(command).EnumerateObject())
            payload[prop.Name] = prop.Value.Clone();

        core.PostWebMessageAsJson(JsonSerializer.Serialize(payload));
        await Task.Yield();

        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task EnsureDocumentCreatedScriptAsync(CoreWebView2 core)
    {
        await _docScriptGate.WaitAsync();
        try
        {
            const string version = "V4";
            if (string.Equals(_documentCreatedBootstrapVersion, version, StringComparison.Ordinal))
                return;

            // Listener lives in chatgpt-api-bridge.js after inject; avoid competing document-created handlers.
            var bootstrap =
                "(function(){if(globalThis.__cgwApiDocBootstrapV4)return;globalThis.__cgwApiDocBootstrapV4=true;})();";

            _documentCreatedScriptId = await core.AddScriptToExecuteOnDocumentCreatedAsync(bootstrap);
            _documentCreatedBootstrapVersion = version;
        }
        catch
        {
            /* document-created script may already exist for this profile */
        }
        finally
        {
            _docScriptGate.Release();
        }
    }

    private static bool IsInjectablePage(string? source) =>
        ChatGptPageGate.IsInjectable(source);

    private static string GetBridgeScript()
    {
        if (!string.IsNullOrWhiteSpace(TestBridgeScriptOverride))
        {
            return "(function(){" + TestBridgeScriptOverride + "})();";
        }

        var path = Path.Combine(AppContext.BaseDirectory, "wrapper-assets", "chatgpt-api-bridge.js");
        var stamp = WrapperAssetCache.ComputeStamp(path);
        if (_cachedScript is not null && stamp == _cachedStamp)
            return _cachedScript;

        if (!File.Exists(path))
            return "/* chatgpt-api-bridge.js missing */";

        var raw = File.ReadAllText(path);
        var streamPath = Path.Combine(AppContext.BaseDirectory, "wrapper-assets", "cgw-conversation-stream.js");
        var kernelPath = Path.Combine(AppContext.BaseDirectory, "wrapper-assets", "cgw-bridge-kernel.js");
        var streamRaw = File.Exists(streamPath) ? File.ReadAllText(streamPath) : "";
        var kernelRaw = File.Exists(kernelPath) ? File.ReadAllText(kernelPath) : "";
        var sb = new StringBuilder();
        sb.Append("(function(){");
        if (!string.IsNullOrWhiteSpace(kernelRaw))
            sb.Append(kernelRaw);
        if (!string.IsNullOrWhiteSpace(streamRaw))
            sb.Append(streamRaw);
        sb.Append(raw);
        sb.Append("})();");
        _cachedScript = sb.ToString();
        _cachedStamp = stamp;
        return _cachedScript;
    }
}

public sealed class ApiBridgeMessage
{
    private readonly JsonDocument _doc;

    public ApiBridgeMessage(string rawJson)
    {
        RawJson = rawJson;
        _doc = JsonDocument.Parse(rawJson);
        Root = _doc.RootElement;
    }

    public string RawJson { get; }

    public JsonElement Root { get; }

    public string? Type =>
        Root.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()
            : null;

    public bool Ok =>
        (Root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
        || string.Equals(Type, "apiResult", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Type, "pong", StringComparison.OrdinalIgnoreCase);

    public int? Status =>
        Root.TryGetProperty("status", out var s) && s.TryGetInt32(out var n) ? n : null;

    public string? Error =>
        Root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
            ? e.GetString()
            : null;

    public string? Message =>
        Root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString()
            : null;

    public JsonElement? Json =>
        Root.TryGetProperty("json", out var j) && j.ValueKind != JsonValueKind.Null && j.ValueKind != JsonValueKind.Undefined
            ? j
            : null;

    public string? BodyText =>
        Root.TryGetProperty("bodyText", out var b) && b.ValueKind == JsonValueKind.String
            ? b.GetString()
            : null;

    public bool Streaming =>
        Root.TryGetProperty("streaming", out var s) && s.ValueKind == JsonValueKind.True;

    public bool StreamComplete =>
        Root.TryGetProperty("streamComplete", out var sc) && sc.ValueKind == JsonValueKind.True;

    public string? AssistantText =>
        Root.TryGetProperty("assistantText", out var at) && at.ValueKind == JsonValueKind.String
            ? at.GetString()
            : null;

    public string? AssistantMessageId =>
        Root.TryGetProperty("assistantMessageId", out var am) && am.ValueKind == JsonValueKind.String
            ? am.GetString()
            : null;

    public string? ConversationId =>
        Root.TryGetProperty("conversationId", out var cid) && cid.ValueKind == JsonValueKind.String
            ? cid.GetString()
            : null;
}

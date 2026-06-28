using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ChatGPTWrapper;

public sealed class ChatGptAdventureBridgeInjection : IPageFeature
{
    private static string? _cachedScript;
    private static long _cachedStamp;

    private readonly WebView2 _webView;
    private ChatGptPageHost? _pageHost;
    private bool _standaloneRegistered;

    string IPageFeature.FeatureId => PageFeatureIds.AdventureBridge;

    public bool IsRegistered => _pageHost is not null || _standaloneRegistered;

    public event EventHandler<AdventureBridgeMessage>? MessageReceived;

    public ChatGptAdventureBridgeInjection(WebView2 webView)
    {
        _webView = webView ?? throw new ArgumentNullException(nameof(webView));
    }

    public void Register(ChatGptPageHost pageHost)
    {
        _pageHost = pageHost ?? throw new ArgumentNullException(nameof(pageHost));
        pageHost.RegisterFeature(this);
        if (_webView.CoreWebView2 is { } core)
            _ = InjectAsync(core);
    }

    void IPageFeature.RegisterMessageHandlers(PageMessageRouter router)
    {
        router.RegisterLegacy((type, root) =>
        {
            if (string.IsNullOrEmpty(type) || type.StartsWith("cgwCompose", StringComparison.Ordinal))
                return;

        if (string.Equals(type, "cgwPlaySendLog", StringComparison.Ordinal)
            || string.Equals(type, "cgwDiagnosticsLog", StringComparison.Ordinal))
            return;

            HandleMessage(type, root);
        });
    }

    Task IPageFeature.ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken) =>
        InjectAsync(core);

    private void HandleMessage(string type, JsonElement root)
    {
        MessageReceived?.Invoke(this, AdventureBridgeMessage.FromJson(type, root.GetRawText(), root));
    }

    public void Register()
    {
        if (_pageHost is not null)
            return;

        var core = _webView.CoreWebView2
                   ?? throw new InvalidOperationException("Call after CoreWebView2 is ready.");

        if (!_standaloneRegistered)
        {
            core.WebMessageReceived += OnStandaloneWebMessageReceived;
            core.NavigationCompleted += OnStandaloneNavigationCompleted;
            _standaloneRegistered = true;
        }

        _ = InjectAsync(core);
    }

    private void OnStandaloneWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
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
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            HandleMessage(type ?? "", root);
        }
        catch
        {
            /* ignore malformed messages */
        }
    }

    private async void OnStandaloneNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not CoreWebView2 core || !e.IsSuccess)
            return;

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        await InjectAsync(core);
    }

    public async Task InjectAsync(CoreWebView2? core = null)
    {
        core ??= _webView.CoreWebView2;
        if (core is null || !ChatGptPageGate.IsInjectable(core.Source))
            return;

        var script = GetBridgeScript();
        await core.ExecuteScriptAsync(script);
    }

    public async Task<bool> EnsureBridgeReadyAsync(
        CoreWebView2 core,
        CancellationToken cancellationToken = default)
    {
        await InjectAsync(core);
        const int maxAttempts = 8;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var raw = await core.ExecuteScriptAsync(
                "(function(){return typeof globalThis.__cgwAdventureSubmitPrompt==='function';})()");
            if (raw.Contains("true", StringComparison.OrdinalIgnoreCase))
                return true;

            await Task.Delay(250, cancellationToken);
            await InjectAsync(core);
        }

        return false;
    }

    public async Task<bool> InvokeSubmitPromptAsync(
        CoreWebView2 core,
        string text,
        bool requireProjectContext,
        string? displayPlayerLine = null,
        string? packetHash = null,
        bool attachmentsPreStaged = false,
        bool hostCdpStaged = false)
    {
        var textJson = JsonSerializer.Serialize(text);
        var req = requireProjectContext ? "true" : "false";
        var playerJson = JsonSerializer.Serialize(displayPlayerLine ?? "");
        var hashJson = JsonSerializer.Serialize(packetHash ?? "");
        var preStagedJson = attachmentsPreStaged ? "true" : "false";
        var cdpJson = hostCdpStaged ? "true" : "false";
        var script =
            "(function(){var fn=globalThis.__cgwAdventureSubmitPrompt;"
            + "if(typeof fn!=='function')return false;"
            + $"fn({textJson},{req},{playerJson},{hashJson},[],false,{cdpJson},{preStagedJson});return true;}})()";

        var raw = await core.ExecuteScriptAsync(script);
        return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    public void SendFillComposerCommand(CoreWebView2 core, string text) =>
        SendCommand(core, new { action = "fillComposer", text });

    public void SendClearStaleInjectionComposerCommand(CoreWebView2 core) =>
        SendCommand(core, new { action = "clearComposerIfInjection" });

    public async Task<bool> InvokeSendPromptAsync(
        CoreWebView2 core,
        string text,
        int timeoutMs,
        bool requireProjectContext)
    {
        var textJson = JsonSerializer.Serialize(text);
        var req = requireProjectContext ? "true" : "false";
        var script =
            "(function(){var fn=globalThis.__cgwAdventureSendPrompt;"
            + "if(typeof fn!=='function')return false;"
            + $"fn({textJson},{timeoutMs},{req});return true;}})()";

        var raw = await core.ExecuteScriptAsync(script);
        return raw.Contains("true", StringComparison.OrdinalIgnoreCase);
    }

    public static Task StampUserDisplayAsync(
        CoreWebView2 core,
        string? displayPlayerLine,
        string? packetHash)
    {
        if (string.IsNullOrWhiteSpace(displayPlayerLine))
            return Task.CompletedTask;

        var playerJson = JsonSerializer.Serialize(displayPlayerLine);
        var hashJson = JsonSerializer.Serialize(packetHash ?? "");
        return core.ExecuteScriptAsync(
            "if(typeof globalThis.__cgwStampUserTurnDisplay==='function')"
            + $"globalThis.__cgwStampUserTurnDisplay({playerJson},{hashJson});");
    }

    public static Task RegisterUtilityHideAsync(CoreWebView2 core, string jobId)
    {
        var jobJson = JsonSerializer.Serialize(jobId);
        return core.ExecuteScriptAsync(
            "if(typeof globalThis.__cgwRegisterUtilityHide==='function')"
            + $"globalThis.__cgwRegisterUtilityHide({jobJson});");
    }

    public static Task ApplyInlineUtilityPreferencesAsync(
        CoreWebView2 core,
        bool hideDuringPlay,
        bool showTraffic)
    {
        var hideJson = hideDuringPlay ? "true" : "false";
        var showJson = showTraffic ? "true" : "false";
        return core.ExecuteScriptAsync(
            "if(typeof globalThis.__cgwSetInlineUtilityPreferences==='function')"
            + $"globalThis.__cgwSetInlineUtilityPreferences({hideJson},{showJson});"
            + "else{"
            + $"globalThis.__cgwHideInlineUtilityDuringPlay={hideJson};"
            + $"globalThis.__cgwShowInlineUtilityTraffic={showJson};"
            + "}");
    }

    /// <summary>Utility worker tabs should always show job traffic regardless of play hide settings.</summary>
    public static Task ApplyUtilityWorkerTabVisibilityAsync(CoreWebView2 core) =>
        ApplyInlineUtilityPreferencesAsync(core, hideDuringPlay: false, showTraffic: true);

    public static Task ApplyPlaySurfaceActionsAsync(
        CoreWebView2 core,
        IReadOnlyDictionary<string, string> actions)
    {
        var json = JsonSerializer.Serialize(actions);
        return core.ExecuteScriptAsync(
            $"globalThis.__cgwPlaySurfaceActions={json};"
            + "if(typeof globalThis.__cgwApplyPlaySurfaceActions==='function')"
            + "globalThis.__cgwApplyPlaySurfaceActions();");
    }

    public static Task ApplyThreadOrdinalMapAsync(CoreWebView2 core, IReadOnlyDictionary<string, int> ordinalMap)
    {
        var json = JsonSerializer.Serialize(ordinalMap);
        return core.ExecuteScriptAsync($"globalThis.__cgwThreadOrdinalMap={json};");
    }

    public static Task ApplyLogTurnLinkMapAsync(CoreWebView2 core, IReadOnlyDictionary<int, LogTurnLink> linkMap)
    {
        var json = JsonSerializer.Serialize(linkMap);
        return core.ExecuteScriptAsync($"globalThis.__cgwLogTurnLinkMap={json};");
    }

    public static Task ApplyRevisionHideEntriesAsync(CoreWebView2 core, IReadOnlyList<RevisionHideEntry> entries)
    {
        var json = JsonSerializer.Serialize(entries);
        return core.ExecuteScriptAsync($"globalThis.__cgwRevisionHideEntries={json};");
    }

    public void SendCommand(CoreWebView2 core, object command)
    {
        var json = JsonSerializer.Serialize(command);
        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeCommand,
            PlaySendCategory.Bridge,
            PlaySendLevel.Debug,
            "Posting bridge command to page",
            data: new { source = core.Source });
        core.PostWebMessageAsJson(json);
    }

    public void SendSubmitPromptCommand(
        CoreWebView2 core,
        string text,
        bool requireProjectContext,
        string? displayPlayerLine,
        string? packetHash,
        bool useWrapperAttachmentStash = false,
        bool hostCdpStaged = false,
        bool attachmentsPreStaged = false)
    {
        SendCommand(core, new
        {
            action = "submitPrompt",
            text,
            requireProjectContext,
            displayUserLine = displayPlayerLine ?? "",
            packetHash = packetHash ?? "",
            useWrapperAttachmentStash = useWrapperAttachmentStash,
            hostCdpStaged = hostCdpStaged,
            attachmentsPreStaged = attachmentsPreStaged,
        });
    }

    /// <summary>
    /// Pushes attachment bytes into the page stash used by DOM fallback staging.
    /// Survives compose script re-injection and avoids large PostWebMessage payloads.
    /// </summary>
    public async Task<bool> StageDomFallbackAttachmentsAsync(
        CoreWebView2 core,
        IReadOnlyList<DomAttachmentPayload> attachments)
    {
        if (attachments is not { Count: > 0 })
            return true;

        var items = attachments.Select(a => new
        {
            name = a.Name,
            mimeType = a.MimeType,
            base64 = Convert.ToBase64String(a.Content),
            sizeBytes = a.Content.Length,
        }).ToList();

        var json = JsonSerializer.Serialize(items);
        var script =
            "(function(){try{globalThis.__cgwDomFallbackAttachmentStash="
            + json
            + ";return true;}catch(e){return false;}})()";

        var raw = await core.ExecuteScriptAsync(script);
        var ok = raw.Contains("true", StringComparison.OrdinalIgnoreCase);
        PlaySendTrace.Event(
            PlaySendTraceEvents.BridgeSubmitInvoke,
            PlaySendCategory.Bridge,
            ok ? PlaySendLevel.Info : PlaySendLevel.Warn,
            ok
                ? "Host staged DOM fallback attachments on page"
                : "Host could not stage DOM fallback attachments on page",
            outcome: ok ? "stash_ok" : "stash_failed",
            data: new
            {
                attachmentCount = attachments.Count,
                totalBytes = attachments.Sum(a => a.Content.Length),
            });
        return ok;
    }

    private static string GetBridgeScript()
    {
        var path = WrapperAssetBundle.AssetPath("adventure-bridge.js");
        var stamp = WrapperAssetCache.ComputeStamp(path);
        if (_cachedScript is not null && stamp == _cachedStamp)
            return _cachedScript;

        if (!File.Exists(path))
            return "/* adventure-bridge.js missing */";

        var kernel = WrapperAssetBundle.GetKernelPayload();
        var raw = File.ReadAllText(path);
        var sb = new StringBuilder();
        sb.Append(kernel);
        sb.Append("(function(){");
        sb.Append(raw);
        sb.Append("})();");
        _cachedScript = sb.ToString();
        _cachedStamp = stamp;
        return _cachedScript;
    }
}

public sealed class AdventureBridgeMessage
{
    private AdventureBridgeMessage(
        string? type,
        string rawJson,
        bool ok,
        string? text,
        string? error,
        string? conversationId,
        bool fromRegenerate,
        bool composerFound,
        bool submitFound,
        int? assistantTurnCount,
        string? domTurnId,
        string? reason,
        int? logTurnIndex,
        string? editRole,
        bool usedFallback,
        string? revisionGroupId,
        string? revisionPrompt,
        string? assistantDomTurnId)
    {
        Type = type;
        RawJson = rawJson;
        Ok = ok;
        Text = text;
        Error = error;
        ConversationId = conversationId;
        FromRegenerate = fromRegenerate;
        ComposerFound = composerFound;
        SubmitFound = submitFound;
        AssistantTurnCount = assistantTurnCount;
        DomTurnId = domTurnId;
        Reason = reason;
        LogTurnIndex = logTurnIndex;
        EditRole = editRole;
        UsedFallback = usedFallback;
        RevisionGroupId = revisionGroupId;
        RevisionPrompt = revisionPrompt;
        AssistantDomTurnId = assistantDomTurnId;
    }

    public string? Type { get; }

    public string RawJson { get; }

    public bool Ok { get; }

    public string? Text { get; }

    public string? Error { get; }

    public string? ConversationId { get; }

    public bool FromRegenerate { get; }

    public bool ComposerFound { get; }

    public bool SubmitFound { get; }

    public int? AssistantTurnCount { get; }

    public string? DomTurnId { get; }

    public string? Reason { get; }

    public int? LogTurnIndex { get; }

    public string? EditRole { get; }

    public bool UsedFallback { get; }

    public string? RevisionGroupId { get; }

    public string? RevisionPrompt { get; }

    public string? AssistantDomTurnId { get; }

    public static AdventureBridgeMessage FromJson(string? type, string rawJson, JsonElement root)
    {
        var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
        var text = root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
            ? textEl.GetString()
            : null;
        var error = root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String
            ? errEl.GetString()
            : null;
        var conversationId =
            root.TryGetProperty("conversationId", out var convEl) && convEl.ValueKind == JsonValueKind.String
                ? convEl.GetString()
                : null;
        var fromRegenerate =
            root.TryGetProperty("fromRegenerate", out var regenEl) && regenEl.ValueKind == JsonValueKind.True;

        var composerFound = false;
        var submitFound = false;
        if (root.TryGetProperty("probe", out var probe) && probe.ValueKind == JsonValueKind.Object)
        {
            composerFound = probe.TryGetProperty("composerFound", out var c)
                            && c.ValueKind == JsonValueKind.True;
            submitFound = probe.TryGetProperty("submitFound", out var s)
                          && s.ValueKind == JsonValueKind.True;
        }

        int? assistantTurnCount = null;
        if (root.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number
            && countEl.TryGetInt32(out var countValue))
        {
            assistantTurnCount = countValue;
        }

        var domTurnId = root.TryGetProperty("turnId", out var turnEl) && turnEl.ValueKind == JsonValueKind.String
            ? turnEl.GetString()
            : root.TryGetProperty("turnId", out var turnNum) && turnNum.ValueKind == JsonValueKind.Number
                ? turnNum.GetInt32().ToString()
                : null;
        var reason = root.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
            ? reasonEl.GetString()
            : null;

        int? logTurnIndex = null;
        if (root.TryGetProperty("logTurnIndex", out var logIdxEl) && logIdxEl.ValueKind == JsonValueKind.Number
            && logIdxEl.TryGetInt32(out var logIdxValue))
        {
            logTurnIndex = logIdxValue;
        }

        var editRole = root.TryGetProperty("editRole", out var roleEl) && roleEl.ValueKind == JsonValueKind.String
            ? roleEl.GetString()
            : null;

        var usedFallback =
            root.TryGetProperty("usedFallback", out var fbEl) && fbEl.ValueKind == JsonValueKind.True;

        var revisionGroupId =
            root.TryGetProperty("revisionGroupId", out var rgEl) && rgEl.ValueKind == JsonValueKind.String
                ? rgEl.GetString()
                : null;
        var revisionPrompt =
            root.TryGetProperty("revisionPrompt", out var rpEl) && rpEl.ValueKind == JsonValueKind.String
                ? rpEl.GetString()
                : null;
        var assistantDomTurnId =
            root.TryGetProperty("assistantDomTurnId", out var adEl) && adEl.ValueKind == JsonValueKind.String
                ? adEl.GetString()
                : null;

        return new AdventureBridgeMessage(
            type,
            rawJson,
            ok,
            text,
            error,
            conversationId,
            fromRegenerate,
            composerFound,
            submitFound,
            assistantTurnCount,
            domTurnId,
            reason,
            logTurnIndex,
            editRole,
            usedFallback,
            revisionGroupId,
            revisionPrompt,
            assistantDomTurnId);
    }
}

public sealed class BridgeHealthStatus
{
    public bool BridgeReachable { get; init; }

    public bool ComposerFound { get; init; }

    public bool SubmitFound { get; init; }

    public string? ConversationId { get; init; }

    public string? Error { get; init; }
}

using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WebViewCoreRuntime = ChatGPTWrapper.WebView.WebView2ManagedCoreRuntime;

namespace ChatGPTWrapper;

/// <summary>Host-specific wiring for standalone play compose (WinUI WinRT core events).</summary>
public delegate void PlayComposeStandaloneWireHandler(object core, ChatGptPlayComposeInjection injection);

/// <summary>
/// Pending file attachment staged in the wrapper composer before upload.
/// </summary>
public sealed class PlayComposePendingAttachment
{
    public required string Name { get; init; }

    public required string MimeType { get; init; }

    public required byte[] Content { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }
}

public sealed class ComposerAttachmentMetaDto
{
    public string Name { get; init; } = "";

    public string? MimeType { get; init; }

    public long? SizeBytes { get; init; }
}

public sealed class PlayComposeSendEventArgs : EventArgs
{
    public string? Text { get; init; }

    public IReadOnlyList<PlayComposePendingAttachment> Attachments { get; init; } = [];

    public IReadOnlyList<ComposerAttachmentMetaDto> AttachmentMeta { get; init; } = [];

    public bool AttachmentsPreStaged { get; init; }
}

public sealed class PlayComposeUploadEventArgs : EventArgs
{
    public required string JobId { get; init; }

    public IReadOnlyList<string> AttachmentIds { get; init; } = [];

    public IReadOnlyList<PlayComposePendingAttachment> Attachments { get; init; } = [];
}

/// <summary>
/// Play-mode composer integration: native send intercept (default) or legacy wrapper UI.
/// </summary>
public sealed class ChatGptPlayComposeInjection : IPageFeature
{
    private static string? _cachedScriptPayload;
    private static long _cachedScriptStamp;

    private readonly object _tabHost;
    private readonly Func<object?> _getCore;
    private readonly Func<bool> _getWrapperComposerEnabled;
    private readonly PlayComposeStandaloneWireHandler? _wireStandaloneEvents;
    private ChatGptPageHost? _pageHost;
    private bool _standaloneRegistered;
    private bool _nativePassthrough;
    private string _cachedText = "";
    private IReadOnlyList<ComposerAttachmentMetaDto> _lastAttachmentMeta = [];

    string IPageFeature.FeatureId => PageFeatureIds.PlayCompose;

    public ChatGptPlayComposeInjection(WebView2 webView, Func<bool> getWrapperComposerEnabled)
        : this(
            webView,
            () => webView.CoreWebView2,
            getWrapperComposerEnabled)
    {
    }

    public ChatGptPlayComposeInjection(
        object tabHost,
        Func<object?> getCore,
        Func<bool> getWrapperComposerEnabled,
        PlayComposeStandaloneWireHandler? wireStandaloneEvents = null)
    {
        _tabHost = tabHost ?? throw new ArgumentNullException(nameof(tabHost));
        _getCore = getCore ?? throw new ArgumentNullException(nameof(getCore));
        _getWrapperComposerEnabled = getWrapperComposerEnabled
            ?? throw new ArgumentNullException(nameof(getWrapperComposerEnabled));
        _wireStandaloneEvents = wireStandaloneEvents;
    }

    static ChatGptPlayComposeInjection() => WebViewCoreRuntime.Register();

    private object? TryGetCoreObject()
    {
        var core = _getCore();
        if (core is null)
            return null;

        WebViewCoreRuntime.TryAsCore(core, out _);
        return core;
    }

    private CoreWebView2? TryGetCore()
    {
        var core = TryGetCoreObject();
        if (core is CoreWebView2 typed)
            return typed;

        if (core is null)
            return null;

        try
        {
            return WebViewCoreRuntime.RequireTypedCore(core);
        }
        catch
        {
            return null;
        }
    }

    public bool IsRegistered => _pageHost is not null || _standaloneRegistered;

    public object TabHost => _tabHost;

    public object? CoreWebView => _getCore();

    public CoreWebView2? CoreWebView2 => TryGetCore();

    public void Register(ChatGptPageHost pageHost)
    {
        _pageHost = pageHost ?? throw new ArgumentNullException(nameof(pageHost));
        pageHost.RegisterFeature(this);
        if (TryGetCore() is { } core)
            _ = ApplyAsync(core);
    }

    void IPageFeature.RegisterMessageHandlers(PageMessageRouter router) =>
        router.Register(PageFeatureIds.PlayCompose, HandleMessage);

    Task IPageFeature.ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken) =>
        ApplyAsync(core);

    public event EventHandler<PlayComposeSendEventArgs>? SendRequested;

    public event EventHandler<PlayComposeUploadEventArgs>? UploadRequested;

    public event EventHandler? TextChanged;

    public string GetText() => _cachedText.Trim();

    public void ClearCachedText() => _cachedText = "";

    public AttachmentContext? GetLastAttachmentContext()
    {
        if (_lastAttachmentMeta.Count == 0)
            return null;

        return AttachmentContext.FromMeta(_lastAttachmentMeta.Select(m => new ComposerAttachmentMeta
        {
            Name = m.Name,
            MimeType = m.MimeType,
            SizeBytes = m.SizeBytes,
        }));
    }

    [Obsolete("Use TabHost for host-neutral orchestration.")]
    internal WebView2? WebView => _tabHost as WebView2;

    public void Register()
    {
        if (_pageHost is not null)
            return;

        var coreObj = TryGetCoreObject()
                      ?? throw new InvalidOperationException("Call after CoreWebView2 is ready.");

        if (TryGetCore() is { } managedCore)
        {
            managedCore.Settings.IsWebMessageEnabled = true;

            if (!_standaloneRegistered)
            {
                managedCore.WebMessageReceived += OnStandaloneWebMessageReceived;
                managedCore.NavigationCompleted += OnStandaloneNavigationCompleted;
                _standaloneRegistered = true;
                _ = ApplyAsync(managedCore);
                return;
            }

            _ = ApplyPreferenceAsync(managedCore, _getWrapperComposerEnabled());
            return;
        }

        EnableWebMessages(coreObj);

        if (!_standaloneRegistered)
        {
            if (_wireStandaloneEvents is null)
            {
                throw new InvalidOperationException(
                    "WinUI play compose requires PlayComposeStandaloneWireHandler.");
            }

            _wireStandaloneEvents(coreObj, this);
            _standaloneRegistered = true;
            _ = ApplyAsyncObject(coreObj);
            return;
        }

        _ = ApplyPreferenceObjectAsync(coreObj, _getWrapperComposerEnabled());
    }

    public void HandleStandaloneWebMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return;

            HandleMessage(typeEl.GetString() ?? "", root);
        }
        catch
        {
            /* ignore malformed messages */
        }
    }

    public async Task OnStandaloneNavigationCompletedObject(object sender, object e)
    {
        try
        {
            dynamic args = e;
            if (args.IsSuccess != true)
                return;

            var source = WebViewCoreRuntime.GetSource(sender);
            if (!ChatGptPageGate.IsInjectable(source))
                return;

            await ApplyAsyncObject(sender);
        }
        catch
        {
            /* page may still be loading */
        }
    }

    private static void EnableWebMessages(object core)
    {
        dynamic dynamicCore = core;
        dynamicCore.Settings.IsWebMessageEnabled = true;
    }

    private void HandleMessage(string type, JsonElement root)
    {
        switch (type)
        {
            case "cgwPlaySendLog":
                DiagnosticsLog.LogFromPage(root, mirrorPlaySendLegacy: true);
                break;
            case "cgwDiagnosticsLog":
                DiagnosticsLog.LogFromPage(root);
                break;
            case "cgwComposeInput":
                if (root.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                {
                    _cachedText = textEl.GetString() ?? "";
                    PlaySendTrace.Event(
                        PlaySendTraceEvents.ComposeInput,
                        PlaySendCategory.Compose,
                        PlaySendLevel.Debug,
                        "Compose input updated",
                        data: new { length = _cachedText.Length });
                }

                TextChanged?.Invoke(this, EventArgs.Empty);
                break;
            case "cgwComposeSend":
                if (root.TryGetProperty("text", out var sendText)
                    && sendText.ValueKind == JsonValueKind.String)
                {
                    _cachedText = sendText.GetString() ?? _cachedText;
                }

                var attachments = ParseComposeAttachments(root);
                var attachmentsPreStaged = root.TryGetProperty("attachmentsPreStaged", out var preStagedEl)
                    && preStagedEl.ValueKind == JsonValueKind.True;
                var attachmentMeta = ParseAttachmentMeta(root);

                var traceContext = attachmentMeta is { Count: > 0 }
                    ? AttachmentContext.FromMeta(attachmentMeta.Select(m => new ComposerAttachmentMeta
                    {
                        Name = m.Name,
                        MimeType = m.MimeType,
                        SizeBytes = m.SizeBytes,
                    }))
                    : null;

                PlaySendTrace.Event(
                    PlaySendTraceEvents.ComposeSend,
                    PlaySendCategory.Compose,
                    PlaySendLevel.Info,
                    "Compose send message received",
                    data: new
                    {
                        textLength = _cachedText.Length,
                        attachmentCount = attachments.Count,
                        attachmentsPreStaged,
                        attachmentKinds = AttachmentSendPolicy.AttachmentKinds(traceContext),
                        attachmentOnly = traceContext?.IsAttachmentOnly(_cachedText) == true,
                        preview = TruncateForLog(_cachedText, 120),
                        webViewSource = PlayWebViewCoreBridge.GetSource(_getCore()),
                    });

                _ = HandleComposeSendAsync(_cachedText, attachments, attachmentsPreStaged, attachmentMeta);
                break;
            case "cgwComposeUploadRequest":
                HandleComposeUploadRequest(root);
                break;
        }
    }

    private void HandleComposeUploadRequest(JsonElement root)
    {
        if (!_getWrapperComposerEnabled())
            return;

        var jobId = root.TryGetProperty("jobId", out var jobEl) && jobEl.ValueKind == JsonValueKind.String
            ? jobEl.GetString()
            : null;
        if (string.IsNullOrWhiteSpace(jobId))
            return;

        var attachmentIds = new List<string>();
        if (root.TryGetProperty("attachmentIds", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var idEl in idsEl.EnumerateArray())
            {
                if (idEl.ValueKind == JsonValueKind.String)
                {
                    var id = idEl.GetString();
                    if (!string.IsNullOrWhiteSpace(id))
                        attachmentIds.Add(id);
                }
            }
        }

        var attachments = ParseComposeAttachments(root);
        if (attachments.Count == 0)
            return;

        PlaySendTrace.Event(
            PlaySendTraceEvents.ComposeState,
            PlaySendCategory.Compose,
            PlaySendLevel.Info,
            "Compose upload requested",
            data: new
            {
                jobId,
                attachmentCount = attachments.Count,
                webViewSource = PlayWebViewCoreBridge.GetSource(_getCore()),
            });

        UploadRequested?.Invoke(this, new PlayComposeUploadEventArgs
        {
            JobId = jobId,
            AttachmentIds = attachmentIds,
            Attachments = attachments,
        });
    }

    private static string? TruncateForLog(string? text, int maxChars)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxChars)
            return text;

        return text[..maxChars] + "…";
    }

    private const int MaxComposeAttachmentBytes = 20 * 1024 * 1024;

    private static IReadOnlyList<PlayComposePendingAttachment> ParseComposeAttachments(JsonElement root)
    {
        if (!root.TryGetProperty("attachments", out var attachmentsEl)
            || attachmentsEl.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var list = new List<PlayComposePendingAttachment>();
        foreach (var item in attachmentsEl.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString()
                : null;
            var mimeType = item.TryGetProperty("mimeType", out var mimeEl) && mimeEl.ValueKind == JsonValueKind.String
                ? mimeEl.GetString()
                : null;
            if (!item.TryGetProperty("base64", out var b64El) || b64El.ValueKind != JsonValueKind.String)
                continue;

            var b64 = b64El.GetString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(b64))
                continue;

            byte[] content;
            try
            {
                content = Convert.FromBase64String(b64);
            }
            catch
            {
                continue;
            }

            if (content.Length == 0 || content.Length > MaxComposeAttachmentBytes)
                continue;

            list.Add(new PlayComposePendingAttachment
            {
                Name = name,
                MimeType = string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
                Content = content,
                Width = item.TryGetProperty("width", out var widthEl) && widthEl.TryGetInt32(out var width) && width > 0
                    ? width
                    : null,
                Height = item.TryGetProperty("height", out var heightEl) && heightEl.TryGetInt32(out var height) && height > 0
                    ? height
                    : null,
            });
        }

        return list;
    }

    public async Task ApplyUploadStatusAsync(
        CoreWebView2 core,
        string jobId,
        IReadOnlyList<string> attachmentIds,
        string status,
        string? error = null)
    {
        var jobJson = JsonSerializer.Serialize(jobId);
        var idsJson = JsonSerializer.Serialize(attachmentIds);
        var statusJson = JsonSerializer.Serialize(status);
        var errorJson = JsonSerializer.Serialize(error ?? "");
        await core.ExecuteScriptAsync(
            $"(function(){{var fn=globalThis.__cgwPlayComposeSetUploadStatus;if(typeof fn==='function')fn({jobJson},{idsJson},{statusJson},{errorJson});}})()");
    }

    private static List<ComposerAttachmentMetaDto> ParseAttachmentMeta(JsonElement root)
    {
        var list = new List<ComposerAttachmentMetaDto>();
        if (!root.TryGetProperty("attachmentMeta", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            var name = item.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                ? nameEl.GetString() ?? ""
                : "";
            if (string.IsNullOrWhiteSpace(name))
                continue;

            list.Add(new ComposerAttachmentMetaDto
            {
                Name = name,
                MimeType = item.TryGetProperty("mimeType", out var mimeEl) && mimeEl.ValueKind == JsonValueKind.String
                    ? mimeEl.GetString()
                    : null,
                SizeBytes = item.TryGetProperty("sizeBytes", out var sizeEl) && sizeEl.TryGetInt64(out var size)
                    ? size
                    : null,
            });
        }

        return list;
    }

    private async Task HandleComposeSendAsync(
        string? text,
        IReadOnlyList<PlayComposePendingAttachment> attachments,
        bool attachmentsPreStaged = false,
        IReadOnlyList<ComposerAttachmentMetaDto>? attachmentMeta = null)
    {
        _lastAttachmentMeta = attachmentMeta ?? [];

        if (TryGetCore() is { } core)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ComposeState,
                PlaySendCategory.Compose,
                PlaySendLevel.Debug,
                "Applying compose busy state before host send",
                data: new { busy = true, status = "Preparing…" });

            await ApplyStateAsync(core, new PlayComposeUiState
            {
                Busy = true,
                Status = "Preparing…",
            });
        }
        else if (TryGetCoreObject() is { } coreObj)
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ComposeState,
                PlaySendCategory.Compose,
                PlaySendLevel.Debug,
                "Applying compose busy state before host send",
                data: new { busy = true, status = "Preparing…" });

            await ApplyStateObjectAsyncHost(coreObj, new PlayComposeUiState
            {
                Busy = true,
                Status = "Preparing…",
            });
        }
        else
        {
            PlaySendTrace.Event(
                PlaySendTraceEvents.ComposeState,
                PlaySendCategory.Compose,
                PlaySendLevel.Warn,
                "Compose send received but CoreWebView2 is not ready");
        }

        PlaySendTrace.Event(
            PlaySendTraceEvents.SendRequested,
            PlaySendCategory.Host,
            PlaySendLevel.Info,
            "Raising SendRequested",
            data: new { textLength = text?.Length ?? 0, attachmentCount = attachments.Count });

        SendRequested?.Invoke(this, new PlayComposeSendEventArgs
        {
            Text = text,
            Attachments = attachments,
            AttachmentMeta = attachmentMeta ?? [],
            AttachmentsPreStaged = attachmentsPreStaged,
        });
    }

    private void OnStandaloneWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var json = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(json))
                json = e.WebMessageAsJson;

            HandleStandaloneWebMessage(json);
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

        await ApplyAsync(core);
    }

    public static Task ReapplyAsync(CoreWebView2 core, bool enabled) =>
        ApplyPreferenceAsync(core, enabled);

    public static Task ApplyNativePassthroughAsync(CoreWebView2 core, bool passthrough) =>
        core.ExecuteScriptAsync(BuildPassthroughScript(passthrough));

    public async Task SetNativePassthroughAsync(bool passthrough)
    {
        _nativePassthrough = passthrough;
        if (TryGetCore() is { } core)
            await ApplyNativePassthroughAsync(core, passthrough);
        else if (TryGetCoreObject() is { } coreObj)
            await ApplyNativePassthroughObjectAsync(coreObj, passthrough);
    }

    public bool NativePassthrough => _nativePassthrough;

    private readonly SemaphoreSlim _applyGate = new(1, 1);

    public Task ApplyComposeStateFromHost(object? coreWebView, PlayComposeUiState state)
    {
        if (coreWebView is null)
            return Task.CompletedTask;

        if (coreWebView is CoreWebView2 typed)
            return ApplyStateAsync(typed, state);

        return ApplyStateObjectAsyncHost(coreWebView, state);
    }

    public Task ApplyStateAsync(object coreWebView, PlayComposeUiState state)
    {
        if (coreWebView is CoreWebView2 typed)
            return ApplyStateAsync(typed, state);

        return ApplyStateObjectAsyncHost(coreWebView, state);
    }

    public async Task ApplyStateAsync(CoreWebView2 core, PlayComposeUiState state)
    {
        if (state.Clear == true)
            _cachedText = "";
        else if (state.Text is not null)
            _cachedText = state.Text;

        PlaySendTrace.Event(
            PlaySendTraceEvents.ComposeState,
            PlaySendCategory.Host,
            PlaySendLevel.Debug,
            "Applying compose UI state",
            data: new
            {
                busy = state.Busy,
                clear = state.Clear,
                focus = state.Focus,
                status = state.Status,
                textLength = state.Text?.Length,
                webViewSource = core.Source,
            });

        await _applyGate.WaitAsync();
        try
        {
            await ApplyStateScriptAsync(core, state);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task ApplyStateObjectAsyncHost(object core, PlayComposeUiState state)
    {
        if (state.Clear == true)
            _cachedText = "";
        else if (state.Text is not null)
            _cachedText = state.Text;

        PlaySendTrace.Event(
            PlaySendTraceEvents.ComposeState,
            PlaySendCategory.Host,
            PlaySendLevel.Debug,
            "Applying compose UI state",
            data: new
            {
                busy = state.Busy,
                clear = state.Clear,
                focus = state.Focus,
                status = state.Status,
                textLength = state.Text?.Length,
                webViewSource = WebViewCoreRuntime.GetSource(core),
            });

        await _applyGate.WaitAsync();
        try
        {
            await ApplyStateObjectAsync(core, state);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    public async Task SyncTextFromPageAsync(CoreWebView2 core)
    {
        try
        {
            var raw = await core.ExecuteScriptAsync(
                "(function(){return typeof globalThis.__cgwPlayComposeGetText==='function'?globalThis.__cgwPlayComposeGetText():'';})()");
            if (raw.Length >= 2 && raw[0] == '"')
                _cachedText = JsonSerializer.Deserialize<string>(raw) ?? _cachedText;
        }
        catch
        {
            /* page may still be loading */
        }
    }

    private async Task ApplyAsync(CoreWebView2? core)
    {
        core ??= TryGetCore();
        if (core is null)
        {
            if (TryGetCoreObject() is { } coreObj)
                await ApplyAsyncObject(coreObj);

            return;
        }

        if (!ChatGptPageGate.IsInjectable(core.Source))
            return;

        var script = GetScriptPayload();
        if (string.IsNullOrWhiteSpace(script))
            return;

        await core.ExecuteScriptAsync(script);
        await ApplyPreferenceAsync(core, _getWrapperComposerEnabled());
        if (_nativePassthrough)
            await ApplyNativePassthroughAsync(core, true);
    }

    private async Task ApplyAsyncObject(object core)
    {
        var source = WebViewCoreRuntime.GetSource(core);
        if (!ChatGptPageGate.IsInjectable(source))
            return;

        var script = GetScriptPayload();
        if (string.IsNullOrWhiteSpace(script))
            return;

        await WebViewCoreRuntime.ExecuteScriptAsync(core, script);
        await ApplyPreferenceObjectAsync(core, _getWrapperComposerEnabled());
        if (_nativePassthrough)
            await ApplyNativePassthroughObjectAsync(core, true);
    }

    private static Task ApplyPreferenceAsync(CoreWebView2 core, bool enabled) =>
        core.ExecuteScriptAsync(BuildPreferenceScript(enabled));

    private static Task ApplyPreferenceObjectAsync(object core, bool enabled) =>
        WebViewCoreRuntime.ExecuteScriptAsync(core, BuildPreferenceScript(enabled));

    private static Task ApplyStateScriptAsync(CoreWebView2 core, PlayComposeUiState state)
    {
        var json = JsonSerializer.Serialize(state, ComposeJsonOptions);
        return core.ExecuteScriptAsync(
            $"(function(){{var fn=globalThis.__cgwPlayComposeApplyState;if(typeof fn==='function')fn({json});}})()");
    }

    private static Task ApplyStateObjectAsync(object core, PlayComposeUiState state)
    {
        var json = JsonSerializer.Serialize(state, ComposeJsonOptions);
        return WebViewCoreRuntime.ExecuteScriptAsync(
            core,
            $"(function(){{var fn=globalThis.__cgwPlayComposeApplyState;if(typeof fn==='function')fn({json});}})()");
    }

    private static Task ApplyNativePassthroughObjectAsync(object core, bool passthrough) =>
        WebViewCoreRuntime.ExecuteScriptAsync(core, BuildPassthroughScript(passthrough));

    private static readonly JsonSerializerOptions ComposeJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static string BuildPreferenceScript(bool enabled) =>
        "(function(){"
        + $"if(typeof globalThis.__cgwSetWrapperComposer==='function')globalThis.__cgwSetWrapperComposer({JsonSerializer.Serialize(enabled)});"
        + "if(typeof globalThis.__cgwPlayComposeEnsureHooks==='function')globalThis.__cgwPlayComposeEnsureHooks();"
        + "})();";

    private static string BuildPassthroughScript(bool passthrough) =>
        "(function(){"
        + $"if(typeof globalThis.__cgwSetNativeComposePassthrough==='function')globalThis.__cgwSetNativeComposePassthrough({JsonSerializer.Serialize(passthrough)});"
        + "})();";

    private static string GetScriptPayload()
    {
        var jsPath = WrapperAssetBundle.AssetPath("cgw-play-compose.js");
        var cssPath = WrapperAssetBundle.AssetPath("cgw-play-compose.css");
        if (!File.Exists(jsPath))
            return "";

        var newStamp = WrapperAssetCache.ComputeStamp(jsPath, cssPath);
        if (_cachedScriptPayload != null && _cachedScriptStamp == newStamp)
            return _cachedScriptPayload;

        _cachedScriptPayload = WrapperAssetBundle.BuildCssJsBundle(
            "cgw-play-compose.css",
            "__cgwPlayComposeCss",
            "cgw-play-compose-css",
            "cgw-play-compose.js");
        _cachedScriptStamp = newStamp;
        return _cachedScriptPayload;
    }
}

public sealed class PlayComposeUiState
{
    public string? Text { get; init; }

    public bool? Clear { get; init; }

    public bool? Busy { get; init; }

    public string? Status { get; init; }

    public string? Placeholder { get; init; }

    public bool? Focus { get; init; }

    public bool? ClearAttachments { get; init; }

    public bool? SendEnabled { get; init; }

    public bool? InjectionArmed { get; init; }

    public string? InjectionArmReason { get; init; }
}

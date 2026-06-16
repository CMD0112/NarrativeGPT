using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;
using ChatGPTWrapper;
using ChatGPTWrapper.PageIntegration;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// Loads a ChatGPT-like composer fixture and injects wrapper assets for behavioral tests.
/// </summary>
public sealed class PlayComposeTestHost : IAsyncLifetime
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private WebView2? _webView;
    private Form? _form;
    private string? _userDataFolder;
    private bool _initialized;

    public CoreWebView2? Core => _webView?.CoreWebView2;

    public async Task InitializeAsync()
    {
        if (_initialized)
            return;

        (_form, _webView) = await PlayComposeUiEnvironment.GetShellAsync();

        await RunOnUiAsync(async () =>
        {
            _userDataFolder = Path.Combine(
                Path.GetTempPath(),
                "cgw-compose-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _userDataFolder);
            await _webView!.EnsureCoreWebView2Async(env);

            var fixturePath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "composer-fixture.html");
            if (!File.Exists(fixturePath))
                throw new FileNotFoundException("Composer fixture missing.", fixturePath);

            Core!.Settings.IsWebMessageEnabled = true;

            var navigateTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnNavCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
            {
                Core!.NavigationCompleted -= OnNavCompleted;
                navigateTcs.TrySetResult();
            }

            Core.NavigationCompleted += OnNavCompleted;
            Core.Navigate(new Uri(fixturePath).AbsoluteUri);
            await navigateTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await InjectComposeAssetsAsync();
            _initialized = true;
        });
    }

    public Task ResetPageAsync(bool enableWrapper = true) =>
        RunOnUiAsync(async () =>
        {
            _form?.Activate();
            _webView?.Focus();
            await EvalVoidAsyncInternal(
                "window.__cgwTestMessages = []; globalThis.__cgwPlayComposeApplyState({ clear: true, busy: false });");
            await EvalVoidAsyncInternal(
                enableWrapper
                    ? "globalThis.__cgwSetWrapperComposer(true);"
                    : "globalThis.__cgwSetWrapperComposer(false);");
            if (enableWrapper)
                await WaitUntilAsync("!!document.getElementById('cgw-play-composer-root')", TimeSpan.FromSeconds(3));
            await PumpUiAsync(TimeSpan.FromMilliseconds(250));
        });

    public async Task<IReadOnlyList<JsonDocument>> DrainMessagesAsync()
    {
        var raw = await EvalRawAsync("(function(){return JSON.stringify(window.__cgwTestMessages||[]);})()");
        var arrayJson = JsonSerializer.Deserialize<string>(raw) ?? "[]";
        using var doc = JsonDocument.Parse(arrayJson);
        var list = new List<JsonDocument>();
        foreach (var el in doc.RootElement.EnumerateArray())
            list.Add(JsonDocument.Parse(el.GetString() ?? "{}"));

        await EvalVoidAsyncInternal("window.__cgwTestMessages = [];");
        return list;
    }

    public Task ApplyStateAsync(PlayComposeUiState state) =>
        RunOnUiAsync(async () =>
        {
            var json = JsonSerializer.Serialize(state, JsonOptions);
            await EvalVoidAsyncInternal(
                $"(function(){{var fn=globalThis.__cgwPlayComposeApplyState;if(typeof fn==='function')fn({json});}})()");
            await PumpUiAsync(TimeSpan.FromMilliseconds(350));
        });

    public Task<string> GetInputValueAsync() =>
        EvalStringAsync(
            "(function(){var el=document.querySelector('#cgw-play-composer-root .cgw-compose-input');return el?el.value:'';})()");

    public Task<bool> IsInputFocusedAsync() =>
        EvalBoolAsync(
            "(function(){var el=document.querySelector('#cgw-play-composer-root .cgw-compose-input');return !!(el&&document.activeElement===el);})()");

    public Task<bool> IsInputReadOnlyAsync() =>
        EvalBoolAsync(
            "(function(){var el=document.querySelector('#cgw-play-composer-root .cgw-compose-input');return !!(el&&el.readOnly);})()");

    public Task<bool> IsSendDisabledAsync() =>
        EvalBoolAsync(
            "(function(){var el=document.querySelector('#cgw-play-composer-root .cgw-compose-send');return !!(el&&el.disabled);})()");

    public Task<bool> HasAcceptButtonAsync() =>
        EvalBoolAsync(
            "(function(){return !!document.querySelector('#cgw-play-composer-root .cgw-compose-accept');})()");

    public Task TriggerSendClickAsync() =>
        EvalVoidAsyncInternal(
            "(function(){var btn=document.querySelector('#cgw-play-composer-root .cgw-compose-send');if(btn)btn.click();})()");

    public Task SetNativeInputValueAsync(string text) =>
        EvalVoidAsyncInternal(
            $"(function(){{var el=document.getElementById('prompt-textarea');if(!el)return;el.value={JsonSerializer.Serialize(text)};el.dispatchEvent(new Event('input',{{bubbles:true}}));}})()");

    public Task<string> GetNativeInputValueAsync() =>
        EvalStringAsync(
            "(function(){var el=document.getElementById('prompt-textarea');return el?el.value:'';})()");

    public Task TriggerNativeSendClickAsync() =>
        EvalVoidAsyncInternal(
            "(function(){var btn=document.querySelector('[data-testid=\"composer-submit-button\"]');if(btn)btn.click();})()");

    public Task PressNativeEnterAsync(bool shift = false)
    {
        var shiftLiteral = shift ? "true" : "false";
        return EvalVoidAsyncInternal(
            "(function(){" +
            "var el=document.getElementById('prompt-textarea');" +
            "if(!el)return;" +
            "var opts={key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true,shiftKey:" + shiftLiteral + "};" +
            "el.dispatchEvent(new KeyboardEvent('keydown',opts));" +
            "})()");
    }

    public Task StageAttachmentAsync(string name, string mimeType, string content)
    {
        var contentJson = JsonSerializer.Serialize(content);
        var nameJson = JsonSerializer.Serialize(name);
        var mimeJson = JsonSerializer.Serialize(mimeType);
        return EvalVoidAsyncInternal(
            "(function(){" +
            "var input=document.querySelector('#cgw-play-composer-root .cgw-compose-file-input');" +
            "if(!input)return;" +
            "var dt=new DataTransfer();" +
            "dt.items.add(new File([" + contentJson + "], " + nameJson + ", {type:" + mimeJson + "}));" +
            "input.files=dt.files;" +
            "input.dispatchEvent(new Event('change',{bubbles:true}));" +
            "})();");
    }

    public Task<int> GetAttachmentCountAsync() =>
        RunOnUiAsync(async () =>
        {
            var raw = await EvalRawAsync(
                "(function(){return typeof globalThis.__cgwPlayComposeGetAttachmentCount==='function'?String(globalThis.__cgwPlayComposeGetAttachmentCount()):'0';})()");
            return int.TryParse(JsonSerializer.Deserialize<string>(raw), out var count) ? count : 0;
        });

    public Task SetInputValueAsync(string text) =>
        EvalVoidAsyncInternal(
            $"(function(){{var el=document.querySelector('#cgw-play-composer-root .cgw-compose-input');if(!el)return;el.value={JsonSerializer.Serialize(text)};el.dispatchEvent(new Event('input',{{bubbles:true}}));}})()");

    public Task PressEnterAsync(bool shift = false)
    {
        var shiftLiteral = shift ? "true" : "false";
        return EvalVoidAsyncInternal(
            "(function(){" +
            "var el=document.querySelector('#cgw-play-composer-root .cgw-compose-input');" +
            "if(!el)return;" +
            "var opts={key:'Enter',code:'Enter',keyCode:13,which:13,bubbles:true,cancelable:true,shiftKey:" + shiftLiteral + "};" +
            "el.dispatchEvent(new KeyboardEvent('keydown',opts));" +
            "})();");
    }

    public Task RememberTextareaAsync() =>
        EvalVoidAsyncInternal(
            "(function(){window.__cgwComposeTextareaRef=document.querySelector('#cgw-play-composer-root .cgw-compose-input');})()");

    public Task<bool> IsSameTextareaAsync() =>
        EvalBoolAsync(
            "(function(){var el=document.querySelector('#cgw-play-composer-root .cgw-compose-input');return !!(window.__cgwComposeTextareaRef&&el===window.__cgwComposeTextareaRef);})()");

    public Task SimulateComposerAnchorReplacementAsync() =>
        EvalVoidAsyncInternal(
            """
            (function(){
              var old=document.querySelector('[data-testid="composer"]');
              if(!old||!old.parentNode)return;
              var parent=old.parentNode;
              var fresh=document.createElement('div');
              fresh.setAttribute('data-testid','composer');
              fresh.innerHTML='<div data-testid="composer-text-input"><textarea id="prompt-textarea"></textarea></div><button data-testid="composer-submit-button">Native send</button>';
              parent.replaceChild(fresh,old);
              if(typeof globalThis.__cgwPlayComposeScheduleMount==='function'){
                globalThis.__cgwPlayComposeScheduleMount();
              }
            })();
            """);

    public Task FocusNativeTextareaAsync() =>
        EvalVoidAsyncInternal("document.getElementById('prompt-textarea')?.focus();");

    public Task<int> GetNativeTextareaTabIndexAsync() =>
        RunOnUiAsync(async () =>
        {
            var raw = await EvalRawAsync("(function(){var el=document.getElementById('prompt-textarea');return el?String(el.tabIndex):'';})()");
            return int.TryParse(JsonSerializer.Deserialize<string>(raw), out var idx) ? idx : 0;
        });

    public Task PumpUiAsync(TimeSpan duration) =>
        RunOnUiAsync(async () =>
        {
            var deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline)
            {
                Application.DoEvents();
                await Task.Delay(50);
            }
        });

    public Task WaitUntilAsync(string jsPredicate, TimeSpan timeout) =>
        RunOnUiAsync(async () =>
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (await EvalBoolAsync($"(function(){{return !!({jsPredicate});}})()"))
                    return;
                Application.DoEvents();
                await Task.Delay(50);
            }

            throw new TimeoutException($"Timed out waiting for: {jsPredicate}");
        });

    public Task<bool> EvalBoolAsync(string script) => EvalBoolAsyncInternal(script);

    public Task<string> EvalStringAsync(string script) => EvalStringAsyncInternal(script);

    public Task EvalVoidAsync(string script) => EvalVoidAsyncInternal(script);

    public Task DisposeAsync()
    {
        if (_userDataFolder is not null && Directory.Exists(_userDataFolder))
        {
            try { Directory.Delete(_userDataFolder, recursive: true); } catch { /* ignore */ }
        }

        _initialized = false;
        PlayComposeUiEnvironment.Shutdown();
        return Task.CompletedTask;
    }

    private async Task InjectComposeAssetsAsync()
    {
        var payload = WrapperAssetBundle.BuildCssJsBundle(
            "cgw-play-compose.css",
            "__cgwPlayComposeCss",
            "cgw-play-compose-css",
            "cgw-play-compose.js");
        if (string.IsNullOrWhiteSpace(payload))
            throw new FileNotFoundException("Compose script missing.");

        await EvalVoidAsyncInternal(
            """
            window.__cgwTestMessages = [];
            if (!window.chrome) window.chrome = {};
            if (!window.chrome.webview) window.chrome.webview = {};
            window.chrome.webview.postMessage = function(msg) {
              var raw = typeof msg === 'string' ? msg : JSON.stringify(msg);
              window.__cgwTestMessages.push(raw);
              try {
                var obj = JSON.parse(raw);
                if (obj && obj.type === 'cgwComposeUploadRequest') {
                  setTimeout(function () {
                    if (typeof globalThis.__cgwPlayComposeSetUploadStatus === 'function') {
                      globalThis.__cgwPlayComposeSetUploadStatus(
                        obj.jobId,
                        obj.attachmentIds || [],
                        'ready',
                        null
                      );
                    }
                  }, 25);
                }
              } catch (_e) { /* ignore */ }
            };
            """);

        await Core!.ExecuteScriptAsync(payload);
    }

    private Task RunOnUiAsync(Func<Task> work)
    {
        if (_form is null)
            throw new InvalidOperationException("Host not initialized.");

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(new Action(async () =>
        {
            try
            {
                await work();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }));
        return tcs.Task;
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> work)
    {
        if (_form is null)
            throw new InvalidOperationException("Host not initialized.");

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _form.BeginInvoke(new Action(async () =>
        {
            try
            {
                tcs.TrySetResult(await work());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }));
        return tcs.Task;
    }

    private Task EvalVoidAsyncInternal(string script) =>
        RunOnUiAsync(async () => { await Core!.ExecuteScriptAsync(script); });

    private Task<string> EvalRawAsync(string script) =>
        RunOnUiAsync(async () => await Core!.ExecuteScriptAsync(script));

    private Task<string> EvalStringAsyncInternal(string script) =>
        RunOnUiAsync(async () =>
        {
            var raw = await Core!.ExecuteScriptAsync(script);
            return JsonSerializer.Deserialize<string>(raw) ?? "";
        });

    private Task<bool> EvalBoolAsyncInternal(string script) =>
        RunOnUiAsync(async () =>
        {
            var raw = await Core!.ExecuteScriptAsync(script);
            return raw == "true";
        });
}

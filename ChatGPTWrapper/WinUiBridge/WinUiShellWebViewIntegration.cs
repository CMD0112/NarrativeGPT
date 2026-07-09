using ChatGPTWrapper.WebView;
using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WinUiBridge;

/// <summary>
/// Wires shell chat tabs with the same page features as WPF <c>InitializeChatWebViewAsync</c>.
/// </summary>
public static class WinUiShellWebViewIntegration
{
    public static object? GetCoreWebView2(object webView)
    {
        try
        {
            return ((dynamic)webView).CoreWebView2;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsInjectableSource(string? source) =>
        ChatGptPageGate.IsInjectable(source);

    public static ChatGptPageHost CreateShellHost(object coreWebView2)
    {
        WinUiWebView2CoreRuntime.EnsureManagedCoreLoaded();

        if (!WinUiWebView2CoreRuntime.TryAsCore(coreWebView2, out _))
            throw new ArgumentException("Expected CoreWebView2.", nameof(coreWebView2));

        var typedCore = WinUiWebView2CoreRuntime.RequireTypedCore(coreWebView2);
        var host = new ChatGptPageHost(typedCore);
        host.RegisterFeature(new ShellStylePageFeature());
        host.RegisterFeature(new ShellContinuousViewPageFeature(typedCore));
        host.RegisterFeature(new ShellContextTagsPageFeature());
        host.Wire();
        return host;
    }

    public static Task ApplyAllAsync(ChatGptPageHost host) => host.ApplyAllAsync();

    public static async Task ApplyTranscriptViewModeAsync(
        object? coreObj,
        ChatGptPageHost host,
        bool includeLibraries = false,
        UiChromeSettings? settings = null,
        int? revisionOverride = null)
    {
        WinUiWebView2CoreRuntime.EnsureManagedCoreLoaded();

        if (!WinUiWebView2CoreRuntime.TryAsCore(coreObj, out var core) || core is null)
            return;

        settings ??= UiChromeStore.Load();
        if (!IsTrustedInjectable(core))
            return;

        var typedCore = WinUiWebView2CoreRuntime.RequireTypedCore(core);
        await ChatGptStyleInjection.ReapplyAsync(typedCore);

        var script = includeLibraries
            ? ChatGptContinuousViewInjection.BuildFullInjectionScript(settings)
            : ChromePreferencesApplier.BuildApplyScript(settings, revisionOverride);

        if (!string.IsNullOrWhiteSpace(script))
            await WebView2ManagedCoreRuntime.ExecuteScriptAsync(core, script);

        await host.ApplyFeatureAsync(PageFeatureIds.ContextTags, typedCore);

        if (settings.IsTranscriptOverlayActive)
            await ShellContinuousViewPageFeature.ScheduleNavigateAsync(typedCore);
    }

    public static Task ApplyFeatureAsync(ChatGptPageHost host, string featureId, object? coreObj)
    {
        WinUiWebView2CoreRuntime.EnsureManagedCoreLoaded();

        if (!WinUiWebView2CoreRuntime.TryAsCore(coreObj, out var core) || core is null)
            return Task.CompletedTask;

        return host.ApplyFeatureAsync(featureId, WinUiWebView2CoreRuntime.RequireTypedCore(core));
    }

    private static bool IsTrustedInjectable(object? coreObj)
    {
        var source = WebView2ManagedCoreRuntime.GetSource(coreObj);
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
               && ChatGptUrls.IsTrustedChatGptTopLevelUri(uri);
    }

    private sealed class ShellStylePageFeature : IPageFeature
    {
        public string FeatureId => PageFeatureIds.Style;

        public Task ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken = default) =>
            ChatGptStyleInjection.ReapplyAsync(core);

        public void RegisterMessageHandlers(PageMessageRouter router)
        {
        }
    }

    private sealed class ShellContinuousViewPageFeature : IPageFeature
    {
        private readonly CoreWebView2 _core;
        private readonly object _historyGate = new();
        private CancellationTokenSource? _historyDebounce;
        private bool _historyWired;

        public ShellContinuousViewPageFeature(CoreWebView2 core) => _core = core;

        public string FeatureId => PageFeatureIds.ContinuousView;

        public Task ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken = default)
        {
            EnsureHistorySubscription();
            return ApplyContinuousViewAsync(core);
        }

        public void RegisterMessageHandlers(PageMessageRouter router)
        {
        }

        private void EnsureHistorySubscription()
        {
            if (_historyWired)
                return;

            _core.HistoryChanged += OnHistoryChanged;
            _historyWired = true;
        }

        private async void OnHistoryChanged(object? sender, object e)
        {
            if (sender is not CoreWebView2 core || !ChatGptPageGate.IsInjectable(core.Source))
                return;

            lock (_historyGate)
            {
                _historyDebounce?.Cancel();
                _historyDebounce?.Dispose();
                _historyDebounce = new CancellationTokenSource();
            }

            var token = _historyDebounce!.Token;
            try
            {
                await Task.Delay(40, token);
                await ScheduleNavigateAsync(core);
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                // Ignore transient failures during teardown or before document exists.
            }
        }

        internal static async Task ApplyContinuousViewAsync(CoreWebView2 core)
        {
            if (!ChatGptPageGate.IsInjectable(core.Source))
                return;

            var chrome = UiChromeStore.Load();
            var script = ChatGptContinuousViewInjection.BuildFullInjectionScript(chrome);
            if (string.IsNullOrWhiteSpace(script))
                return;

            try
            {
                await core.ExecuteScriptAsync(script);
            }
            catch
            {
                // Ignore transient failures during teardown or before document exists.
            }
        }

        internal static Task ScheduleNavigateAsync(CoreWebView2 core)
        {
            if (!ChatGptPageGate.IsInjectable(core.Source))
                return Task.CompletedTask;

            const string navigateScript =
                "(function(){if(typeof globalThis.__cgwContinuousViewNavigate===\"function\")" +
                "globalThis.__cgwContinuousViewNavigate();" +
                "else if(typeof globalThis.__cgwContinuousViewSchedule===\"function\")" +
                "globalThis.__cgwContinuousViewSchedule({immediate:true});})();";

            try
            {
                return core.ExecuteScriptAsync(navigateScript);
            }
            catch
            {
                return Task.CompletedTask;
            }
        }
    }

    private sealed class ShellContextTagsPageFeature : IPageFeature
    {
        public string FeatureId => PageFeatureIds.ContextTags;

        public Task ApplyAsync(CoreWebView2 core, CancellationToken cancellationToken = default)
        {
            var chrome = UiChromeStore.Load();
            if (chrome.IsTranscriptOverlayActive)
                return Task.CompletedTask;

            var mode = chrome.ActiveModeSettings();
            var script = ChatGptContextTagsInjection.BuildPreferenceScript(
                mode.HideContextTagsInThread,
                mode.ExpandHiddenContextInThread);

            try
            {
                return core.ExecuteScriptAsync(script);
            }
            catch
            {
                return Task.CompletedTask;
            }
        }

        public void RegisterMessageHandlers(PageMessageRouter router)
        {
        }
    }
}

using System.IO;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WpfApplication = System.Windows.Application;
using WpfWindow = System.Windows.Window;

namespace ChatGPTWrapper;

/// <summary>Hidden WPF WebView for Project API — must run on the WinUI WPF STA thread.</summary>
public static class WpfStaProjectHostBridge
{
    private static readonly object Gate = new();
    private static readonly Dictionary<WebView2, ChatGptApiBridgeInjection> Bridges = new();
    private static ChatGptProjectHost? _host;
    private static WebView2? _apiWebView;
    private static CoreWebView2Environment? _environment;
    private static Task? _initTask;

    public static Func<Guid, bool, Task>? EnsureAdventureTabAsync { get; set; }

    /// <summary>Set from WinUI startup to marshal all project-host work onto <see cref="WinUi.Services.WpfStaHost"/>.</summary>
    public static Func<Func<Task>, Task>? RunOnStaThreadAsync { get; set; }

    public static Task InvokeAsync(Func<IChatGptProjectHost, Task> action) =>
        RunOnStaThreadAsync?.Invoke(() => InvokeCoreAsync(action))
        ?? InvokeCoreAsync(action);

    public static Task<T> InvokeAsync<T>(Func<IChatGptProjectHost, Task<T>> action)
    {
        if (RunOnStaThreadAsync is null)
            return InvokeCoreAsync(action);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = RunOnStaThreadAsync(async () =>
        {
            try
            {
                tcs.SetResult(await InvokeCoreAsync(action).ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private static async Task InvokeCoreAsync(Func<IChatGptProjectHost, Task> action)
    {
        var host = await EnsureHostAsync().ConfigureAwait(true);
        await action(host).ConfigureAwait(true);
    }

    private static async Task<T> InvokeCoreAsync<T>(Func<IChatGptProjectHost, Task<T>> action)
    {
        var host = await EnsureHostAsync().ConfigureAwait(true);
        return await action(host).ConfigureAwait(true);
    }

    private static async Task<IChatGptProjectHost> EnsureHostAsync()
    {
        lock (Gate)
        {
            if (_host is not null)
                return _host;

            if (WpfApplication.Current is null)
            {
                throw new InvalidOperationException(
                    "WPF Application is not initialized. Set WpfStaProjectHostBridge.RunOnStaThreadAsync from the WinUI host.");
            }

            var helperWindow = new WpfWindow
            {
                WindowStyle = System.Windows.WindowStyle.None,
                ShowInTaskbar = false,
                Width = 0,
                Height = 0,
            };
            helperWindow.Show();

            _apiWebView = new WebView2();
            helperWindow.Content = _apiWebView;

            _host = new ChatGptProjectHost(new ChatGptProjectHostDependencies
            {
                GetEnvironment = () => _environment,
                FindWebView = () => _apiWebView,
                EnsureAdventureTabAsync = EnsureAdventureTabCoreAsync,
                GetOrRegisterBridge = GetOrRegisterBridge,
                SelectTab = _ => { },
                RequestShowBrowserPane = _ => { },
                WireServices = _ => { },
            });

            _initTask = InitializeEnvironmentAsync();
        }

        if (_initTask is not null)
            await _initTask.ConfigureAwait(true);

        return _host!;
    }

    private static ChatGptApiBridgeInjection GetOrRegisterBridge(WebView2 webView)
    {
        if (!Bridges.TryGetValue(webView, out var bridge))
        {
            bridge = new ChatGptApiBridgeInjection(webView);
            Bridges[webView] = bridge;
        }

        if (webView.CoreWebView2 is not null && !bridge.IsRegistered)
            bridge.Register();

        return bridge;
    }

    private static async Task InitializeEnvironmentAsync()
    {
        if (_apiWebView is null || _environment is not null)
            return;

        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChatGPTWrapper",
            "WebView2UserData");
        Directory.CreateDirectory(userDataFolder);
        _environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder);
        await _apiWebView.EnsureCoreWebView2Async(_environment);
        GetOrRegisterBridge(_apiWebView);
    }

    private static async Task<WebView2?> EnsureAdventureTabCoreAsync(Guid adventureId, bool selectTab)
    {
        if (adventureId != Guid.Empty && EnsureAdventureTabAsync is not null)
            await EnsureAdventureTabAsync(adventureId, selectTab).ConfigureAwait(true);

        return _apiWebView;
    }
}

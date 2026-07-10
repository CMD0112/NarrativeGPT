using System.Windows.Threading;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.ApiDiagnostics.Unit;

/// <summary>
/// STA dispatcher for WPF tab-control harness tests (pin key lookup, session tab resolution).
/// </summary>
internal static class PlayTabUiEnvironment
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly TaskCompletionSource<Dispatcher> Ready =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static Thread? _uiThread;
    private static bool _started;

    public static Task<Dispatcher> GetDispatcherAsync()
    {
        if (_started)
            return Ready.Task;

        return StartAsync();
    }

    public static Task<T> RunAsync<T>(Func<T> work) =>
        RunAsync(() => Task.FromResult(work()));

    public static async Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var dispatcher = await GetDispatcherAsync().ConfigureAwait(false);
        return await await dispatcher.InvokeAsync(work).Task.ConfigureAwait(false);
    }

    public static Task RunAsync(Action work) =>
        RunAsync(() =>
        {
            work();
            return true;
        });

    private static async Task<Dispatcher> StartAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_started)
                return await Ready.Task;

            _uiThread = new Thread(() =>
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                Ready.TrySetResult(dispatcher);
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "PlayTabHarnessUi",
            };
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            _started = true;
        }
        finally
        {
            Gate.Release();
        }

        return await Ready.Task;
    }
}

/// <summary>
/// Builds a <see cref="TabControl"/> with tagged WebView2 tabs for pin-resolution tests.
/// </summary>
internal sealed class PlayTabPinHarness
{
    public System.Windows.Controls.TabControl Tabs { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await PlayTabUiEnvironment.RunAsync(() =>
        {
            Tabs = new System.Windows.Controls.TabControl();
        });
    }

    public Task<(WebView2 WebView, string TabKey)> AddTabAsync(string? presetKey = null, string? title = null) =>
        PlayTabUiEnvironment.RunAsync(() =>
        {
            var key = presetKey ?? Guid.NewGuid().ToString("N");
            var webView = new WebView2();
            var tab = new TabItem
            {
                Header = title ?? "ChatGPT",
                Content = webView,
                Tag = key,
            };
            Tabs.Items.Add(tab);
            return (webView, key);
        });

    public Task<T> OnUiAsync<T>(Func<T> work) =>
        PlayTabUiEnvironment.RunAsync(work);

    public Task OnUiAsync(Action work) =>
        PlayTabUiEnvironment.RunAsync(work);

    public static AdventureBundle CreateInMemoryRegistryPinnedBundle(
        string pinTabKey,
        string? conversationId = "thread-registry",
        string? projectId = "g-p-injection")
    {
        var bundle = new AdventureBundle
        {
            Metadata = new AdventureMetadata
            {
                LinkedProjectId = projectId,
            },
        };

        if (!string.IsNullOrWhiteSpace(conversationId))
            PlayThreadBindingService.MarkVerified(bundle, conversationId);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = pinTabKey;
        entry.PinnedTabTitle = "Pinned play tab";
        bundle.Metadata.PinnedPlayTabKey = null;
        return bundle;
    }

    public static AdventureBundle CreateRegistryPinnedBundle(
        string pinTabKey,
        string? conversationId = "thread-registry",
        string? projectId = "g-p-injection")
    {
        var bundle = AdventureStore.CreateNew("Injection pin harness");
        bundle.Metadata.LinkedProjectId = projectId;
        if (!string.IsNullOrWhiteSpace(conversationId))
            PlayThreadBindingService.MarkVerified(bundle, conversationId);

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        var entry = AdventureThreadRegistryService.GetOrCreateActiveEntry(bundle, AdventureThreadKind.Play);
        entry.PinnedTabKey = pinTabKey;
        entry.PinnedTabTitle = "Pinned play tab";
        AdventureStore.Save(bundle);

        var reloaded = AdventureStore.Load(bundle.Metadata.Id)!;
        AssertMetadataPinStripped(reloaded);
        return reloaded;
    }

    private static void AssertMetadataPinStripped(AdventureBundle bundle)
    {
        if (!string.IsNullOrWhiteSpace(bundle.Metadata.PinnedPlayTabKey))
            throw new InvalidOperationException("Expected schema-6 save to strip metadata pin key.");
    }
}

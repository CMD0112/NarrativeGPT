using ChatGPTWrapper.Shell;
using ChatGPTWrapper.WinUI.Views;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>WinUI shell accessors for thread manager, project host, and cross-thread UI marshaling.</summary>
internal static class WinUiShellHost
{
    private static MainWindow? _window;
    private static WinUiPlaySessionService? _session;
    private static ShellNavigationService? _navigation;

    public static void Register(
        MainWindow window,
        WinUiPlaySessionService session,
        ShellNavigationService navigation)
    {
        _window = window;
        _session = session;
        _navigation = navigation;
    }

    public static WinUiPlaySessionService Session =>
        _session ?? throw new InvalidOperationException("WinUI shell host is not registered.");

    public static ShellNavigationService Navigation =>
        _navigation ?? throw new InvalidOperationException("WinUI shell host is not registered.");

    public static XamlRoot? XamlRoot => _window?.Content.XamlRoot;

    public static ChatTabHost? GetShellChatHost() => _window?.ShellChatHostControl;

    public static ChatTabHost? GetSessionChatHost() => GetShellChatHost();

    public static void ApplyPlayWorkspaceLayout() => _window?.ApplyPlayWorkspaceLayout();

    public static void SyncPlayCompanionWidth(double companionWidth, bool collapsed) =>
        _window?.SyncPlayCompanionWidth(companionWidth, collapsed);

    public static double GetCompanionPanelWidth() => _window?.GetCompanionPanelWidth() ?? 0;

    public static double GetShellBodyWidth() => _window?.GetShellBodyWidth() ?? 0;

    public static Task RunOnUiThreadAsync(Func<Task> action) =>
        RunOnUiThreadAsync(async () =>
        {
            await action();
            return true;
        });

    public static Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (_window?.DispatcherQueue is not { } queue)
            throw new InvalidOperationException("WinUI shell host is not registered.");

        if (queue.HasThreadAccess)
            return action();

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.TryEnqueue(async () =>
        {
            try
            {
                tcs.SetResult(await action());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public static Task RunOnUiThreadAsync(Action action) =>
        RunOnUiThreadAsync(() =>
        {
            action();
            return Task.CompletedTask;
        });

    /// <summary>Marshal to the WinUI dispatcher; safe when called from WPF STA dialog threads.</summary>
    public static T RunOnUiThreadSync<T>(Func<T> action, T fallback = default!)
    {
        if (_window?.DispatcherQueue is not { } queue)
            return action();

        if (queue.HasThreadAccess)
            return action();

        try
        {
            return RunOnUiThreadAsync(() => Task.FromResult(action())).GetAwaiter().GetResult();
        }
        catch
        {
            return fallback;
        }
    }

    public static void RefreshSessionChrome() => _window?.RefreshSessionChromeFromHost();

    public static void SetUtilityJobCount(int count) => _window?.SetUtilityJobCount(count);
}

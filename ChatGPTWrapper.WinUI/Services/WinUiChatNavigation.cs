using Microsoft.Web.WebView2.Core;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Host-neutral navigation wait helpers for WinUI WebView2 cores.</summary>
internal static class WinUiChatNavigation
{
    public static bool IsAtDestination(object? core, string? expectedDestination)
    {
        if (core is null || string.IsNullOrWhiteSpace(expectedDestination))
            return false;

        try
        {
            var source = core switch
            {
                CoreWebView2 typed => typed.Source,
                _ => ((dynamic)core).Source as string,
            };
            return string.Equals(source, expectedDestination, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task WaitForNavigationAsync(
        object core,
        string? expectedDestination = null,
        int timeoutMs = 20000,
        CancellationToken cancellationToken = default)
    {
        if (IsAtDestination(core, expectedDestination))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (core is CoreWebView2 typedCore)
        {
            void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs args)
            {
                if (args.IsSuccess && IsAtDestination(core, expectedDestination))
                    tcs.TrySetResult();
            }

            typedCore.NavigationCompleted += Handler;
            try
            {
                await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
            }
            catch (TimeoutException)
            {
                /* best effort */
            }
            finally
            {
                typedCore.NavigationCompleted -= Handler;
            }

            return;
        }

        dynamic dynamicCore = core;
        EventHandler<object> dynamicHandler = (_, args) =>
        {
            try
            {
                dynamic eventArgs = args;
                if ((bool)eventArgs.IsSuccess && IsAtDestination(core, expectedDestination))
                    tcs.TrySetResult();
            }
            catch
            {
                /* ignore handler errors */
            }
        };

        dynamicCore.NavigationCompleted += dynamicHandler;
        try
        {
            await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), cancellationToken);
        }
        catch (TimeoutException)
        {
            /* best effort */
        }
        finally
        {
            dynamicCore.NavigationCompleted -= dynamicHandler;
        }
    }

    public static void Navigate(object core, string url)
    {
        if (core is CoreWebView2 typedCore)
        {
            typedCore.Navigate(url);
            return;
        }

        dynamic dynamicCore = core;
        dynamicCore.Navigate(url);
    }
}

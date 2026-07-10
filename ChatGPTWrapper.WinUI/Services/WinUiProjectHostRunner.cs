using ChatGPTWrapper;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Runs project API work on the WPF STA host and returns to the WinUI caller.</summary>
internal static class WinUiProjectHostRunner
{
    public static Task RunAsync(Func<IChatGptProjectHost, Task> action) =>
        WpfStaProjectHostBridge.InvokeAsync(async host =>
        {
            if (!host.TryEnterOperation())
                return;

            try
            {
                await action(host);
            }
            finally
            {
                host.ExitOperation();
            }
        });

    public static Task<T> RunAsync<T>(Func<IChatGptProjectHost, Task<T>> action) =>
        WpfStaProjectHostBridge.InvokeAsync(async host =>
        {
            if (!host.TryEnterOperation())
                return default!;

            try
            {
                return await action(host);
            }
            finally
            {
                host.ExitOperation();
            }
        });
}

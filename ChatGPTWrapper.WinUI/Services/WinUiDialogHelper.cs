using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;

namespace ChatGPTWrapper.WinUI.Services;

internal static class WinUiDialogHelper
{
    public static XamlRoot RequireXamlRoot(Window? owner)
    {
        if (owner?.Content is FrameworkElement { XamlRoot: { } root })
            return root;

        if (App.CurrentMainWindow?.Content is FrameworkElement { XamlRoot: { } mainRoot })
            return mainRoot;

        throw new InvalidOperationException("No XamlRoot is available for WinUI dialogs.");
    }

    public static async Task<ContentDialogResult> ShowAsync(ContentDialog dialog, Window? owner)
    {
        dialog.XamlRoot = RequireXamlRoot(owner);
        return await dialog.ShowAsync();
    }

    /// <summary>Await after <see cref="ContentDialog.Hide"/> before opening another dialog on the same XamlRoot.</summary>
    public static Task WaitForCloseAsync(ContentDialog dialog)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(ContentDialog sender, ContentDialogClosedEventArgs args)
        {
            dialog.Closed -= Handler;
            tcs.TrySetResult();
        }

        dialog.Closed += Handler;
        return tcs.Task;
    }

    public static void InitializeWithOwner(object picker, Window owner)
    {
        var hwnd = WindowNative.GetWindowHandle(owner);
        InitializeWithWindow.Initialize(picker, hwnd);
    }

    public static async Task ShowInfoAsync(Window? owner, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
        };
        await ShowAsync(dialog, owner);
    }

    public static async Task<bool> ConfirmAsync(
        Window? owner,
        string title,
        string message,
        string confirmText = "OK",
        string cancelText = "Cancel")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = confirmText,
            CloseButtonText = cancelText,
            DefaultButton = ContentDialogButton.Close,
        };
        return await ShowAsync(dialog, owner) == ContentDialogResult.Primary;
    }
}

using ChatGPTWrapper.Adventure.Services.PlaySend;
using ChatGPTWrapper.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Services;

/// <summary>Blocks navigation away from play when send is active or compose has unsent input.</summary>
internal static class WinUiPlayNavigationGuard
{
    public static async Task<bool> ConfirmLeavePlayAsync(
        ShellNavigationService navigation,
        WinUiPlaySessionService session,
        XamlRoot xamlRoot)
    {
        if (navigation.Mode != AppMode.Play)
            return true;

        var sendHost = session.SendHost;
        if (!await sendHost.TryAcquireSendGateAsync())
        {
            var busy = new ContentDialog
            {
                Title = "Send in progress",
                Content = "Wait for the current send to finish before leaving play.",
                CloseButtonText = "OK",
                XamlRoot = xamlRoot,
            };
            await busy.ShowAsync();
            return false;
        }

        sendHost.ReleaseSendGate();

        var compose = session.GetActiveComposeInjection();
        var hasDraft = !string.IsNullOrWhiteSpace(compose?.GetText())
                       || !string.IsNullOrWhiteSpace(session.GetMergedPreview());
        if (!hasDraft)
            return true;

        var dialog = new ContentDialog
        {
            Title = "Leave play session?",
            Content = "You have unsent compose text. Leave anyway?",
            PrimaryButtonText = "Leave",
            CloseButtonText = "Stay",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

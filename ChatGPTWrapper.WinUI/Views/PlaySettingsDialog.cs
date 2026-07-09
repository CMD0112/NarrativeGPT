using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public static class PlaySettingsDialog
{
    public static async Task ShowAsync(Guid adventureId)
    {
        await WinUiDialogHostService.ShowPlaySettingsAsync(App.CurrentMainWindow, adventureId);
    }
}

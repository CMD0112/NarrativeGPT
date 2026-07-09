using ChatGPTWrapper;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class WrapperSettingsPage : UserControl
{
    private Window? _ownerWindow;

    public WrapperSettingsPage()
    {
        InitializeComponent();
        var settings = WrapperSettingsStore.Current;
        AdventuresPathBox.Text = settings.AdventuresDirectoryOverride ?? string.Empty;
        DefaultPathLine.Text = $"Default: {AppDirectories.DefaultAdventuresDirectory}";
    }

    public void SetOwnerWindow(Window owner) => _ownerWindow = owner;

    public string AdventuresPathText => AdventuresPathBox.Text.Trim();

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (_ownerWindow is null)
            return;

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add("*");
        WinUiDialogHelper.InitializeWithOwner(picker, _ownerWindow);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            AdventuresPathBox.Text = folder.Path;
    }

    private void Default_Click(object sender, RoutedEventArgs e) =>
        AdventuresPathBox.Text = string.Empty;
}

using System.IO;
using System.Windows.Interop;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using WpfWindow = System.Windows.Window;

namespace ChatGPTWrapper.WinUI.Services;

internal static class WpfDialogHostService
{
    public static Task ShowPlaySettingsAsync(Window? owner, Guid adventureId, PlaySettingsTab initialTab = PlaySettingsTab.Injection) =>
        WinUiDialogHostService.ShowPlaySettingsAsync(owner, adventureId, initialTab);

    public static Task ShowThreadManagerAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowThreadManagerAsync(owner, adventureId);

    public static Task ShowThreadManagerAsync(Window? owner, Guid adventureId, AdventureThreadKind? initialKind = null) =>
        WinUiDialogHostService.ShowThreadManagerAsync(owner, adventureId, initialKind);

    public static Task ShowProposalReviewAsync(
        Window? owner,
        Guid adventureId,
        ProposalReviewCategory? initialCategory = null) =>
        WinUiDialogHostService.ShowProposalReviewAsync(owner, adventureId, initialCategory);

    public static Task ShowFormatDialogAsync(Window? owner, Guid? adventureId = null) =>
        WinUiDialogHostService.ShowFormatDialogAsync(owner, adventureId);

    public static Task ShowThemeCustomizationAsync(Window? owner) =>
        WinUiDialogHostService.ShowThemeCustomizationAsync(owner);

    public static Task ShowEntityEditAsync(Window? owner, Guid adventureId, EntityReferenceRow? row = null) =>
        WinUiDialogHostService.ShowEntityEditAsync(owner, adventureId, row);

    public static Task ShowJsonImportReviewAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowJsonImportReviewAsync(owner, adventureId);

    public static Task ShowDesignWizardAsync(Window? owner, Guid? adventureId = null) =>
        RunModalAsync(owner, () =>
        {
            if (adventureId is { } id)
                new AdventureDesignWizard(id).ShowDialog();
            else
                new AdventureDesignWizard().ShowDialog();
            return true;
        });

    public static Task ShowLocalInferenceLabAsync(Window? owner) =>
        RunModalAsync(owner, () =>
        {
            LocalInferenceLabDialog.ShowForOwner(null);
            return true;
        });

    public static Task<bool> ShowRenameAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowRenameAsync(owner, adventureId);

    public static Task ShowRecapAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowRecapAsync(owner, adventureId);

    public static Task<Models.ScenarioCreationOutcome> ShowScenarioCreationAsync(Window? owner) =>
        WinUiDialogHostService.ShowScenarioCreationAsync(owner);

    public static Task ShowExportAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowExportAsync(owner, adventureId);

    public static Task<bool> ShowImportBackupAsync(Window? owner) =>
        WinUiDialogHostService.ShowImportBackupAsync(owner);

    public static Task ShowWrapperSettingsAsync(Window? owner) =>
        WinUiDialogHostService.ShowWrapperSettingsAsync(owner);

    public static Task ShowSearchAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowSearchAsync(owner, adventureId);

    public static Task ShowPlayHandoffAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowPlayHandoffAsync(owner, adventureId);

    public static Task ShowSourceManagerAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowSourceManagerAsync(owner, adventureId);

    public static Task ShowProjectWorkspaceAsync(Window? owner, Guid adventureId) =>
        WinUiDialogHostService.ShowProjectWorkspaceAsync(owner, adventureId);

    public static Task<bool> ShowEntityDeleteAsync(Window? owner, Guid adventureId, EntityReferenceRow row) =>
        WinUiDialogHostService.ShowEntityDeleteAsync(owner, adventureId, row);

    public static Task<bool> ShowEntityMergeAsync(
        Window? owner,
        Guid adventureId,
        EntityReferenceRow row,
        string categoryFilter) =>
        WinUiDialogHostService.ShowEntityMergeAsync(owner, adventureId, row, categoryFilter);

    public static Task<bool> ShowEntityRetireAsync(
        Window? owner,
        Guid adventureId,
        EntityReferenceRow row,
        string categoryFilter) =>
        WinUiDialogHostService.ShowEntityRetireAsync(owner, adventureId, row, categoryFilter);

    private static async Task RunModalAsync(Window? owner, Func<bool> show)
    {
        _ = await RunModalAsync<bool>(owner, show);
    }

    private static Task<T> RunModalAsync<T>(Window? owner, Func<T> show)
    {
        return WpfStaHost.InvokeAsync(() =>
        {
            try
            {
                if (System.Windows.Application.Current is { } wpfApp)
                    WpfStaThemeBootstrap.EnsureApplied(wpfApp);

                var result = ShowOwnedDialog(owner, show);
                WinUiShellCoordinator.ScheduleShellRefresh(refreshWebView: true);
                return result;
            }
            catch (Exception ex)
            {
                DiagnosticsMirror.LogException("wpf_dialog_failed", ex);
                _ = WinUiShellHost.RunOnUiThreadAsync(() =>
                    WinUiDialogHelper.ShowInfoAsync(
                        App.CurrentMainWindow,
                        "Dialog failed",
                        ex.Message));
                return default!;
            }
        });
    }

    private static T ShowOwnedDialog<T>(Window? owner, Func<T> show)
    {
        if (owner is null)
            return show();

        var helperWindow = new WpfWindow
        {
            WindowStyle = System.Windows.WindowStyle.None,
            ShowInTaskbar = false,
            Width = 0,
            Height = 0,
        };
        helperWindow.Show();
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(owner);
            new WindowInteropHelper(helperWindow) { Owner = hwnd };
            return show();
        }
        finally
        {
            helperWindow.Close();
        }
    }
}

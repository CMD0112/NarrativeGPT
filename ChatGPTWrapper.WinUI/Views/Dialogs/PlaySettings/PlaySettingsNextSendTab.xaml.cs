using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsNextSendTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;
    private bool _suppress;

    public PlaySettingsNextSendTab()
    {
        InitializeComponent();
    }

    public event EventHandler? SettingsChanged;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        _suppress = true;
        try
        {
            QueueBox.Text = string.Join(Environment.NewLine, context.Bundle.ContinuationQueue);

            WinUiNarratorComboHelper.Populate(
                TurnOverrideResponseLengthCombo, context.Bundle, NarratorParameter.ResponseLength, NarratorOverrideScope.Turn);
            WinUiNarratorComboHelper.Populate(
                TurnOverrideDetailLevelCombo, context.Bundle, NarratorParameter.DetailLevel, NarratorOverrideScope.Turn);
            WinUiNarratorComboHelper.Populate(
                TurnOverrideToneCombo, context.Bundle, NarratorParameter.Tone, NarratorOverrideScope.Turn, isEditable: true);
            WinUiNarratorComboHelper.Populate(
                TurnOverrideDifficultyCombo, context.Bundle, NarratorParameter.Difficulty, NarratorOverrideScope.Turn, isEditable: true);
        }
        finally
        {
            _suppress = false;
        }
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        context.Bundle.ContinuationQueue = QueueBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        PlaySettingsStore.MirrorContinuationQueue(context.Bundle);

        var staging = new AdventureBundle
        {
            Metadata = new AdventureMetadata { Settings = context.Bundle.Metadata.Settings },
        };
        WinUiNarratorComboHelper.Save(staging, TurnOverrideResponseLengthCombo, NarratorParameter.ResponseLength, NarratorOverrideScope.Turn);
        WinUiNarratorComboHelper.Save(staging, TurnOverrideDetailLevelCombo, NarratorParameter.DetailLevel, NarratorOverrideScope.Turn);
        WinUiNarratorComboHelper.Save(staging, TurnOverrideToneCombo, NarratorParameter.Tone, NarratorOverrideScope.Turn);
        WinUiNarratorComboHelper.Save(staging, TurnOverrideDifficultyCombo, NarratorParameter.Difficulty, NarratorOverrideScope.Turn);
    }

    private void ResetTurnOverrides_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        NarratorOverrideResolver.ClearTurnOverrides(_ctx.Bundle.Metadata.Settings);
        Bind(_ctx);
        OnChanged(sender, e);
    }

    private void GoToPreview_Click(object sender, RoutedEventArgs e) =>
        _ctx?.NavigateToTab?.Invoke(ChatGPTWrapper.Views.PlaySettingsTab.Preview);

    private async void CopyRepairPacket_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        var playerLine = _ctx.PreviewPlayerLine?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(playerLine))
        {
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Repair packet", "Enter a player line first.");
            return;
        }

        var priorCount = _ctx.Host?.ResolveThreadUserTurnCountAsync is { } resolve
            ? await resolve()
            : 0;
        var repairTurnIndex = PlaySendRepairService.ResolveRepairTurnIndex(_ctx.Bundle, priorCount);
        var attachment = _ctx.Host?.ResolvePreviewAttachmentContext?.Invoke();
        var prepared = PlaySendRepairService.PrepareRepairPacket(_ctx.Bundle, playerLine, repairTurnIndex, attachment);
        var clipboardText = PlaySendRepairService.AssembleRepairClipboardText(prepared.MergedText, repairTurnIndex);
        var package = new DataPackage();
        package.SetText(clipboardText);
        Clipboard.SetContent(package);
        await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Repair packet", "Repair packet copied to clipboard.");
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppress || _ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.NotifySettingsChanged();
    }
}

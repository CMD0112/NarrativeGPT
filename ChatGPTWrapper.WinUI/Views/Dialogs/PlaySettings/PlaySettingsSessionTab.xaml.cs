using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsSessionTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;

    public PlaySettingsSessionTab()
    {
        InitializeComponent();
        SessionHelpText.Text = PlayThreadRotationCopy.SessionHelpText;
        ApplyCardGridLayout();
    }

    private void OnCardsGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyCardGridLayout();

    private void ApplyCardGridLayout() =>
        PlaySettingsCardGridLayout.Apply(
            CardsGrid,
            [ThreadToolsCard, ThreadSnapshotsCard, AutomationCard, DiagnosticsCard],
            [false, false, true, true],
            ActualWidth);

    public event EventHandler? SettingsChanged;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        AdventureThreadRegistryService.EnsureMigrated(context.Bundle);
        ThreadStatusBlock.Text = AdventureThreadRegistryService.FormatConnectionSummary(context.Bundle);

        var settings = ThreadSnapshotPolicyService.Resolve(context.Bundle);
        ThreadSnapshotOnSendCheck.IsChecked = settings.CaptureOnSend;
        ThreadSnapshotOnInvalidationCheck.IsChecked = settings.CaptureOnInvalidation;
        ThreadSnapshotOnSessionLoadCheck.IsChecked = settings.CaptureOnSessionLoad;
        ThreadSnapshotOnWorkerSendCheck.IsChecked = settings.CaptureOnWorkerSend;

        SnapshotNowButton.IsEnabled = context.Host?.PromptThreadLogSnapshotAsync is not null;
        SyncLogButton.IsEnabled = context.Host?.PromptThreadLogSyncAsync is not null;
        DumpLogButton.IsEnabled = context.Host?.PromptThreadLogDumpAsync is not null;

        var s = context.Bundle.Metadata.Settings;
        AutomationCheck.IsChecked = s.AdventureAutomationEnabled;
        AutoExtractEntitiesCheck.IsChecked = s.AutoExtractEntities;
        AutoProposeMemoriesCheck.IsChecked = s.AutoProposeMemories;
        AutoUpdateSummaryCheck.IsChecked = s.AutoUpdateSummary;
        SummaryIntervalBox.Text = s.SummaryUpdateIntervalTurns.ToString();
        AutoContinuityCheckCheck.IsChecked = s.AutoContinuityCheck;
        AutoUpdateStateCheck.IsChecked = s.AutoUpdateState;
        AutoProposeEntityStateCheck.IsChecked = s.AutoProposeEntityState;
        AutoProposeCanonEvolutionCheck.IsChecked = s.AutoProposeCanonEvolution;
        AutoSyncInstructionsCheck.IsChecked = s.AutoSyncProjectInstructions;

        var hasProject = !string.IsNullOrWhiteSpace(context.Bundle.Metadata.LinkedProjectId);
        AutoExtractEntitiesCheck.IsEnabled = hasProject;
        AutoProposeMemoriesCheck.IsEnabled = hasProject;
        AutoUpdateSummaryCheck.IsEnabled = hasProject;
        AutoContinuityCheckCheck.IsEnabled = hasProject;
        AutoUpdateStateCheck.IsEnabled = hasProject;
        AutoProposeEntityStateCheck.IsEnabled = hasProject;
        AutoProposeCanonEvolutionCheck.IsEnabled = hasProject;
        AutoSyncInstructionsCheck.IsEnabled = hasProject;
        AutomationProjectHint.Text = hasProject
            ? "Post-turn proposals route to Reference → review queue by layer after each accepted turn."
            : "Link a Project to enable post-turn utility jobs.";

        RefreshUtilityParseLog();

        var draftActive = ProjectChatDraftService.IsActive(context.Bundle);
        CancelDraftButton.Visibility = draftActive ? Visibility.Visible : Visibility.Collapsed;
        DraftStatusBlock.Visibility = draftActive ? Visibility.Visible : Visibility.Collapsed;
        DraftStatusBlock.Text = draftActive ? "Project chat drafting is active." : "";
    }

    public void Flush(PlaySettingsWorkbenchContext context)
    {
        var settings = context.Bundle.Metadata.Settings;
        settings.ThreadSnapshot ??= new ThreadSnapshotSettings();
        settings.ThreadSnapshot.CaptureOnSend = ThreadSnapshotOnSendCheck.IsChecked == true;
        settings.ThreadSnapshot.CaptureOnInvalidation = ThreadSnapshotOnInvalidationCheck.IsChecked == true;
        settings.ThreadSnapshot.CaptureOnSessionLoad = ThreadSnapshotOnSessionLoadCheck.IsChecked == true;
        settings.ThreadSnapshot.CaptureOnWorkerSend = ThreadSnapshotOnWorkerSendCheck.IsChecked == true;

        settings.AdventureAutomationEnabled = AutomationCheck.IsChecked == true;
        settings.AutoExtractEntities = AutoExtractEntitiesCheck.IsChecked == true;
        settings.AutoProposeMemories = AutoProposeMemoriesCheck.IsChecked == true;
        settings.AutoUpdateSummary = AutoUpdateSummaryCheck.IsChecked == true;
        if (int.TryParse(SummaryIntervalBox.Text, out var interval))
            settings.SummaryUpdateIntervalTurns = Math.Max(1, interval);
        settings.AutoContinuityCheck = AutoContinuityCheckCheck.IsChecked == true;
        settings.AutoUpdateState = AutoUpdateStateCheck.IsChecked == true;
        settings.AutoProposeEntityState = AutoProposeEntityStateCheck.IsChecked == true;
        settings.AutoProposeCanonEvolution = AutoProposeCanonEvolutionCheck.IsChecked == true;
        settings.AutoSyncProjectInstructions = AutoSyncInstructionsCheck.IsChecked == true;
    }

    public bool AutoSyncInstructions => AutoSyncInstructionsCheck.IsChecked == true;

    private void OpenThreadsHub_Click(object sender, RoutedEventArgs e) =>
        _ctx?.Host?.OpenThreadsHub?.Invoke();

    private async void StartNarrative_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.StartNewPlayThreadAsync is { } start)
            await start(new PlayThreadStartRequest { Kind = PlayThreadStartKind.FreshStart });
        if (_ctx is not null)
            Bind(_ctx);
    }

    private void Handoff_Click(object sender, RoutedEventArgs e) =>
        _ctx?.Host?.OpenPlayHandoffDialog?.Invoke();

    private async void DraftProjectChat_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.DraftNewProjectChatAsync is { } draft)
            await draft();
        if (_ctx is not null)
            Bind(_ctx);
    }

    private void CancelDraft_Click(object sender, RoutedEventArgs e)
    {
        _ctx?.Host?.CancelProjectChatDraft?.Invoke();
        if (_ctx is not null)
            Bind(_ctx);
    }

    private void PinPlayTab_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host is PlaySettingsWorkbenchPage page)
            page.RequestPinPlayTab();
    }

    private void OpenPinnedTab_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host is PlaySettingsWorkbenchPage page)
            page.RequestOpenPinnedPlayTab();
    }

    private void ClearPin_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host is PlaySettingsWorkbenchPage page)
            page.RequestClearPlayTabPin();
    }

    private async void ListThreadFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.ListThreadFilesAsync is not { } list || _ctx.Host.DownloadThreadFileAsync is not { } download)
            return;

        var files = await list();
        await WinUiDialogHelper.ShowInfoAsync(
            App.CurrentMainWindow,
            "Thread files",
            files.Count == 0
                ? "No conversation files found."
                : string.Join(Environment.NewLine, files.Select(f => f.Name)));
    }

    private void SnapshotNow_Click(object sender, RoutedEventArgs e) =>
        _ = _ctx?.Host?.PromptThreadLogSnapshotAsync?.Invoke() ?? Task.CompletedTask;

    private void SyncLog_Click(object sender, RoutedEventArgs e) =>
        _ = _ctx?.Host?.PromptThreadLogSyncAsync?.Invoke() ?? Task.CompletedTask;

    private void DumpLog_Click(object sender, RoutedEventArgs e) =>
        _ = _ctx?.Host?.PromptThreadLogDumpAsync?.Invoke() ?? Task.CompletedTask;

    private async void PushInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.PushInstructionsNowAsync is { } push)
            await push();
    }

    private void GoToUtilityJobs_Click(object sender, RoutedEventArgs e) =>
        _ctx?.NavigateToTab?.Invoke(ChatGPTWrapper.Views.PlaySettingsTab.UtilityJobs);

    private void RefreshUtilityParseLog()
    {
        if (_ctx is null || UtilityParseLogBox is null)
            return;

        UtilityParseLogBox.Text = UtilityParseLogService.ReadRecentTail(_ctx.Bundle.Metadata.Id);
    }

    private void RefreshUtilityParseLog_Click(object sender, RoutedEventArgs e) =>
        RefreshUtilityParseLog();

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.NotifySettingsChanged();
    }
}

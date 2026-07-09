using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.ChatGptApi.ProjectSource.Publication;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.WinUiBridge;
using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class SourceSyncPage : UserControl
{
    private readonly Guid _adventureId;
    private readonly ObservableCollection<SourceSyncRowViewModel> _rows = [];

    private AdventureBundle? _bundle;
    private SourceSyncPlan? _plan;
    private bool _suppressUploadMethodSave;
    private bool _applyInFlight;
    private bool _directPublishInFlight;
    private CancellationTokenSource? _directPublishCts;

    public SourceSyncPage(Guid adventureId)
    {
        _adventureId = adventureId;
        InitializeComponent();
        FilesList.ItemsSource = _rows;
        CapabilitiesHint.Text =
            $"Advanced API sync — manual publish (copy/drag) is recommended. See {ChatGptApiDiscovery.CapabilitiesPath}";
        BindUploadMethodSelector();
        Loaded += async (_, _) => await RefreshPlanAsync();
    }

    public bool SyncCompleted { get; private set; }

    private SourceSyncRowViewModel? SelectedRow => FilesList.SelectedItem as SourceSyncRowViewModel;

    private ProjectSourceUploadMethod SelectedUploadMethod =>
        UploadMethodCombo.SelectedItem is ComboBoxItem { Tag: ProjectSourceUploadMethod method }
            ? method
            : ProjectSourceUploadMethod.HeadlessBrowser;

    private IProgress<string> StatusProgress => new Progress<string>(s => StatusLine.Text = s);

    private async Task RefreshPlanAsync()
    {
        StatusLine.Text = "Building sync plan…";
        _rows.Clear();

        try
        {
            var snapshot = await WinUiProjectHostOperations.BuildSourceSyncPlanAsync(_adventureId, StatusProgress);
            if (snapshot is null)
            {
                StatusLine.Text = "ChatGPT WebView is not ready.";
                return;
            }

            _bundle = snapshot.Bundle;
            _plan = snapshot.Plan;
            BindUploadMethodSelector(_bundle);

            foreach (var item in _plan.Items)
                AddRow(item);

            UpdateRemoteBanner(_plan);
            UpdatePlanSummary();

            ApplySafeButton.IsEnabled = !_plan.SyncBlocked;
            ApplyAllButton.IsEnabled = !_plan.SyncBlocked;
            if (_plan.SyncBlocked)
                StatusLine.Text = $"Sync blocked: {_plan.SyncBlockReason}";
        }
        catch (Exception ex)
        {
            DiagnosticsMirror.LogException("source_sync_plan_failed", ex);
            StatusLine.Text = "Failed to build plan.";
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Sync plan", ex.Message);
        }
    }

    private void UpdatePlanSummary()
    {
        if (_plan is null)
            return;

        var autoSafe = _rows.Count(r => ProjectFileSyncPlanner.IsAutoSafe(r.PlanItem));
        var unresolved = _rows.Count(r =>
            ProjectFileSyncPlanner.ResolveAction(r.PlanItem) == SourceSyncAction.NeedsResolution);
        StatusLine.Text = unresolved > 0
            ? $"{autoSafe} auto-safe, {unresolved} conflict(s) need choice, {_rows.Count} file(s)."
            : $"{autoSafe} auto-safe, {_plan.ConflictCount} conflict(s), {_rows.Count} file(s).";
    }

    private void AddRow(SourceSyncPlanItem item)
    {
        var row = new SourceSyncRowViewModel(item);
        row.ActionChanged += UpdatePlanSummary;
        _rows.Add(row);
    }

    private void UpdateRemoteBanner(SourceSyncPlan plan)
    {
        var text = SourceSyncUiHelper.FormatRemoteBanner(plan);
        if (string.IsNullOrWhiteSpace(text))
        {
            RemoteBanner.Visibility = Visibility.Collapsed;
            RemoteBanner.Text = "";
        }
        else
        {
            RemoteBanner.Text = text;
            RemoteBanner.Visibility = Visibility.Visible;
        }

        ReconcileDuplicatesButton.IsEnabled = SourceSyncUiHelper.CountOrphanDuplicates(plan) > 0;
    }

    private async void ClearBindings_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        if (!await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                "Clear remote bindings",
                "Clear all stored remote file bindings for this adventure?\n\n"
                + "Local sources/ files are unchanged. Use Refresh plan, then Apply all to re-upload.",
                confirmText: "Clear"))
        {
            return;
        }

        SourceSyncUiHelper.ClearRemoteBindings(_bundle);
        await RefreshPlanAsync();
    }

    private async void ReconcileDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _plan is null)
            return;

        var reconciled = await WinUiProjectHostOperations.ReconcileSourceDuplicatesAsync(
            _adventureId,
            _plan,
            StatusProgress,
            async (title, message, confirm) =>
                await WinUiDialogHelper.ConfirmAsync(App.CurrentMainWindow, title, message, confirmText: confirm));

        if (reconciled)
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Reconcile duplicates",
                "Duplicate remote files removed.");
            await RefreshPlanAsync();
        }
    }

    private async void DeleteRemote_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is null)
            return;

        var entry = SelectedRow.PlanItem.Entry;
        if (entry.SyncState != SourceSyncState.RemoteOnly
            || string.IsNullOrWhiteSpace(entry.RemoteFileId))
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Delete remote file",
                "Select a RemoteOnly row with a remote file id.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId))
            return;

        if (!await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                "Delete remote file",
                $"Remove {entry.RelativePath} from the linked ChatGPT project?\n{entry.RemoteFileId}",
                confirmText: "Delete"))
        {
            return;
        }

        try
        {
            var deleted = await WinUiProjectHostOperations.DeleteRemoteSourceFileAsync(
                _adventureId,
                entry.RelativePath,
                entry.RemoteFileId,
                StatusProgress);
            if (deleted)
                StatusLine.Text = $"Deleted remote file {entry.RelativePath}.";
            await RefreshPlanAsync();
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"Delete failed: {ex.Message}";
            ProjectLinkDiagnostics.Log($"Delete remote file failed {entry.RelativePath}: {ex}");
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Delete remote file", ex.Message);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshPlanAsync();

    private void KeepLocal_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not null)
            SelectedRow.SelectedAction = SourceSyncAction.PushReplace;
    }

    private void KeepRemote_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not null)
            SelectedRow.SelectedAction = SourceSyncAction.Pull;
    }

    private async void ApplySafe_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(autoSafeOnly: true);

    private async void ApplyAll_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(autoSafeOnly: false);

    private async Task ApplyAsync(bool autoSafeOnly)
    {
        if (_bundle is null || _plan is null || _applyInFlight)
            return;

        _applyInFlight = true;
        ApplySafeButton.IsEnabled = false;
        ApplyAllButton.IsEnabled = false;

        try
        {
            var result = await WinUiProjectHostOperations.ApplySourceSyncPlanAsync(
                _adventureId,
                _plan,
                autoSafeOnly,
                StatusProgress);
            if (result is null)
                return;

            SyncCompleted = result.Success;
            foreach (var row in _rows)
                row.RefreshLabels();

            var rebuilt = await WinUiProjectHostOperations.RebuildSourceSyncPlanAsync(
                _adventureId,
                result.Plan?.DetectedRemoteFiles);
            if (rebuilt is not null)
            {
                _bundle = rebuilt.Bundle;
                _plan = rebuilt.Plan;
                _rows.Clear();
                foreach (var item in _plan.Items)
                    AddRow(item);
                UpdateRemoteBanner(_plan);
                UpdatePlanSummary();
            }

            StatusLine.Text = result.Success
                ? $"Sync complete. Pulled {result.Pulled}, replaced {result.Replaced}."
                : $"Sync incomplete: {result.Error} (pulled {result.Pulled}, replaced {result.Replaced}, conflicts {result.Conflicts}).";

            if (!result.Success && !string.IsNullOrEmpty(result.Error))
            {
                var errorText = result.Error;
                if (!string.IsNullOrWhiteSpace(result.RunSummaryPath) && File.Exists(result.RunSummaryPath))
                    errorText += $"{Environment.NewLine}{Environment.NewLine}Open run summary: {result.RunSummaryPath}";

                await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Sync", errorText);
            }
            else if (result.Warnings.Count > 0)
            {
                await WinUiDialogHelper.ShowInfoAsync(
                    App.CurrentMainWindow,
                    "Sync warnings",
                    string.Join(Environment.NewLine, result.Warnings));
            }

            if (!string.IsNullOrEmpty(result.DuplicateProjectWarning))
            {
                await WinUiDialogHelper.ShowInfoAsync(
                    App.CurrentMainWindow,
                    "Possible duplicate project",
                    result.DuplicateProjectWarning);
            }
        }
        catch (Exception ex)
        {
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Sync failed", ex.Message);
        }
        finally
        {
            _applyInFlight = false;
            var blocked = _plan?.SyncBlocked == true;
            ApplySafeButton.IsEnabled = !blocked;
            ApplyAllButton.IsEnabled = !blocked;
        }
    }

    private async void PublishSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SelectedRow is null || _directPublishInFlight)
            return;

        if (string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId))
        {
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Publish file", "Link a ChatGPT project first.");
            return;
        }

        var entry = SelectedRow.PlanItem.Entry;
        var localPath = Path.Combine(ProjectSourceExportService.SourcesDirectory(_bundle), entry.RelativePath);
        if (!File.Exists(localPath))
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Publish file",
                "Local file not found. Export sources or pull from project first.");
            return;
        }

        await RunDirectPublishAsync(entry.RelativePath, localPath, entry);
    }

    private async void UploadTestFile_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _directPublishInFlight || App.CurrentMainWindow is not { } owner)
            return;

        if (string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId))
        {
            await WinUiDialogHelper.ShowInfoAsync(owner, "Upload test file", "Link a ChatGPT project first.");
            return;
        }

        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add("*");
        WinUiDialogHelper.InitializeWithOwner(picker, owner);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return;

        var (promptOk, remoteName) = await WinUiDialogHostService.PromptAsync(
            owner,
            "Remote file name",
            "Publish to the linked ChatGPT project as:",
            file.Name,
            confirmButtonText: "Publish");
        if (!promptOk)
            return;

        await RunDirectPublishAsync(remoteName, file.Path, manifestEntry: null);
    }

    private void CancelPublish_Click(object sender, RoutedEventArgs e)
    {
        if (!_directPublishInFlight)
            return;

        StatusLine.Text = "Cancelling publish…";
        _directPublishCts?.Cancel();
    }

    private async Task RunDirectPublishAsync(
        string remoteFileName,
        string localFilePath,
        SourceManifestEntry? manifestEntry)
    {
        if (_bundle is null || _directPublishInFlight)
            return;

        _directPublishInFlight = true;
        _directPublishCts = new CancellationTokenSource();
        PublishSelectedButton.IsEnabled = false;
        UploadTestFileButton.IsEnabled = false;
        CancelPublishButton.IsEnabled = true;

        try
        {
            var uploadMethod = ProjectSourceUploadMethodResolver.Resolve(_bundle, SelectedUploadMethod);
            var result = await WinUiProjectHostOperations.PublishSourceFileAsync(
                _adventureId,
                remoteFileName,
                localFilePath,
                manifestEntry,
                uploadMethod,
                StatusProgress,
                _directPublishCts.Token);

            if (result is null)
            {
                await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Publish file", "ChatGPT WebView is not ready.");
                return;
            }

            StatusLine.Text =
                $"Published {remoteFileName} → file_id={result.File.FileId}"
                + (result.UsedAttachFallback ? " (attach fallback)" : "")
                + $" [{result.Outcome}]";
            RenderPublicationAttempts(result.Run);

            var testUploadNote = manifestEntry is null
                ? $"{Environment.NewLine}{Environment.NewLine}Test uploads appear as RemoteOnly until you add a matching file under sources/ or pull the remote into the adventure."
                : "";

            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Publish complete",
                $"Published {remoteFileName}{Environment.NewLine}"
                + $"file_id: {result.File.FileId}{Environment.NewLine}"
                + $"attach fallback: {result.UsedAttachFallback}{Environment.NewLine}"
                + $"manifest updated: {result.UpdatedManifest}"
                + testUploadNote);

            await RefreshPlanAsync();
        }
        catch (OperationCanceledException)
        {
            StatusLine.Text = "Publish cancelled.";
        }
        catch (ProjectPublicationExhaustedException ex)
        {
            StatusLine.Text = $"Publication exhausted: {ex.Message}";
            RenderPublicationAttempts(ex.Run);
            ProjectLinkDiagnostics.Log($"Direct source publish exhausted {remoteFileName}: {ex}");
            var triage = ProjectPublicationTriage.BuildExhaustedSummary(ex.Run, remoteFileName);
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Publication exhausted",
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}{triage}");
        }
        catch (Exception ex)
        {
            StatusLine.Text = $"Publish failed: {ex.Message}";
            PublicationAttemptList.Visibility = Visibility.Collapsed;
            ProjectLinkDiagnostics.Log($"Direct source publish failed {remoteFileName}: {ex}");
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Publish failed", ex.Message);
        }
        finally
        {
            _directPublishInFlight = false;
            _directPublishCts?.Dispose();
            _directPublishCts = null;
            PublishSelectedButton.IsEnabled = true;
            UploadTestFileButton.IsEnabled = true;
            CancelPublishButton.IsEnabled = false;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        AppDirectories.EnsureCreated();
        Process.Start(new ProcessStartInfo { FileName = AppDirectories.Root, UseShellExecute = true });
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null || SelectedRow is null)
            return;

        var path = Path.Combine(
            ProjectSourceExportService.SourcesDirectory(bundle),
            SelectedRow.PlanItem.Entry.RelativePath);

        if (!File.Exists(path))
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                "Preview",
                "Local file not found. Pull from project or export sources first.");
            return;
        }

        var text = await File.ReadAllTextAsync(path);
        var dialog = new ContentDialog
        {
            Title = $"{SelectedRow.FileName} | {SelectedRow.StateLabel}",
            Content = new ScrollViewer
            {
                MaxHeight = 480,
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                    IsTextSelectionEnabled = true,
                },
            },
            CloseButtonText = "Close",
        };
        await WinUiDialogHelper.ShowAsync(dialog, App.CurrentMainWindow);
    }

    private void RenderPublicationAttempts(ProjectFilePublicationRun? run)
    {
        if (run is null || run.Attempts.Count == 0)
        {
            PublicationAttemptList.Visibility = Visibility.Collapsed;
            PublicationAttemptList.ItemsSource = null;
            return;
        }

        PublicationAttemptList.ItemsSource = run.Attempts.Select(a =>
            $"{a.Lane} · {a.Phase} · {a.Outcome} · {a.LatencyMs}ms"
            + (string.IsNullOrWhiteSpace(a.Error) ? "" : $" · {a.Error}"));
        PublicationAttemptList.Visibility = Visibility.Visible;
    }

    private void BindUploadMethodSelector(AdventureBundle? bundle = null)
    {
        bundle ??= _bundle;
        if (UploadMethodCombo.Items.Count == 0)
        {
            UploadMethodCombo.Items.Add(new ComboBoxItem
            {
                Content = "Headless Chrome (Playwright)",
                Tag = ProjectSourceUploadMethod.HeadlessBrowser,
            });
            UploadMethodCombo.Items.Add(new ComboBoxItem
            {
                Content = "Pure API (backend-api)",
                Tag = ProjectSourceUploadMethod.PureApi,
            });
        }

        if (bundle is null)
            return;

        var method = ProjectSourceUploadMethodResolver.Resolve(bundle);
        _suppressUploadMethodSave = true;
        try
        {
            foreach (ComboBoxItem item in UploadMethodCombo.Items)
            {
                if (item.Tag is ProjectSourceUploadMethod tagged && tagged == method)
                {
                    UploadMethodCombo.SelectedItem = item;
                    break;
                }
            }
        }
        finally
        {
            _suppressUploadMethodSave = false;
        }
    }

    private void UploadMethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressUploadMethodSave || _bundle is null)
            return;

        if (UploadMethodCombo.SelectedItem is not ComboBoxItem { Tag: ProjectSourceUploadMethod method })
            return;

        ProjectSourceUploadMethodResolver.PersistSelection(_bundle, method);
    }
}

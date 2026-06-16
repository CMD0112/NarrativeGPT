using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChatGPTWrapper.Views;

public partial class SourceSyncDialog : Window
{
    private readonly Guid _adventureId;
    private readonly IChatGptProjectHost? _host;
    private readonly Func<WebView2?>? _getWebView;
    private readonly Func<ProjectSourceSyncService?>? _getSyncService;

    private AdventureBundle? _bundle;
    private SourceSyncPlan? _plan;
    private readonly ObservableCollection<SourceSyncRowViewModel> _rows = [];

    public bool SyncCompleted { get; private set; }

    public SourceSyncDialog(Guid adventureId, IChatGptProjectHost host)
    {
        _adventureId = adventureId;
        _host = host;
        InitializeComponent();
        SetupGridColumns();
        CapabilitiesHint.Text = $"Advanced API sync — manual publish (copy/drag) is recommended. See {ChatGptApiDiscovery.CapabilitiesPath}";
        Loaded += async (_, _) => await RefreshPlanAsync();
    }

    public SourceSyncDialog(
        Guid adventureId,
        Func<WebView2?> getWebView,
        Func<ProjectSourceSyncService?> getSyncService)
    {
        _adventureId = adventureId;
        _getWebView = getWebView;
        _getSyncService = getSyncService;
        InitializeComponent();
        SetupGridColumns();
        CapabilitiesHint.Text = $"API diagnostics: {ChatGptApiDiscovery.CapabilitiesPath}";
        Loaded += async (_, _) => await RefreshPlanAsync();
    }

    private void SetupGridColumns()
    {
        SourceSyncGridHelper.AddFileColumn(FilesGrid);
        SourceSyncGridHelper.AddStateColumn(FilesGrid);
        SourceSyncGridHelper.AddLocalHashColumn(FilesGrid);
        SourceSyncGridHelper.AddRemoteHashColumn(FilesGrid);
        SourceSyncGridHelper.AddActionColumn(FilesGrid);
        FilesGrid.ItemsSource = _rows;
    }

    private SourceSyncRowViewModel? SelectedRow => FilesGrid.SelectedItem as SourceSyncRowViewModel;

    private async Task RefreshPlanAsync()
    {
        StatusLine.Text = "Building sync plan…";
        _rows.Clear();

        var bundle = AdventureStore.Load(_adventureId);
        CoreWebView2? core = null;
        ProjectSourceSyncService? sync = null;

        if (_host is not null)
        {
            await _host.EnsureReadyAsync(_adventureId);
            core = _host.ApiCore;
            sync = _host.Sync;
        }
        else
        {
            sync = _getSyncService?.Invoke();
            var wv = _getWebView?.Invoke();
            if (wv?.CoreWebView2 is null && wv is not null)
                await wv.EnsureCoreWebView2Async();
            core = wv?.CoreWebView2;
        }

        if (bundle is null || sync is null || core is null)
        {
            StatusLine.Text = "ChatGPT WebView is not ready.";
            return;
        }

        _bundle = bundle;
        try
        {
            var progress = new Progress<string>(s => StatusLine.Text = s);
            _plan = _host is not null
                ? await _host.FileSync.BuildPlanAsync(core, bundle, progress)
                : await sync.BuildPlanAsync(core, bundle, progress);
            AdventureStore.Save(bundle);

            foreach (var item in _plan.Items)
                AddRow(item);

            UpdateRemoteBanner(_plan);
            UpdatePlanSummary();

            ApplySafeButton.IsEnabled = !_plan.SyncBlocked;
            ApplyAllButton.IsEnabled = !_plan.SyncBlocked;
            if (_plan.SyncBlocked)
            {
                StatusLine.Text = $"Sync blocked: {_plan.SyncBlockReason}";
            }
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Failed to build plan.";
            ProjectLinkDiagnostics.Log($"Sync plan build failed: {ex}");
            MessageBox.Show(this, ex.Message, "Sync plan", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        if (!SourceSyncUiHelper.ConfirmClearRemoteBindings(this))
            return;

        SourceSyncUiHelper.ClearRemoteBindings(_bundle);
        await RefreshPlanAsync();
    }

    private async void ReconcileDuplicates_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _plan is null)
            return;

        CoreWebView2? core = _host?.ApiCore ?? _getWebView?.Invoke()?.CoreWebView2;
        ProjectSourceSyncService? sync = _host?.Sync ?? _getSyncService?.Invoke();
        if (core is null || sync is null)
            return;

        var reconciled = await SourceSyncUiHelper.ConfirmAndReconcileDuplicatesAsync(
            this,
            core,
            _bundle,
            _plan,
            sync,
            _host?.FileSync,
            new Progress<string>(s => StatusLine.Text = s));

        if (reconciled)
            await RefreshPlanAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshPlanAsync();

    private void KeepLocal_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
            return;
        SelectedRow.SelectedAction = SourceSyncAction.PushReplace;
    }

    private void KeepRemote_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
            return;
        SelectedRow.SelectedAction = SourceSyncAction.Pull;
    }

    private async void ApplySafe_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(autoSafeOnly: true);

    private async void ApplyAll_Click(object sender, RoutedEventArgs e) =>
        await ApplyAsync(autoSafeOnly: false);

    private bool _applyInFlight;

    private async Task ApplyAsync(bool autoSafeOnly)
    {
        if (_bundle is null || _plan is null || _applyInFlight)
            return;

        _applyInFlight = true;
        CoreWebView2? core = null;
        if (_host is not null)
            core = _host.ApiCore;
        else if (_getWebView?.Invoke()?.CoreWebView2 is { } c)
            core = c;

        if (core is null)
            return;

        ApplySafeButton.IsEnabled = false;
        ApplyAllButton.IsEnabled = false;
        try
        {
            var result = _host is not null
                ? await _host.FileSync.ApplyAndVerifyAsync(
                    core,
                    _bundle,
                    _plan,
                    autoSafeOnly,
                    new Progress<string>(s => StatusLine.Text = s))
                : await (_getSyncService?.Invoke() ?? throw new InvalidOperationException("Sync service missing"))
                    .ApplyPlanAsync(core, _bundle, _plan, autoSafeOnly);
            SyncCompleted = result.Success;
            foreach (var row in _rows)
                row.RefreshLabels();

            _bundle = AdventureStore.Load(_adventureId);
            if (_bundle is not null)
            {
                var cachedRemote = result.Plan?.DetectedRemoteFiles;
                _plan = _host is not null
                    ? await _host.FileSync.BuildPlanAsync(
                        core,
                        _bundle,
                        ensureProjectPage: false,
                        cachedRemoteFiles: cachedRemote)
                    : await (_getSyncService?.Invoke()
                              ?? throw new InvalidOperationException("Sync service missing"))
                        .BuildPlanAsync(
                            core,
                            _bundle,
                            ensureProjectPage: false,
                            cachedRemoteFiles: cachedRemote);
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
                if (!string.IsNullOrWhiteSpace(result.RunSummaryPath)
                    && File.Exists(result.RunSummaryPath))
                {
                    errorText += $"{Environment.NewLine}{Environment.NewLine}Open run summary: {result.RunSummaryPath}";
                }

                MessageBox.Show(this, errorText, "Sync", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                SourceSyncUiHelper.ShowApplyWarnings(this, result);
            }

            if (!string.IsNullOrEmpty(result.DuplicateProjectWarning))
            {
                MessageBox.Show(
                    this,
                    result.DuplicateProjectWarning,
                    "Possible duplicate project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Sync failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _applyInFlight = false;
            var blocked = _plan?.SyncBlocked == true;
            ApplySafeButton.IsEnabled = !blocked;
            ApplyAllButton.IsEnabled = !blocked;
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true,
        });
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        AppDirectories.EnsureCreated();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppDirectories.Root,
            UseShellExecute = true,
        });
    }

    private void Preview_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null || SelectedRow is null)
            return;

        var path = Path.Combine(
            ProjectSourceExportService.SourcesDirectory(bundle),
            SelectedRow.PlanItem.Entry.RelativePath);

        if (!File.Exists(path))
        {
            MessageBox.Show(this, "Local file not found. Pull from project or export sources first.", "Preview");
            return;
        }

        var text = File.ReadAllText(path);
        var dlg = new ContextViewerDialog(
            text,
            $"{SelectedRow.FileName} | {SelectedRow.StateLabel} | local {SelectedRow.LocalHashShort}");
        dlg.Owner = this;
        dlg.ShowDialog();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

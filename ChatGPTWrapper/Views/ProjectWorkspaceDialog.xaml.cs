using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Views;

public partial class ProjectWorkspaceDialog : Window
{
    private readonly Guid _adventureId;
    private readonly IChatGptProjectHost _host;

    private List<GizmoSummary> _projects = [];
    private bool _sessionPrepared;
    private bool _linked;
    private bool _linkInFlight;
    private SourceSyncPlan? _syncPlan;
    private AdventureBundle? _bundle;
    private readonly ObservableCollection<SourceSyncRowViewModel> _syncRows = [];

    public bool LinkedSuccessfully => _linked;

    public bool LinkStateChanged { get; private set; }

    public bool SyncCompleted { get; private set; }

    public ProjectWorkspaceDialog(Guid adventureId, IChatGptProjectHost host)
    {
        _adventureId = adventureId;
        _host = host;
        InitializeComponent();
        SetupSyncGridColumns();
        ProjectModeTabs.SelectionChanged += ProjectModeTabs_SelectionChanged;
        NewProjectNameBox.TextChanged += (_, _) => UpdateLinkButtonState();
        ManualUrlBox.TextChanged += (_, _) => UpdateLinkButtonState();
        Loaded += OnLoaded;
    }

    private void SetupSyncGridColumns()
    {
        SourceSyncGridHelper.AddFileColumn(SyncFilesGrid);
        SourceSyncGridHelper.AddStateColumn(SyncFilesGrid);
        SourceSyncGridHelper.AddActionColumn(SyncFilesGrid);
        SyncFilesGrid.ItemsSource = _syncRows;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        WorkspaceTabs.SelectedItem = ProjectsTab;
        PrefillCreateProjectName();
        ApplyDesigningProjectUiState();
        await PrepareSessionAsync(showPane: false);
        UpdateChecklist(_host.LastSessionStatus);
        ApplyLinkedProjectUiState();
        UpdateSessionHint();
        await RefreshProjectsAsync();
    }

    private void PrefillCreateProjectName()
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null || !string.IsNullOrWhiteSpace(NewProjectNameBox.Text))
            return;

        NewProjectNameBox.Text = bundle.Metadata.Title;
    }

    private void ApplyDesigningProjectUiState()
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle?.Metadata.Status != AdventureStatus.Designing)
            return;

        CreateThreadCheck.IsChecked = false;
        CreateThreadCheck.Content = "Create play thread (optional — not used while designing)";
    }

    private void UpdateSessionHint()
    {
        var status = _host.LastSessionStatus;
        SessionHintLine.Text = status is { IsAuthenticated: true, HasDeviceId: true }
            ? "Signed in — pick a project below, then Link."
            : "Not fully signed in — use the Connection tab if the project list is empty.";
    }

    private void ApplyLinkedProjectUiState()
    {
        var bundle = AdventureStore.Load(_adventureId);
        AdventureNavigationService.SyncLinkedFields(bundle!);
        var linkedId = bundle is null
            ? null
            : AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(linkedId))
        {
            LinkedProjectBannerPanel.Visibility = Visibility.Collapsed;
            Title = "Link ChatGPT Project";
            IntroLine.Text =
                "Pick or create a ChatGPT Project below, then Link. After linking, use the Sources tab to export and publish lore files.";
            return;
        }

        _linked = true;
        var linkedTitle = _projects.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, linkedId))?.Title;
        var linkedLabel = string.IsNullOrWhiteSpace(linkedTitle) ? linkedId : $"{linkedTitle} ({linkedId})";
        LinkedProjectBanner.Text =
            $"Currently linked to {linkedLabel}. Select another project below and click Switch, "
            + "paste a different URL, or Unlink to detach.";
        LinkedProjectBannerPanel.Visibility = Visibility.Visible;
        Title = "Change ChatGPT Project";
        IntroLine.Text =
            "To use a different Project, pick one from the list or paste its URL, then Switch. "
            + "Local lore files stay on this adventure; remote publish bindings reset for the new Project.";

        var match = _projects.FirstOrDefault(p => ChatGptUrls.GizmoIdsEqual(p.Id, linkedId));
        if (match is not null)
            ProjectList.SelectedItem = match;
        else
            SelectedProjectLine.Text = $"Linked to {linkedId}. Refresh the list to pick a different project.";

        UpdateLinkButtonState();
        UpdateLinkHealth();
    }

    private void UpdateLinkHealth()
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
        {
            LinkHealthLine.Text = "";
            return;
        }

        LinkHealthLine.Text = SourceSyncUiHelper.FormatLinkHealth(bundle, _syncPlan);
    }

    private async void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab)
            return;

        if (tab == ProjectsTab && _projects.Count == 0)
            await RefreshProjectsAsync();

        if (tab.Header?.ToString() == "Sources" && _linked)
            await RefreshSourcesTabAsync();

        UpdateLinkButtonState();
    }

    private void ProjectModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateLinkButtonState();

    private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateProjectSelectionLine();

    private void UpdateProjectSelectionLine()
    {
        if (ProjectList.SelectedItem is GizmoSummary picked)
        {
            SelectedProjectLine.Text = $"Selected: {picked.Title} ({picked.Id})";
            ErrorLine.Text = "";
        }
        else
        {
            SelectedProjectLine.Text = _projects.Count == 0
                ? "No projects yet — click Refresh or use Advanced: URL."
                : "Projects load automatically. Select one, then Link below (or double-click a row).";
        }

        UpdateLinkButtonState();
    }

    private async void ProjectList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ProjectModeTabs.SelectedIndex != 0 || ProjectList.SelectedItem is null)
            return;

        await LinkSelectedAsync();
    }

    private void UpdateLinkButtonState()
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab || tab != ProjectsTab)
        {
            LinkButton.Content = "Link project";
            LinkButton.IsEnabled = false;
            return;
        }

        var bundle = AdventureStore.Load(_adventureId);
        var linkedId = bundle?.Metadata.LinkedProjectId;
        var isLinked = !string.IsNullOrWhiteSpace(linkedId);
        var switchVerb = isLinked ? "Switch" : "Link";

        switch (ProjectModeTabs.SelectedIndex)
        {
            case 1:
            {
                var name = string.IsNullOrWhiteSpace(NewProjectNameBox.Text)
                    ? bundle?.Metadata.Title ?? "project"
                    : NewProjectNameBox.Text.Trim();
                LinkButton.IsEnabled = !string.IsNullOrWhiteSpace(name);
                LinkButton.Content = isLinked
                    ? $"Create and switch to: {name}"
                    : $"Create and link: {name}";
                return;
            }
            case 2:
            {
                var canLink = !string.IsNullOrWhiteSpace(ManualUrlBox.Text);
                LinkButton.IsEnabled = canLink;
                if (!canLink)
                {
                    LinkButton.Content = $"{switchVerb} from URL (paste id)";
                    return;
                }

                if (isLinked
                    && ChatGptUrls.TryParseGizmoIdFromUserInput(ManualUrlBox.Text, out var urlGizmoId)
                    && ChatGptUrls.GizmoIdsEqual(urlGizmoId, linkedId))
                {
                    LinkButton.IsEnabled = false;
                    LinkButton.Content = "Already linked to this project";
                    return;
                }

                LinkButton.Content = $"{switchVerb} from URL";
                return;
            }
            default:
            {
                if (ProjectList.SelectedItem is not GizmoSummary picked)
                {
                    LinkButton.IsEnabled = false;
                    LinkButton.Content = isLinked
                        ? "Switch project (select from list)"
                        : "Link project (select from list)";
                    return;
                }

                if (isLinked && ChatGptUrls.GizmoIdsEqual(picked.Id, linkedId))
                {
                    LinkButton.IsEnabled = false;
                    LinkButton.Content = $"Already linked: {picked.Title}";
                    return;
                }

                LinkButton.IsEnabled = true;
                LinkButton.Content = isLinked
                    ? $"Switch to: {picked.Title}"
                    : $"Link project: {picked.Title}";
                return;
            }
        }
    }

    private bool ConfirmProjectSwitch(string? currentProjectId, string targetLabel)
    {
        if (string.IsNullOrWhiteSpace(currentProjectId))
            return true;

        return MessageBox.Show(
                this,
                $"Switch this adventure from the current Project to {targetLabel}?\n\n"
                + "Remote source bindings and play-tab pins for the old Project will be cleared. "
                + "Local lore files in this adventure are kept.",
                "Switch ChatGPT Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            == MessageBoxResult.Yes;
    }

    private void UnlinkProject_Click(object sender, RoutedEventArgs e)
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return;

        if (MessageBox.Show(
                this,
                "Unlink this adventure from its ChatGPT Project?\n\n"
                + "Local adventure data and lore files are kept. You can link again later.",
                "Unlink Project",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question)
            != MessageBoxResult.Yes)
        {
            return;
        }

        AdventureProjectBindingService.ClearProjectLink(bundle);
        LinkStateChanged = true;
        _linked = false;
        _bundle = bundle;
        DoneButton.Content = "Done";
        LinkButton.IsDefault = true;
        DoneButton.IsDefault = false;
        StatusLine.Text = "Project unlinked.";
        ErrorLine.Text = "";
        ApplyLinkedProjectUiState();
        UpdateProjectSelectionLine();
    }

    private async Task PrepareSessionAsync(bool showPane, bool force = false)
    {
        if (_sessionPrepared && !force)
            return;

        StatusLine.Text = "Preparing ChatGPT session…";
        ErrorLine.Text = "";
        SetBusy(true);
        try
        {
            await _host.EnsureReadyAsync(_adventureId, showBrowserPane: showPane);
            _sessionPrepared = true;
            StatusLine.Text = "Session ready.";
            UpdateChecklist(_host.LastSessionStatus);
        }
        catch (ChatGptApiException ex)
        {
            StatusLine.Text = "Session not ready.";
            ErrorLine.Text = ex.Message;
            UpdateChecklist(_host.LastSessionStatus);
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Session failed.";
            ErrorLine.Text = ex.Message;
            UpdateChecklist(_host.LastSessionStatus);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateChecklist(ProjectSessionStatus? status)
    {
        if (status is null)
        {
            CheckSignedIn.Text = "○ Signed in: unknown";
            CheckDeviceId.Text = "○ Device cookie: unknown";
            CheckAccountId.Text = "○ Account id: unknown";
            return;
        }

        CheckSignedIn.Text = status.IsAuthenticated ? "✓ Signed in" : "○ Not signed in";
        CheckDeviceId.Text = status.HasDeviceId ? "✓ Device cookie present" : "○ Device cookie missing";
        CheckAccountId.Text = status.HasAccountId
            ? "✓ Account id present"
            : "○ Account id not detected (optional — not required to connect)";
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        ErrorLine.Text = "";
        SetBusy(true);
        try
        {
            _sessionPrepared = false;
            await PrepareSessionAsync(showPane: true, force: true);
            UpdateChecklist(_host.LastSessionStatus);

            var probe = await _host.ProbeSidebarAsync();
            ProbeLine.Text =
                $"Probe: status={probe.Status} items={probe.ItemCount} keys=[{string.Join(", ", probe.JsonKeys)}] device={probe.HasDeviceId}";
            StatusLine.Text = probe.Ok ? "Connection OK." : "Connection probe failed.";
            UpdateSessionHint();
            if (!probe.Ok && !string.IsNullOrEmpty(probe.Error))
                ErrorLine.Text = probe.Error;
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
            StatusLine.Text = "Test failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CaptureHeaders_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "Browse Projects in the ChatGPT tab (sidebar), open a project, then click Test connection. Headers are saved to api-client-profile.json automatically.",
            "Capture headers",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private async void RefreshProjects_Click(object sender, RoutedEventArgs e) =>
        await RefreshProjectsAsync();

    private async Task RefreshProjectsAsync()
    {
        ErrorLine.Text = "";
        SetBusy(true);
        try
        {
            await PrepareSessionAsync(showPane: false);
            var result = await _host.DiscoverProjectsAsync();
            _projects = result.Projects.ToList();
            ProjectList.ItemsSource = _projects;
            ProjectModeTabs.SelectedIndex = 0;
            DiscoveryLine.Text = result.StrategiesUsed.Count > 0
                ? $"via {string.Join(", ", result.StrategiesUsed)} ({_projects.Count})"
                : $"{_projects.Count} project(s)";
            StatusLine.Text = _projects.Count == 0
                ? "No projects found. Use Advanced URL tab or sign in on ChatGPT tab."
                : $"{_projects.Count} project(s) loaded.";
            UpdateProjectSelectionLine();
            ApplyLinkedProjectUiState();
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
            StatusLine.Text = "Refresh failed.";
        }
        finally
        {
            SetBusy(false);
            UpdateLinkButtonState();
        }
    }

    private async void Link_Click(object sender, RoutedEventArgs e) => await LinkSelectedAsync();

    private async Task LinkSelectedAsync()
    {
        if (_linkInFlight)
            return;

        _linkInFlight = true;
        ErrorLine.Text = "";
        LinkButton.IsEnabled = false;
        SetBusy(true);
        try
        {
            var selectedId = ProjectList.SelectedItem is GizmoSummary sel ? sel.Id : "none";
            ProjectLinkDiagnostics.Log($"Link start mode={ProjectModeTabs.SelectedIndex} selected={selectedId}");

            StatusLine.Text = "Linking project…";
            await PrepareSessionAsync(showPane: false);
            var core = _host.ApiCore
                       ?? throw new InvalidOperationException("ChatGPT WebView is not ready.");

            var bundle = AdventureStore.Load(_adventureId)
                           ?? throw new InvalidOperationException("Adventure not found.");

            var sync = SyncSourcesCheck.IsChecked == true;
            var createThread = CreateThreadCheck.IsChecked == true;
            var syncProgress = new Progress<string>(msg => Dispatcher.Invoke(() => StatusLine.Text = msg));
            ProjectBindingResult result;
            var linkedId = bundle.Metadata.LinkedProjectId;

            if (ProjectModeTabs.SelectedIndex == 1)
            {
                var name = string.IsNullOrWhiteSpace(NewProjectNameBox.Text)
                    ? bundle.Metadata.Title
                    : NewProjectNameBox.Text.Trim();
                if (!ConfirmProjectSwitch(linkedId, $"new project \"{name}\""))
                    return;

                result = await _host.Binding.CreateAndLinkAsync(
                    core,
                    bundle,
                    name,
                    sync,
                    createThread,
                    syncProgress,
                    allowRecreate: !string.IsNullOrWhiteSpace(linkedId));
            }
            else if (ProjectModeTabs.SelectedIndex == 2)
            {
                if (!ChatGptUrls.TryParseGizmoIdFromUserInput(ManualUrlBox.Text, out var gizmoId))
                {
                    ErrorLine.Text = "Could not parse Project URL or gizmo id.";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(linkedId)
                    && ChatGptUrls.GizmoIdsEqual(gizmoId, linkedId))
                {
                    ErrorLine.Text = "This adventure is already linked to that project.";
                    return;
                }

                if (!ConfirmProjectSwitch(linkedId, gizmoId))
                    return;

                var detail = await _host.Api.GetGizmoDetailAsync(core, gizmoId);

                result = await _host.Binding.LinkExistingAsync(
                    core,
                    bundle,
                    gizmoId,
                    sync,
                    UpdateInstructionsCheck.IsChecked == true,
                    createThread,
                    projectTitle: detail?.Title,
                    existingProjectFiles: detail?.Files,
                    syncProgress: syncProgress);
            }
            else
            {
                if (ProjectList.SelectedItem is not GizmoSummary picked)
                {
                    ErrorLine.Text = "Select a project from the list.";
                    return;
                }

                if (!string.IsNullOrWhiteSpace(linkedId)
                    && ChatGptUrls.GizmoIdsEqual(picked.Id, linkedId))
                {
                    ErrorLine.Text = "This adventure is already linked to the selected project.";
                    return;
                }

                if (!ConfirmProjectSwitch(linkedId, $"{picked.Title} ({picked.Id})"))
                    return;

                result = await _host.Binding.LinkExistingAsync(
                    core,
                    bundle,
                    picked.Id,
                    sync,
                    UpdateInstructionsCheck.IsChecked == true,
                    createThread,
                    projectTitle: picked.Title,
                    existingProjectFiles: picked.Files,
                    syncProgress: syncProgress);
            }

            if (!result.Success)
            {
                ErrorLine.Text = result.Error ?? "Link failed.";
                return;
            }

            _linked = true;
            LinkStateChanged = true;
            ApplyLinkedProjectUiState();
            StatusLine.Text = $"Project linked ({result.GizmoId}). Click Done (linked) to finish.";
            DoneButton.Content = "Done (linked)";
            LinkButton.IsDefault = false;
            DoneButton.IsDefault = true;
            ProjectLinkDiagnostics.Log($"Link ok gizmo={result.GizmoId} conv={result.ConversationId}");
            if (!string.IsNullOrWhiteSpace(result.Error))
                MessageBox.Show(this, result.Error, "Linked with warnings", MessageBoxButton.OK, MessageBoxImage.Warning);

            WorkspaceTabs.SelectedItem = SourcesTab;
            await RefreshSourcesTabAsync();
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
            StatusLine.Text = "Link failed.";
            ProjectLinkDiagnostics.Log($"Link error: {ex.Message}");
        }
        finally
        {
            _linkInFlight = false;
            SetBusy(false);
            UpdateLinkButtonState();
        }
    }

    private Task RefreshSourcesTabAsync()
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            SyncStatusLine.Text = "Link a project first.";
            ManualPublishGuideLine.Text = "";
            ApiSyncPanel.Visibility = Visibility.Collapsed;
            return Task.CompletedTask;
        }

        _bundle = bundle;
        ApplySourcesTabPublishModeUi(bundle);
        return Task.CompletedTask;
    }

    private void ApplySourcesTabPublishModeUi(AdventureBundle bundle)
    {
        var sourcesDir = ProjectSourceExportService.SourcesDirectory(bundle);
        var readiness = ProjectSourceInjectionService.Evaluate(bundle);

        ApiSyncPanel.Visibility = Visibility.Collapsed;
        ManualPublishGuideLine.Visibility = Visibility.Visible;
        SyncStatusLine.Text =
            $"Manual publish — {readiness.SyncedFiles.Count} of {readiness.SyncedFiles.Count + readiness.NeedsRepublishCount} lore file(s) marked published.";
        ManualPublishGuideLine.Text =
            $"Canonical sources: {sourcesDir}\n"
            + "Use Manage sources… for the full publish walkthrough, version history, probe, and compare.\n"
            + "Quick path: Refresh export → copy/drag files to ChatGPT Project → mark Published.";
    }

    private async Task RefreshSyncPlanAsync()
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
        {
            SyncStatusLine.Text = "Link a project first.";
            return;
        }

        if (bundle.Metadata.Settings.SourcePublishMode == SourcePublishMode.Manual)
        {
            ApplySourcesTabPublishModeUi(bundle);
            return;
        }

        var core = _host.ApiCore;
        if (core is null)
        {
            SyncStatusLine.Text = "WebView not ready.";
            return;
        }

        SyncStatusLine.Text = "Building sync plan…";
        _syncRows.Clear();
        try
        {
            var progress = new Progress<string>(s => SyncStatusLine.Text = s);
            _bundle = bundle;
            _syncPlan = await _host.FileSync.BuildPlanAsync(core, bundle, progress);
            AdventureStore.Save(bundle);

            foreach (var item in _syncPlan.Items)
                AddSyncRow(item);

            UpdateSyncPlanSummary();
            UpdateLinkHealth();
        }
        catch (Exception ex)
        {
            SyncStatusLine.Text = "Plan failed.";
            ErrorLine.Text = ex.Message;
        }
    }

    private async void SyncReconcile_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _syncPlan is null || _host.ApiCore is not { } core)
            return;

        var reconciled = await SourceSyncUiHelper.ConfirmAndReconcileDuplicatesAsync(
            this,
            core,
            _bundle,
            _syncPlan,
            _host.Sync,
            _host.FileSync,
            new Progress<string>(s => SyncStatusLine.Text = s));

        if (reconciled)
            await RefreshSourcesTabAsync();
    }

    private async void SyncRefresh_Click(object sender, RoutedEventArgs e) => await RefreshSourcesTabAsync();

    private async void SyncApplySafe_Click(object sender, RoutedEventArgs e) => await ApplySyncAsync(autoSafeOnly: true);

    private async void SyncApplyAll_Click(object sender, RoutedEventArgs e) => await ApplySyncAsync(autoSafeOnly: false);

    private async Task ApplySyncAsync(bool autoSafeOnly)
    {
        if (_bundle is null || _syncPlan is null || _host.ApiCore is not { } core)
            return;

        SyncApplySafeButton.IsEnabled = false;
        SyncApplyAllButton.IsEnabled = false;
        try
        {
            var result = await _host.FileSync.ApplyAndVerifyAsync(
                core,
                _bundle,
                _syncPlan,
                autoSafeOnly,
                new Progress<string>(s => SyncStatusLine.Text = s));

            SyncCompleted = result.Success;
            foreach (var row in _syncRows)
                row.RefreshLabels();

            _bundle = AdventureStore.Load(_adventureId);
            if (_bundle is not null)
            {
                var cachedRemote = result.Plan?.DetectedRemoteFiles;
                _syncPlan = await _host.FileSync.BuildPlanAsync(
                    core,
                    _bundle,
                    ensureProjectPage: false,
                    cachedRemoteFiles: cachedRemote);
                _syncRows.Clear();
                foreach (var item in _syncPlan.Items)
                    AddSyncRow(item);
                UpdateSyncPlanSummary();
            }

            if (result.Success)
            {
                SyncStatusLine.Text = $"Sync complete. Pulled {result.Pulled}, replaced {result.Replaced}.";
                SourceSyncUiHelper.ShowApplyWarnings(this, result);
            }
            else
                SyncStatusLine.Text = $"Sync incomplete: {result.Error}";
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
        }
        finally
        {
            SyncApplySafeButton.IsEnabled = true;
            SyncApplyAllButton.IsEnabled = true;
        }
    }

    private async void ManageSources_Click(object sender, RoutedEventArgs e)
    {
        await _host.EnsureReadyAsync(_adventureId, showBrowserPane: true);
        var dlg = SourceManagerDialog.ShowNonModal(_adventureId, _host, this);
        dlg.ManagerClosed += async (_, _) => await RefreshSourcesTabAsync();
    }

    private void CopyInstructions_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        try
        {
            Clipboard.SetText(InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle));
            StatusLine.Text = "Instructions copied — paste into ChatGPT Project settings.";
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
        }
    }

    private void OpenSourcesFolder_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        var dir = ProjectSourceExportService.SourcesDirectory(bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void RefreshExport_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        ProjectSourceExportService.ExportForce(bundle);
        AdventureStore.Save(bundle);
        _bundle = bundle;
        StatusLine.Text = "Sources exported to local folder. Drag files to your ChatGPT Project.";
    }

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_host.GetDiagnosticsText());
            StatusLine.Text = "Diagnostics copied to clipboard.";
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = _linked;
        Close();
    }

    private void SetBusy(bool busy)
    {
        TestConnectionButton.IsEnabled = !busy;
        RefreshProjectsButton.IsEnabled = !busy;
        if (busy)
            LinkButton.IsEnabled = false;
        else
            UpdateLinkButtonState();
    }

    private void AddSyncRow(SourceSyncPlanItem item)
    {
        var row = new SourceSyncRowViewModel(item);
        row.ActionChanged += UpdateSyncPlanSummary;
        _syncRows.Add(row);
    }

    private void UpdateSyncPlanSummary()
    {
        if (_syncPlan is null)
            return;

        var autoSafe = _syncRows.Count(r => ProjectFileSyncPlanner.IsAutoSafe(r.PlanItem));
        var unresolved = _syncRows.Count(r =>
            ProjectFileSyncPlanner.ResolveAction(r.PlanItem) == SourceSyncAction.NeedsResolution);
        var summary = unresolved > 0
            ? $"{autoSafe} auto-safe, {unresolved} conflict(s) need choice, {_syncRows.Count} file(s)."
            : $"{autoSafe} auto-safe, {_syncPlan.ConflictCount} conflict(s), {_syncRows.Count} file(s).";

        var banner = SourceSyncUiHelper.FormatRemoteBanner(_syncPlan);
        SyncStatusLine.Text = string.IsNullOrWhiteSpace(banner) ? summary : $"{banner} {summary}";

        var orphanCount = SourceSyncUiHelper.CountOrphanDuplicates(_syncPlan);
        SyncReconcileButton.IsEnabled = orphanCount > 0;
    }

    private async void SyncClearBindings_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        if (!SourceSyncUiHelper.ConfirmClearRemoteBindings(this))
            return;

        SourceSyncUiHelper.ClearRemoteBindings(_bundle);
        await RefreshSourcesTabAsync();
    }
}

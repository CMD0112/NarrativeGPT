using System.Diagnostics;
using System.IO;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Diagnostics;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.WinUiBridge;
using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class ProjectWorkspacePage : UserControl
{
    private readonly Guid _adventureId;
    private List<GizmoSummary> _projects = [];
    private bool _sessionPrepared;
    private bool _linked;
    private bool _linkInFlight;
    private AdventureBundle? _bundle;
    private ProjectSessionStatus? _lastSessionStatus;

    public ProjectWorkspacePage(Guid adventureId)
    {
        _adventureId = adventureId;
        InitializeComponent();
        Loaded += async (_, _) => await OnLoadedAsync();
    }

    public bool LinkedSuccessfully => _linked;

    public bool LinkStateChanged { get; private set; }

    public bool SyncCompleted { get; private set; }

    private int ProjectModeTabIndex => ProjectModeTabs.SelectedIndex;

    private IProgress<string> StatusProgress => new Progress<string>(s => StatusLine.Text = s);

    private async Task OnLoadedAsync()
    {
        PrefillCreateProjectName();
        ApplyDesigningProjectUiState();
        await PrepareSessionAsync(showPane: false);
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
        var status = _lastSessionStatus;
        SessionHintLine.Text = status is { IsAuthenticated: true, HasDeviceId: true }
            ? "Signed in — pick a project below, then Link."
            : "Not fully signed in — use the Connection tab if the project list is empty.";
    }

    private void ApplyLinkedProjectUiState()
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        AdventureNavigationService.SyncLinkedFields(bundle);
        var linkedId = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(linkedId))
        {
            LinkedProjectBannerPanel.Visibility = Visibility.Collapsed;
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
        LinkHealthLine.Text = bundle is null ? "" : SourceSyncUiHelper.FormatLinkHealth(bundle);
    }

    private void WorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is not TabViewItem tab)
            return;

        if (tab == ProjectsTab && _projects.Count == 0)
            _ = RefreshProjectsAsync();

        if (tab.Header?.ToString() == "Sources" && _linked)
            _ = RefreshSourcesTabAsync();

        UpdateLinkButtonState();
    }

    private void ProjectModeTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateLinkButtonState();

    private void ProjectList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        UpdateProjectSelectionLine();

    private async void ProjectList_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (ProjectModeTabIndex != 0 || ProjectList.SelectedItem is null)
            return;

        await LinkSelectedAsync();
    }

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

    private void NewProjectNameBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateLinkButtonState();

    private void ManualUrlBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateLinkButtonState();

    private void UpdateLinkButtonState()
    {
        if (WorkspaceTabs.SelectedItem is not TabViewItem tab || tab != ProjectsTab)
        {
            LinkButton.Content = "Link project";
            LinkButton.IsEnabled = false;
            return;
        }

        var bundle = AdventureStore.Load(_adventureId);
        var linkedId = bundle?.Metadata.LinkedProjectId;
        var isLinked = !string.IsNullOrWhiteSpace(linkedId);
        var switchVerb = isLinked ? "Switch" : "Link";

        switch (ProjectModeTabIndex)
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

    private async Task<bool> ConfirmProjectSwitchAsync(string? currentProjectId, string targetLabel)
    {
        if (string.IsNullOrWhiteSpace(currentProjectId))
            return true;

        return await WinUiDialogHelper.ConfirmAsync(
            App.CurrentMainWindow,
            "Switch ChatGPT Project",
            $"Switch this adventure from the current Project to {targetLabel}?\n\n"
            + "Remote source bindings and play-tab pins for the old Project will be cleared. "
            + "Local lore files in this adventure are kept.",
            confirmText: "Switch");
    }

    private async void UnlinkProject_Click(object sender, RoutedEventArgs e)
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null || string.IsNullOrWhiteSpace(bundle.Metadata.LinkedProjectId))
            return;

        if (!await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                "Unlink Project",
                "Unlink this adventure from its ChatGPT Project?\n\n"
                + "Local adventure data and lore files are kept. You can link again later.",
                confirmText: "Unlink"))
        {
            return;
        }

        AdventureProjectBindingService.ClearProjectLink(bundle);
        LinkStateChanged = true;
        _linked = false;
        _bundle = bundle;
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
            var snapshot = await WinUiProjectHostOperations.PrepareProjectSessionAsync(_adventureId, showPane);
            _lastSessionStatus = snapshot?.Status;
            _sessionPrepared = snapshot is not null;
            StatusLine.Text = _sessionPrepared ? "Session ready." : "Session not ready.";
            UpdateChecklist(_lastSessionStatus);
        }
        catch (ChatGptApiException ex)
        {
            StatusLine.Text = "Session not ready.";
            ErrorLine.Text = ex.Message;
            UpdateChecklist(_lastSessionStatus);
        }
        catch (Exception ex)
        {
            StatusLine.Text = "Session failed.";
            ErrorLine.Text = ex.Message;
            UpdateChecklist(_lastSessionStatus);
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

            var probe = await WinUiProjectHostOperations.ProbeProjectSidebarAsync(_adventureId);
            if (probe is not null)
            {
                ProbeLine.Text =
                    $"Probe: status={probe.Status} items={probe.ItemCount} keys=[{string.Join(", ", probe.JsonKeys)}] device={probe.HasDeviceId}";
                StatusLine.Text = probe.Ok ? "Connection OK." : "Connection probe failed.";
                if (!probe.Ok && !string.IsNullOrEmpty(probe.Error))
                    ErrorLine.Text = probe.Error;
            }

            UpdateSessionHint();
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

    private async void CaptureHeaders_Click(object sender, RoutedEventArgs e) =>
        await WinUiDialogHelper.ShowInfoAsync(
            App.CurrentMainWindow,
            "Capture headers",
            "Browse Projects in the ChatGPT tab (sidebar), open a project, then click Test connection. "
            + "Headers are saved to api-client-profile.json automatically.");

    private async void RefreshProjects_Click(object sender, RoutedEventArgs e) =>
        await RefreshProjectsAsync();

    private async Task RefreshProjectsAsync()
    {
        ErrorLine.Text = "";
        SetBusy(true);
        try
        {
            await PrepareSessionAsync(showPane: false);
            var result = await WinUiProjectHostOperations.DiscoverProjectsAsync(_adventureId);
            if (result is null)
                return;

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

    private async void Link_Click(object sender, RoutedEventArgs e) =>
        await LinkSelectedAsync();

    private async Task LinkSelectedAsync()
    {
        if (_linkInFlight)
            return;

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        var linkedId = bundle.Metadata.LinkedProjectId;
        var mode = ProjectModeTabIndex;
        GizmoSummary? picked = ProjectList.SelectedItem as GizmoSummary;
        string? manualGizmoId = null;
        string? createName = null;
        ProjectLinkMode linkMode;

        if (mode == 1)
        {
            linkMode = ProjectLinkMode.CreateNew;
            createName = string.IsNullOrWhiteSpace(NewProjectNameBox.Text)
                ? bundle.Metadata.Title
                : NewProjectNameBox.Text.Trim();
            if (!await ConfirmProjectSwitchAsync(linkedId, $"new project \"{createName}\""))
                return;
        }
        else if (mode == 2)
        {
            linkMode = ProjectLinkMode.FromUrl;
            if (!ChatGptUrls.TryParseGizmoIdFromUserInput(ManualUrlBox.Text, out manualGizmoId))
            {
                ErrorLine.Text = "Could not parse Project URL or gizmo id.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(linkedId)
                && ChatGptUrls.GizmoIdsEqual(manualGizmoId, linkedId))
            {
                ErrorLine.Text = "This adventure is already linked to that project.";
                return;
            }

            if (!await ConfirmProjectSwitchAsync(linkedId, manualGizmoId))
                return;
        }
        else
        {
            linkMode = ProjectLinkMode.FromList;
            if (picked is null)
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

            if (!await ConfirmProjectSwitchAsync(linkedId, $"{picked.Title} ({picked.Id})"))
                return;
        }

        _linkInFlight = true;
        ErrorLine.Text = "";
        LinkButton.IsEnabled = false;
        SetBusy(true);
        try
        {
            ProjectLinkDiagnostics.Log($"Link start mode={mode} selected={picked?.Id ?? manualGizmoId ?? "none"}");
            StatusLine.Text = "Linking project…";
            await PrepareSessionAsync(showPane: false);

            var result = await WinUiProjectHostOperations.LinkProjectAsync(new ProjectLinkRequest
            {
                AdventureId = _adventureId,
                Mode = linkMode,
                SelectedProject = picked,
                ManualGizmoId = manualGizmoId,
                CreateName = createName,
                SyncSources = SyncSourcesCheck.IsChecked == true,
                PushInstructions = UpdateInstructionsCheck.IsChecked == true,
                CreateThread = CreateThreadCheck.IsChecked == true,
                Progress = StatusProgress,
            });

            if (result is null)
                return;

            if (!result.Success)
            {
                ErrorLine.Text = result.Error ?? "Link failed.";
                return;
            }

            _linked = true;
            LinkStateChanged = true;
            ApplyLinkedProjectUiState();
            StatusLine.Text = $"Project linked ({result.GizmoId}).";
            ProjectLinkDiagnostics.Log($"Link ok gizmo={result.GizmoId} conv={result.ConversationId}");

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                await WinUiDialogHelper.ShowInfoAsync(
                    App.CurrentMainWindow,
                    "Linked with warnings",
                    result.Error);
            }

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

        SyncStatusLine.Text =
            $"Manual publish — {readiness.SyncedFiles.Count} of {readiness.SyncedFiles.Count + readiness.NeedsRepublishCount} lore file(s) marked published.";
        ManualPublishGuideLine.Text =
            $"Canonical sources: {sourcesDir}\n"
            + "Use Manage sources… for the full publish walkthrough, version history, probe, and compare.\n"
            + "Quick path: Refresh export → copy/drag files to ChatGPT Project → mark Published.";
    }

    private async void ManageSources_Click(object sender, RoutedEventArgs e)
    {
        await WinUiDialogHostService.ShowPlaySettingsAsync(
            App.CurrentMainWindow,
            _adventureId,
            PlaySettingsTab.Sources);
        await RefreshSourcesTabAsync();
    }

    private void CopyInstructions_Click(object sender, RoutedEventArgs e)
    {
        var bundle = _bundle ?? AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        try
        {
            var package = new DataPackage();
            package.SetText(InstructionSourcesPolicy.BuildStaticInstructionsBody(bundle));
            Clipboard.SetContent(package);
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
        ApplySourcesTabPublishModeUi(bundle);
    }

    private async void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = await WinUiProjectHostOperations.GetProjectDiagnosticsAsync();
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
            StatusLine.Text = "Diagnostics copied to clipboard.";
        }
        catch (Exception ex)
        {
            ErrorLine.Text = ex.Message;
        }
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
}

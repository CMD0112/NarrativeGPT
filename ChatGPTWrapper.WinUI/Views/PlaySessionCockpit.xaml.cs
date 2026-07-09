using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Controls;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class PlaySessionCockpit : UserControl
{
    private WinUiPlaySessionService? _session;
    private bool _suppressSegment;
    private bool _suppressNarrator;
    private readonly Dictionary<string, ActionListRow> _aiToolRows = new(StringComparer.Ordinal);

    public PlaySessionCockpit()
    {
        InitializeComponent();
    }

    public event EventHandler? ReviewRequested;

    public event EventHandler? ManageThreadsRequested;

    public void Bind(WinUiPlaySessionService session)
    {
        _session = session;
        InitializeSections();
        BindNarratorControls(resetSceneProfile: true);
        ResyncFromStore();
        session.StatusChanged += (_, _) => ResyncFromStore();
    }

    public void ApplyLayout(PlayLayoutContext context)
    {
        CockpitBorder.Padding = context.Capabilities.UseCompactSessionPadding
            ? new Thickness(8)
            : new Thickness(12);

        ManageThreadsButton.Content = context.Capabilities.UseFullFooterLabels
            ? "Manage threads"
            : "Threads";

        NarratorSettingsButton.Content = context.Capabilities.UseFullFooterLabels
            ? "Full narrator settings"
            : "Narrator settings";
    }

    public void ResyncFromStore()
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        AdventureNavigationService.SyncLinkedFields(bundle);
        AdventureThreadRegistryService.EnsureMigrated(bundle);

        var reviewCount = PendingReviewService.GetCounts(bundle).Total;
        ReviewChip.Label = reviewCount > 0 ? $"Review ({reviewCount})" : "Review";

        var hasProject = AdventureProjectBindingService.HasLinkedProject(bundle);
        LinkProjectBanner.Visibility = hasProject ? Visibility.Collapsed : Visibility.Visible;

        var playEntry = AdventureThreadRegistryService.GetActiveEntry(bundle, AdventureThreadKind.Play);
        var pinned = !string.IsNullOrWhiteSpace(playEntry?.PinnedTabKey);
        PlayTabStatusText.Text = pinned
            ? $"Play tab: {playEntry?.PinnedTabTitle ?? "ChatGPT tab"}"
            : "Play tab: not linked — pin a tab from Manage threads or Play settings → Session.";

        ThreadStatusLink.Content = AdventureThreadRegistryService.FormatConnectionSummary(bundle);

        var sourceReadiness = ProjectSourceInjectionService.Evaluate(bundle);
        var sourcesLine = ProjectSourceInjectionService.FormatLinkStatusSources(sourceReadiness);
        var duplicateHint = bundle.SourceManifest.LastKnownDuplicateRemotes > 0
            ? $" ({bundle.SourceManifest.LastKnownDuplicateRemotes} duplicate remote(s))"
            : "";
        var instructionsLine = InstructionSourcesPolicy.FormatInstructionSyncStatus(bundle);
        var sourcesWithInstructions = string.IsNullOrWhiteSpace(instructionsLine)
            ? $"{sourcesLine}{duplicateHint}"
            : $"{sourcesLine} | {instructionsLine}{duplicateHint}";
        var canonStatus = CanonReconciliationPromptService.FormatUnresolvedStatus(bundle);
        if (!string.IsNullOrWhiteSpace(canonStatus))
            sourcesWithInstructions += " | " + canonStatus;

        if (!hasProject)
        {
            var packet = sourceReadiness.CanDelegateStaticContent
                ? "source-delegated packets"
                : sourceReadiness.HasLinkedProject
                    ? "inline fallback"
                    : "minimal local";
            sourcesWithInstructions = $"No Project — {packet}" + (canonStatus is not null ? " | " + canonStatus : "");
        }

        SourcesStatusLink.Content = sourcesWithInstructions;

        RefreshAiToolRows(bundle);
        UpdateOverrideChips(bundle);
    }

    public void RestoreSection()
    {
        if (_session is null)
            return;

        var section = _session.ResolveCompanionSection();
        _suppressSegment = true;
        try
        {
            SectionSegment.SelectedIndex = section switch
            {
                "Narrator" => 1,
                "Tools" => 2,
                _ => 0,
            };
        }
        finally
        {
            _suppressSegment = false;
        }

        ApplySectionVisibility();
    }

    private void InitializeSections()
    {
        SectionSegment.ItemsSource = new List<object>
        {
            new SegmentedItemModel { Content = "Session", Tag = "Session" },
            new SegmentedItemModel { Content = "Narrator", Tag = "Narrator" },
            new SegmentedItemModel { Content = "AI Tools", Tag = "Tools" },
        };
        SectionSegment.SelectedIndex = 0;

        ScopeSegment.ItemsSource = new List<object>
        {
            new SegmentedItemModel { Content = "This send", Tag = NarratorOverrideScope.Turn },
            new SegmentedItemModel { Content = "Session", Tag = NarratorOverrideScope.Session },
            new SegmentedItemModel { Content = "Adventure", Tag = NarratorOverrideScope.Adventure },
        };

        ApplySectionVisibility();
    }

    private void BindNarratorControls(bool resetSceneProfile)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        _suppressNarrator = true;
        try
        {
            var items = new List<NarratorPresetComboItem> { NarratorPresetComboItem.Inherit() };
            items.AddRange(NarratorPresetLibrary.SceneProfiles.Select(p => new NarratorPresetComboItem(p.Id, p.DisplayName)));
            SceneProfileCombo.ItemsSource = items;
            if (resetSceneProfile)
                SceneProfileCombo.SelectedIndex = 0;

            var scope = NarratorOverrideResolver.ReadPersistedScope(bundle.Metadata.Settings);
            ScopeSegment.SelectedIndex = scope switch
            {
                NarratorOverrideScope.Session => 1,
                NarratorOverrideScope.Adventure => 2,
                _ => 0,
            };

            UpdateOverrideChips(bundle);
        }
        finally
        {
            _suppressNarrator = false;
        }
    }

    private void UpdateOverrideChips(AdventureBundle bundle)
    {
        OverrideChipsText.Text = NarratorOverrideResolver.GetActiveOverrideChips(bundle) is { Count: > 0 } chips
            ? $"Active: {string.Join(" · ", chips)}"
            : "No active overrides.";
    }

    private void RefreshAiToolRows(AdventureBundle bundle)
    {
        AiToolsPanel.Children.Clear();
        _aiToolRows.Clear();

        foreach (var state in AiToolActionRowBuilder.Build(bundle))
        {
            var row = new ActionListRow
            {
                Title = state.Title,
                Hint = !state.IsEnabled && !string.IsNullOrWhiteSpace(state.DisabledReason)
                    ? state.DisabledReason
                    : state.Hint ?? string.Empty,
                ActionLabel = state.IsEnabled ? "Run" : "—",
                RowEnabled = state.IsEnabled,
            };

            var actionKey = state.ActionKey;
            row.RunRequested += async (_, _) => await RunAiToolAsync(actionKey);
            AiToolsPanel.Children.Add(row);
            _aiToolRows[actionKey] = row;
        }
    }

    private async Task RunAiToolAsync(string actionKey)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        var (success, message) = await WinUiAiToolJobService.RunJobsAsync(
            bundle.Metadata.Id,
            [actionKey],
            _session.UtilityWorker);

        if (!string.IsNullOrWhiteSpace(message))
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Utility jobs", message);

        if (success)
            _session.ReloadBundle(bundle.Metadata.Id);
    }

    private void SectionSegment_SelectionChanged(object sender, EventArgs e)
    {
        if (_suppressSegment)
            return;

        var tag = SectionSegment.SelectedTag as string ?? "Session";
        _session?.SaveCompanionSection(tag);
        ApplySectionVisibility();
    }

    private void ApplySectionVisibility()
    {
        var tag = SectionSegment.SelectedTag as string ?? "Session";
        SessionPanel.Visibility = tag == "Session" ? Visibility.Visible : Visibility.Collapsed;
        NarratorPanel.Visibility = tag == "Narrator" ? Visibility.Visible : Visibility.Collapsed;
        ToolsPanel.Visibility = tag == "Tools" ? Visibility.Visible : Visibility.Collapsed;
    }

    private NarratorOverrideScope GetSelectedScope() =>
        ScopeSegment.SelectedTag is NarratorOverrideScope scope
            ? scope
            : NarratorOverrideScope.Turn;

    private void ScopeSegment_SelectionChanged(object sender, EventArgs e)
    {
        if (_suppressNarrator || _session?.CurrentBundle is not { } bundle)
            return;

        NarratorOverrideResolver.PersistScope(bundle.Metadata.Settings, GetSelectedScope());
        AdventureStore.Save(bundle);
        UpdateOverrideChips(bundle);
    }

    private void SceneProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressNarrator || _session?.CurrentBundle is not { } bundle)
            return;

        if (SceneProfileCombo.SelectedItem is not NarratorPresetComboItem item)
            return;

        var scope = GetSelectedScope();
        if (item.IsInherit)
            NarratorOverrideResolver.ResetScope(bundle, scope);
        else if (!string.IsNullOrWhiteSpace(item.Id))
            NarratorPresetLibrary.ApplySceneProfile(bundle, item.Id, scope);

        AdventureStore.Save(bundle);
        UpdateOverrideChips(bundle);
        _session.NotifyStatusChanged();
    }

    private void ReviewChip_Click(object sender, RoutedEventArgs e) =>
        ReviewRequested?.Invoke(this, EventArgs.Empty);

    private void ManageThreads_Click(object sender, RoutedEventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    private void ThreadStatusLink_Click(object sender, RoutedEventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    private async void SourcesStatusLink_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        await WinUiDialogHostService.ShowSourceManagerAsync(App.CurrentMainWindow, bundle.Metadata.Id);
        ResyncFromStore();
    }

    private async void LinkProject_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        await WinUiThreadManagerBridge.OpenProjectWorkspaceAsync(bundle.Metadata.Id);
        _session.ReloadBundle(bundle.Metadata.Id);
        ResyncFromStore();
    }

    private async void NarratorSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        await WinUiDialogHostService.ShowPlaySettingsAsync(
            App.CurrentMainWindow,
            bundle.Metadata.Id,
            PlaySettingsTab.Injection);
        _session.ReloadBundle(bundle.Metadata.Id);
        BindNarratorControls(resetSceneProfile: false);
        ResyncFromStore();
    }
}

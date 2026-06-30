using System.Collections.ObjectModel;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public sealed class AdventureThreadManagerActions
{
    public required Func<Task> StartNarrativeFromSourcesAsync { get; init; }

    public required Func<Task> OpenPlayHandoffWizardAsync { get; init; }

    public required Func<Task> StartNewDesignThreadAsync { get; init; }

    public required Func<AdventureThreadKind, Task<Guid?>> CreateThreadSlotAsync { get; init; }

    public required Func<AdventureThreadKind, Guid, Task> ActivateEntryAsync { get; init; }

    public required Func<AdventureThreadKind, Guid, Task> OpenEntryAsync { get; init; }

    public required Func<Task> OpenProjectWorkspaceAsync { get; init; }

    public required Func<AdventureThreadKind, Guid, bool, Task> PinTabToEntryAsync { get; init; }

    public required Func<AdventureThreadKind, Guid, Task> ClearEntryPinAsync { get; init; }

    public required Func<Guid, Task> RemoveEntryAsync { get; init; }

    public required Func<Task> ProbeUtilityWorkerAsync { get; init; }

    public required Func<Task> SetupUtilityWorkerAsync { get; init; }

    public required Func<bool, Task> SetupUtilityWorkerReplaceAsync { get; init; }

    public required Func<Task> PinUtilityWorkerFromCurrentTabAsync { get; init; }

    public required Func<Task> OpenUtilityWorkerAsync { get; init; }
}

public partial class AdventureThreadManagerDialog : ShellDialogWindow
{
    private readonly Guid _adventureId;
    private readonly AdventureThreadManagerActions _actions;
    private readonly Dictionary<AdventureThreadKind, ThreadTabContent> _tabPanels = new();
    private UtilityWorkerTabContent? _utilityWorkerTab;
    private bool _changed;
    private bool _suppressDeliverySettings;
    private bool _showArchived = true;
    private Guid? _selectedEntryId;

    public AdventureThreadManagerDialog(
        Guid adventureId,
        AdventureThreadManagerActions actions,
        AdventureThreadKind initialKind = AdventureThreadKind.Play)
    {
        _adventureId = adventureId;
        _actions = actions;
        InitializeComponent();

        BuildTab(AdventureThreadKind.Play, labelEditable: false);
        BuildTab(AdventureThreadKind.Design, labelEditable: true);
        BuildUtilityWorkerTab();

        KindTabs.SelectedIndex = initialKind switch
        {
            AdventureThreadKind.Design => 1,
            AdventureThreadKind.UtilityWorker => 2,
            _ => 0,
        };
        LoadProjectSummary();
        LoadDeliverySettings();
        RefreshCurrentTab();
    }

    private void LoadProjectSummary()
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
        {
            ProjectSummaryBlock.Text = "Adventure not found.";
            ChangeProjectButton.IsEnabled = false;
            return;
        }

        AdventureProjectBindingService.SyncLinkedProjectFields(bundle.Metadata);
        var project = AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);
        if (string.IsNullOrWhiteSpace(project))
        {
            ProjectSummaryBlock.Text = "No ChatGPT Project linked — use Link project… to bind this adventure to an existing Project (required for play, design, and utility worker chats).";
            ChangeProjectButton.Content = "Link project…";
            return;
        }

        var readiness = ProjectSourceInjectionService.Evaluate(bundle);
        var sources = ProjectSourceInjectionService.FormatLinkStatusSources(readiness);
        ProjectSummaryBlock.Text = $"Linked: {project} · {sources}";
        ChangeProjectButton.Content = "Change project…";
    }

    private void LoadDeliverySettings()
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        _suppressDeliverySettings = true;
        try
        {
            HideInlineUtilityCheck.IsChecked = bundle.Metadata.Settings.HideInlineUtilityDuringPlay;
            ShowInlineUtilityTrafficCheck.IsChecked = bundle.Metadata.Settings.ShowInlineUtilityTraffic;
        }
        finally
        {
            _suppressDeliverySettings = false;
        }
    }

    private void DeliverySetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressDeliverySettings)
            return;

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        bundle.Metadata.Settings.HideInlineUtilityDuringPlay = HideInlineUtilityCheck.IsChecked == true;
        bundle.Metadata.Settings.ShowInlineUtilityTraffic = ShowInlineUtilityTrafficCheck.IsChecked == true;
        AdventureStore.Save(bundle);
        _changed = true;
    }

    private async void ChangeProject_Click(object sender, RoutedEventArgs e)
    {
        await _actions.OpenProjectWorkspaceAsync();
        LoadProjectSummary();
        RefreshCurrentTab();
        _changed = true;
    }

    private void BuildTab(AdventureThreadKind kind, bool labelEditable)
    {
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = false,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            MinHeight = 220,
            EnableRowVirtualization = false,
            Focusable = true,
        };

        if (labelEditable)
        {
            grid.CellEditEnding += (_, e) =>
            {
                if (e.EditAction != DataGridEditAction.Commit
                    || e.Row.Item is not ThreadManagerRow row)
                {
                    return;
                }

                SaveLabel(row);
            };
        }

        WireGridSelection(grid, kind);

        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Label",
            Binding = new Binding(nameof(ThreadManagerRow.Label)) { Mode = labelEditable ? BindingMode.TwoWay : BindingMode.OneWay },
            Width = new DataGridLength(120),
            IsReadOnly = !labelEditable,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Status",
            Binding = new Binding(nameof(ThreadManagerRow.StatusDisplay)),
            Width = new DataGridLength(80),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Conversation",
            Binding = new Binding(nameof(ThreadManagerRow.ConversationDisplay)),
            Width = new DataGridLength(140),
            IsReadOnly = true,
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "Tab",
            Binding = new Binding(nameof(ThreadManagerRow.TabTitleDisplay)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            IsReadOnly = true,
        });

        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };

        var newSlot = new Button
        {
            Content = ThreadManagerCopy.NewThreadSlotButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        newSlot.Click += async (_, _) =>
        {
            var entryId = await _actions.CreateThreadSlotAsync(kind);
            if (entryId is { } id)
            {
                _selectedEntryId = id;
                _changed = true;
                RefreshCurrentTab();
            }
        };

        var pinCurrent = new Button
        {
            Content = ThreadManagerCopy.PinCurrentToSelectedButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        pinCurrent.Click += (_, _) => RunRowAction(grid, row => PinTabToSelectedAsync(kind, row, usePicker: false));

        var pickTab = new Button
        {
            Content = ThreadManagerCopy.PickTabToPinButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        pickTab.Click += (_, _) => RunRowAction(grid, row => PinTabToSelectedAsync(kind, row, usePicker: true));

        var clearPin = new Button
        {
            Content = ThreadManagerCopy.ClearPinButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        clearPin.Click += (_, _) => RunRowAction(grid, row => ClearPinAsync(kind, row));

        var setActive = new Button { Content = "Set active", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        setActive.Click += (_, _) => RunRowAction(grid, row => SetActiveAsync(kind, row));

        var archive = new Button { Content = "Archive", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        archive.Click += (_, _) => RunRowAction(grid, row => ArchiveAsync(kind, row));

        var remove = new Button
        {
            Content = ThreadManagerCopy.RemoveButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        remove.Click += (_, _) => RunRowAction(grid, row => RemoveAsync(row));

        var open = new Button { Content = "Open in browser", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 4) };
        open.Click += (_, _) => RunRowAction(grid, row => OpenAsync(kind, row));

        var newNarrative = new Button
        {
            Content = kind == AdventureThreadKind.Design
                ? ThreadManagerCopy.StartNewDesignThreadButton
                : PlayThreadRotationCopy.NarrativeFromSourcesButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
        };
        newNarrative.Click += async (_, _) =>
        {
            if (kind == AdventureThreadKind.Play)
                await _actions.StartNarrativeFromSourcesAsync();
            else if (kind == AdventureThreadKind.Design)
                await StartNewDesignThreadAsync(kind);
        };
        newNarrative.Visibility = kind == AdventureThreadKind.UtilityWorker
            ? Visibility.Collapsed
            : Visibility.Visible;

        var handOff = new Button
        {
            Content = PlayThreadRotationCopy.HandoffToNewChatButton,
            Padding = new Thickness(10, 4, 10, 4),
            Margin = new Thickness(0, 0, 6, 4),
            Visibility = kind == AdventureThreadKind.Play ? Visibility.Visible : Visibility.Collapsed,
        };
        handOff.Click += async (_, _) => await _actions.OpenPlayHandoffWizardAsync();

        var showArchived = new CheckBox
        {
            Content = ThreadManagerCopy.ShowArchivedCheck,
            IsChecked = _showArchived,
            Margin = new Thickness(0, 0, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
        };
        showArchived.Checked += (_, _) => { _showArchived = true; RefreshCurrentTab(); };
        showArchived.Unchecked += (_, _) => { _showArchived = false; RefreshCurrentTab(); };

        if (kind == AdventureThreadKind.UtilityWorker)
        {
            var probe = new Button
            {
                Content = "Probe API push/pull",
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 6, 4),
            };
            probe.Click += async (_, _) =>
            {
                await _actions.ProbeUtilityWorkerAsync();
                _changed = true;
                RefreshCurrentTab();
            };
            actions.Children.Add(probe);
        }

        actions.Children.Add(newSlot);
        actions.Children.Add(pinCurrent);
        actions.Children.Add(pickTab);
        actions.Children.Add(clearPin);
        actions.Children.Add(setActive);
        actions.Children.Add(archive);
        actions.Children.Add(remove);
        actions.Children.Add(open);
        actions.Children.Add(newNarrative);
        if (kind == AdventureThreadKind.Play)
            actions.Children.Add(handOff);
        actions.Children.Add(showArchived);

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(actions);

        _tabPanels[kind] = new ThreadTabContent
        {
            Grid = grid,
            Root = panel,
            NewSlotButton = newSlot,
            PinCurrentButton = pinCurrent,
            PickTabButton = pickTab,
            ClearPinButton = clearPin,
            SetActiveButton = setActive,
            ArchiveButton = archive,
            RemoveButton = remove,
            OpenButton = open,
            ShowArchivedCheck = showArchived,
        };

        foreach (TabItem tab in KindTabs.Items)
        {
            if (tab.Tag as string != kind.ToString())
                continue;

            tab.Content = _tabPanels[kind].Root;
            break;
        }
    }

    private void BuildUtilityWorkerTab()
    {
        var intro = new TextBlock
        {
            Text = UtilityWorkerSetupCopy.TabIntro,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
            Foreground = (Brush)FindResource("TextMutedBrush"),
        };

        var statusBanner = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 12),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
        var statusBannerText = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeights.SemiBold,
        };
        statusBanner.Child = statusBannerText;

        var stepProject = CreateStepBlock();
        var stepWorker = CreateStepBlock();
        var stepVerified = CreateStepBlock();
        var detail = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 12),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextMutedBrush"),
        };

        var setup = new Button
        {
            Content = UtilityWorkerSetupCopy.SetupButton,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 32,
        };
        setup.Click += async (_, _) =>
        {
            setup.IsEnabled = false;
            try
            {
                await _actions.SetupUtilityWorkerAsync();
                _changed = true;
                RefreshCurrentTab();
            }
            finally
            {
                setup.IsEnabled = true;
            }
        };

        var useCurrent = new Button
        {
            Content = UtilityWorkerSetupCopy.UseCurrentTabButton,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 32,
        };
        useCurrent.Click += async (_, _) =>
        {
            useCurrent.IsEnabled = false;
            try
            {
                await _actions.PinUtilityWorkerFromCurrentTabAsync();
                _changed = true;
                RefreshCurrentTab();
            }
            finally
            {
                useCurrent.IsEnabled = true;
            }
        };

        var verify = new Button
        {
            Content = UtilityWorkerSetupCopy.VerifyButton,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 32,
        };
        verify.Click += async (_, _) =>
        {
            verify.IsEnabled = false;
            try
            {
                await _actions.ProbeUtilityWorkerAsync();
                _changed = true;
                RefreshCurrentTab();
            }
            finally
            {
                verify.IsEnabled = true;
            }
        };

        var openWorker = new Button
        {
            Content = UtilityWorkerSetupCopy.OpenWorkerButton,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 32,
        };
        openWorker.Click += async (_, _) =>
        {
            await _actions.OpenUtilityWorkerAsync();
            _changed = true;
        };

        var replace = new Button
        {
            Content = UtilityWorkerSetupCopy.ReplaceWorkerButton,
            Padding = new Thickness(10, 6, 10, 6),
            Margin = new Thickness(0, 0, 8, 8),
            MinHeight = 32,
        };
        replace.Click += async (_, _) =>
        {
            var bundle = AdventureStore.Load(_adventureId);
            var linkedProject = bundle is null
                ? null
                : AdventureProjectBindingService.GetLinkedProjectId(bundle.Metadata);

            if (MessageBox.Show(
                    this,
                    UtilityWorkerSetupCopy.ReplaceWorkerConfirmMessage(linkedProject),
                    UtilityWorkerSetupCopy.DialogTitle,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            replace.IsEnabled = false;
            try
            {
                await _actions.SetupUtilityWorkerReplaceAsync(true);
                _changed = true;
                RefreshCurrentTab();
            }
            finally
            {
                replace.IsEnabled = true;
            }
        };

        var actions = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        actions.Children.Add(setup);
        actions.Children.Add(useCurrent);
        actions.Children.Add(verify);
        actions.Children.Add(openWorker);
        actions.Children.Add(replace);

        var card = new Border
        {
            Style = (Style)FindResource("ShellCardStyle"),
            Padding = new Thickness(14),
            Child = new StackPanel
            {
                Children =
                {
                    intro,
                    statusBanner,
                    stepProject,
                    stepWorker,
                    stepVerified,
                    detail,
                    actions,
                },
            },
        };

        _utilityWorkerTab = new UtilityWorkerTabContent
        {
            Root = card,
            StatusBanner = statusBanner,
            StatusBannerText = statusBannerText,
            StepProjectBlock = stepProject,
            StepWorkerBlock = stepWorker,
            StepVerifiedBlock = stepVerified,
            DetailBlock = detail,
            SetupButton = setup,
            UseCurrentTabButton = useCurrent,
            VerifyButton = verify,
            OpenWorkerButton = openWorker,
            ReplaceWorkerButton = replace,
        };

        foreach (TabItem tab in KindTabs.Items)
        {
            if (tab.Tag as string != AdventureThreadKind.UtilityWorker.ToString())
                continue;

            tab.Content = card;
            break;
        }
    }

    private static TextBlock CreateStepBlock() =>
        new()
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
            FontWeight = FontWeights.SemiBold,
        };

    private bool _refreshingTab;

    private void KindTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // TabControl.SelectionChanged bubbles from nested selectors (DataGrid, etc.).
        if (!ReferenceEquals(e.OriginalSource, KindTabs))
            return;

        RefreshCurrentTab();
    }

    private void RefreshCurrentTab()
    {
        if (_refreshingTab)
            return;

        if (KindTabs.SelectedItem is not TabItem tab
            || tab.Tag is not string kindName
            || !Enum.TryParse<AdventureThreadKind>(kindName, out var kind))
        {
            return;
        }

        _refreshingTab = true;
        try
        {
            var bundle = AdventureStore.Load(_adventureId);
            if (bundle is null)
                return;

            AdventureThreadRegistryService.EnsureMigrated(bundle);

            if (kind == AdventureThreadKind.UtilityWorker)
            {
                RefreshUtilityWorkerTab(bundle);
            }
            else
            {
                foreach (var tabPanel in _tabPanels.Values)
                {
                    if (tabPanel.ShowArchivedCheck.IsChecked != _showArchived)
                        tabPanel.ShowArchivedCheck.IsChecked = _showArchived;
                }

                var panel = _tabPanels[kind];
                var rows = AdventureThreadRegistryService.ListEntries(bundle, kind, includeArchived: _showArchived)
                    .Select(entry => ThreadManagerRow.FromEntry(bundle, entry))
                    .ToList();
                panel.Grid.ItemsSource = new ObservableCollection<ThreadManagerRow>(rows);
                SelectInitialRow(panel, rows);
            }

            var playActive = AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Play);
            var designActive = AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.Design);
            var workerActive = AdventureThreadRegistryService.FormatThreadStatus(bundle, AdventureThreadKind.UtilityWorker);
            var workerCaps = UtilityWorkerSessionService.FormatWorkerStatus(bundle);
            ActiveSummaryBlock.Text =
                $"{AdventureThreadRegistryService.FormatConnectionSummary(bundle)}\nActive play: {playActive} · Active design: {designActive}\n{workerActive} · {workerCaps}";
        }
        finally
        {
            _refreshingTab = false;
        }
    }

    private void RefreshUtilityWorkerTab(AdventureBundle bundle)
    {
        if (_utilityWorkerTab is null)
            return;

        var status = UtilityWorkerSetupService.Evaluate(bundle);
        _utilityWorkerTab.StepProjectBlock.Text = status.StepProject;
        _utilityWorkerTab.StepWorkerBlock.Text = status.StepWorkerChat;
        _utilityWorkerTab.StepVerifiedBlock.Text = status.StepVerified;
        _utilityWorkerTab.DetailBlock.Text = $"{status.Detail}\n{status.CapabilityDetail}";

        ApplyUtilityStepStyle(_utilityWorkerTab.StepProjectBlock, status.ProjectLinked);
        ApplyUtilityStepStyle(_utilityWorkerTab.StepWorkerBlock, status.WorkerPinned);
        ApplyUtilityStepStyle(
            _utilityWorkerTab.StepVerifiedBlock,
            status.ConnectionGreen,
            warning: status.WorkerPinned && status.HostReady && !status.ConnectionGreen,
            failed: status.ProbeError is { Length: > 0 } && !status.ConnectionGreen);

        ApplyUtilityStatusBanner(status);

        _utilityWorkerTab.SetupButton.IsEnabled = status.CanSetup;
        _utilityWorkerTab.UseCurrentTabButton.IsEnabled = status.CanUseCurrentTab;
        _utilityWorkerTab.VerifyButton.IsEnabled = status.CanVerify;
        _utilityWorkerTab.OpenWorkerButton.IsEnabled = status.CanOpenWorker;
        _utilityWorkerTab.ReplaceWorkerButton.IsEnabled = status.CanSetup && status.WorkerPinned;
    }

    private void ApplyUtilityStatusBanner(UtilityWorkerSetupStatus status)
    {
        if (_utilityWorkerTab?.StatusBanner is null || _utilityWorkerTab.StatusBannerText is null)
            return;

        _utilityWorkerTab.StatusBannerText.Text = status.ConnectionBannerText;
        _utilityWorkerTab.StatusBanner.Visibility = status.ProjectLinked
            ? Visibility.Visible
            : Visibility.Collapsed;

        Brush background;
        Brush border;
        Brush foreground;
        switch (status.ConnectionBannerState)
        {
            case UtilityConnectionBannerState.Ready:
                background = (Brush)FindResource("SuccessSubtleBrush");
                border = (Brush)FindResource("SuccessBrush");
                foreground = (Brush)FindResource("SuccessBrush");
                break;
            case UtilityConnectionBannerState.Error:
                background = (Brush)FindResource("WarningSubtleBrush");
                border = (Brush)FindResource("ErrorBrush");
                foreground = (Brush)FindResource("ErrorBrush");
                break;
            default:
                background = (Brush)FindResource("AccentSubtleBrush");
                border = (Brush)FindResource("AccentPrimaryBrush");
                foreground = (Brush)FindResource("TextPrimaryBrush");
                break;
        }

        _utilityWorkerTab.StatusBanner.Background = background;
        _utilityWorkerTab.StatusBanner.BorderBrush = border;
        _utilityWorkerTab.StatusBannerText.Foreground = foreground;
    }

    private void ApplyUtilityStepStyle(
        TextBlock block,
        bool complete,
        bool warning = false,
        bool failed = false)
    {
        if (complete)
        {
            block.Foreground = (Brush)FindResource("SuccessBrush");
            block.FontWeight = FontWeights.SemiBold;
            return;
        }

        if (failed)
        {
            block.Foreground = (Brush)FindResource("ErrorBrush");
            block.FontWeight = FontWeights.SemiBold;
            return;
        }

        if (warning)
        {
            block.Foreground = (Brush)FindResource("WarningBrush");
            block.FontWeight = FontWeights.SemiBold;
            return;
        }

        block.Foreground = (Brush)FindResource("TextPrimaryBrush");
        block.FontWeight = FontWeights.SemiBold;
    }

    private void WireGridSelection(DataGrid grid, AdventureThreadKind kind)
    {
        grid.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { IsEnabled: true } row)
                return;

            grid.SelectedItem = row.Item;
            row.IsSelected = true;
            if (row.Item is ThreadManagerRow threadRow)
                _selectedEntryId = threadRow.EntryId;

            UpdateRowActions(_tabPanels[kind]);
        };

        grid.SelectionChanged += (_, _) =>
        {
            if (GetSelectedRow(grid) is { } row)
                _selectedEntryId = row.EntryId;
            UpdateRowActions(_tabPanels[kind]);
        };

        grid.MouseDoubleClick += async (_, _) =>
        {
            if (GetSelectedRow(grid) is not { } row || row.IsArchived)
                return;

            if (row.IsActive)
                await RunRowActionAsync(r => OpenAsync(kind, r), row);
            else
                await RunRowActionAsync(r => SetActiveAsync(kind, r), row);
        };
    }

    private void SelectInitialRow(ThreadTabContent panel, IReadOnlyList<ThreadManagerRow> rows)
    {
        if (rows.Count == 0)
        {
            panel.Grid.SelectedItem = null;
            UpdateRowActions(panel);
            return;
        }

        ThreadManagerRow? match = null;
        if (_selectedEntryId is { } id)
            match = rows.FirstOrDefault(r => r.EntryId == id);

        if (match is null && panel.Grid.SelectedItem is ThreadManagerRow current)
            match = rows.FirstOrDefault(r => r.EntryId == current.EntryId);

        match ??= rows.FirstOrDefault(r => r.IsActive) ?? rows[0];
        if (!ReferenceEquals(panel.Grid.SelectedItem, match))
            panel.Grid.SelectedItem = match;
        panel.Grid.ScrollIntoView(match);
        _selectedEntryId = match.EntryId;
        UpdateRowActions(panel);
    }

    private static void UpdateRowActions(ThreadTabContent panel)
    {
        var row = panel.Grid.SelectedItem as ThreadManagerRow;
        var hasRow = row is not null && row.IsArchived == false;
        var hasArchivedRow = row is not null && row.IsArchived;
        panel.NewSlotButton.IsEnabled = true;
        panel.PinCurrentButton.IsEnabled = hasRow;
        panel.PickTabButton.IsEnabled = hasRow;
        panel.ClearPinButton.IsEnabled = hasRow && row!.HasPin;
        panel.SetActiveButton.IsEnabled = hasRow && row!.IsActive == false;
        panel.ArchiveButton.IsEnabled = hasRow && row!.IsActive == false;
        panel.RemoveButton.IsEnabled = hasArchivedRow && row!.IsActive == false;
        panel.OpenButton.IsEnabled = row is not null;
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T match)
                return match;

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private ThreadManagerRow? GetSelectedRow(DataGrid grid) =>
        grid.SelectedItem as ThreadManagerRow;

    private void RunRowAction(DataGrid grid, Func<ThreadManagerRow, Task> action)
    {
        if (GetSelectedRow(grid) is not { } row)
        {
            MessageBox.Show(this, "Select a thread row first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        _ = RunRowActionAsync(action, row);
    }

    private async Task RunRowActionAsync(Func<ThreadManagerRow, Task> action, ThreadManagerRow row)
    {
        try
        {
            await action(row);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task SetActiveAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        if (row.IsArchived)
        {
            MessageBox.Show(this, "Archived threads cannot be set active.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (row.IsActive)
            return;

        await _actions.ActivateEntryAsync(kind, row.EntryId);
        _changed = true;
        RefreshCurrentTab();
    }

    private Task ArchiveAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        if (row.IsActive)
        {
            MessageBox.Show(
                this,
                "Switch to a different active thread before archiving this one.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        if (row.IsArchived)
            return Task.CompletedTask;

        if (MessageBox.Show(
                this,
                $"Archive \"{row.Label}\"? The conversation id is kept for reference but will no longer be active.",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return Task.CompletedTask;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.ArchiveEntry(bundle, row.EntryId);
        AdventureThreadRegistryService.Persist(bundle);
        _changed = true;
        RefreshCurrentTab();
        return Task.CompletedTask;
    }

    private async Task PinTabToSelectedAsync(AdventureThreadKind kind, ThreadManagerRow row, bool usePicker)
    {
        if (row.IsArchived)
        {
            MessageBox.Show(this, "Select an active thread row to pin a browser tab.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        await _actions.PinTabToEntryAsync(kind, row.EntryId, usePicker);
        _changed = true;
        RefreshCurrentTab();
    }

    private async Task ClearPinAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        if (row.IsArchived)
            return;

        if (!row.HasPin)
            return;

        if (MessageBox.Show(
                this,
                ThreadManagerCopy.ClearPinConfirmBody(row.Label),
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        await _actions.ClearEntryPinAsync(kind, row.EntryId);
        _changed = true;
        RefreshCurrentTab();
    }

    private Task RemoveAsync(ThreadManagerRow row)
    {
        if (!row.IsArchived || row.IsActive)
        {
            MessageBox.Show(
                this,
                "Only archived threads can be removed. Archive the thread first, or switch active to another row.",
                Title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return Task.CompletedTask;
        }

        if (MessageBox.Show(
                this,
                ThreadManagerCopy.RemoveConfirmBody(row.Label),
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        return RemoveEntryConfirmedAsync(row);
    }

    private async Task RemoveEntryConfirmedAsync(ThreadManagerRow row)
    {
        await _actions.RemoveEntryAsync(row.EntryId);
        if (_selectedEntryId == row.EntryId)
            _selectedEntryId = null;

        _changed = true;
        RefreshCurrentTab();
    }

    private async Task OpenAsync(AdventureThreadKind kind, ThreadManagerRow row)
    {
        await _actions.OpenEntryAsync(kind, row.EntryId);
        _changed = true;
        RefreshCurrentTab();
    }

    private async Task StartNewDesignThreadAsync(AdventureThreadKind kind)
    {
        if (kind != AdventureThreadKind.Design)
            return;

        await _actions.StartNewDesignThreadAsync();
        _changed = true;
        RefreshCurrentTab();
    }

    private void SaveLabel(ThreadManagerRow row)
    {
        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.UpdateEntryLabel(bundle, row.EntryId, row.Label);
        AdventureThreadRegistryService.Persist(bundle);
        _changed = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (_changed)
            DialogResult = true;

        Close();
    }

    private sealed class ThreadTabContent
    {
        public required DataGrid Grid { get; init; }

        public required Panel Root { get; init; }

        public required Button NewSlotButton { get; init; }

        public required Button PinCurrentButton { get; init; }

        public required Button PickTabButton { get; init; }

        public required Button ClearPinButton { get; init; }

        public required Button SetActiveButton { get; init; }

        public required Button ArchiveButton { get; init; }

        public required Button RemoveButton { get; init; }

        public required Button OpenButton { get; init; }

        public required CheckBox ShowArchivedCheck { get; init; }
    }

    private sealed class UtilityWorkerTabContent
    {
        public required FrameworkElement Root { get; init; }

        public required Border StatusBanner { get; init; }

        public required TextBlock StatusBannerText { get; init; }

        public required TextBlock StepProjectBlock { get; init; }

        public required TextBlock StepWorkerBlock { get; init; }

        public required TextBlock StepVerifiedBlock { get; init; }

        public required TextBlock DetailBlock { get; init; }

        public required Button SetupButton { get; init; }

        public required Button UseCurrentTabButton { get; init; }

        public required Button VerifyButton { get; init; }

        public required Button OpenWorkerButton { get; init; }

        public required Button ReplaceWorkerButton { get; init; }
    }

    private sealed class ThreadManagerRow
    {
        public Guid EntryId { get; init; }

        public string Label { get; set; } = "";

        public string StatusDisplay { get; init; } = "";

        public string ConversationDisplay { get; init; } = "";

        public string TabTitleDisplay { get; init; } = "";

        public bool IsActive { get; init; }

        public bool IsArchived { get; init; }

        public bool HasPin { get; init; }

        public static ThreadManagerRow FromEntry(AdventureBundle bundle, AdventureThreadEntry entry)
        {
            var isActive = AdventureThreadRegistryService.IsActiveEntry(bundle, entry.Id);
            var conversation = string.IsNullOrWhiteSpace(entry.ConversationId)
                ? "—"
                : entry.ConversationId.Length > 14
                    ? entry.ConversationId[..14] + "…"
                    : entry.ConversationId;

            return new ThreadManagerRow
            {
                EntryId = entry.Id,
                Label = entry.Label,
                StatusDisplay = isActive
                    ? "Active"
                    : entry.Status == AdventureThreadStatus.Archived
                        ? "Archived"
                        : "Inactive",
                ConversationDisplay = conversation,
                TabTitleDisplay = string.IsNullOrWhiteSpace(entry.PinnedTabTitle) ? "—" : entry.PinnedTabTitle,
                IsActive = isActive,
                IsArchived = entry.Status == AdventureThreadStatus.Archived,
                HasPin = AdventureThreadRegistryService.EntryHasPin(entry),
            };
        }
    }
}

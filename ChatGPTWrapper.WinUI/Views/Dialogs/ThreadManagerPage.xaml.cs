using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Views;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class ThreadManagerPage : UserControl
{
    private readonly Guid _adventureId;
    private readonly AdventureThreadManagerActions _actions;
    private readonly Dictionary<AdventureThreadKind, ThreadKindTabHost> _tabHosts = new();
    private UtilityWorkerTabHost? _utilityTab;
    private bool _suppressDeliverySettings;
    private bool _showArchived = true;
    private Guid? _selectedEntryId;
    private bool _refreshingTab;

    public ThreadManagerPage(Guid adventureId, AdventureThreadManagerActions actions, AdventureThreadKind? initialKind = null)
    {
        _adventureId = adventureId;
        _actions = actions;
        _initialKind = initialKind;
        InitializeComponent();
        Loaded += (_, _) => InitializeTabs();
    }

    public bool Changed { get; private set; }

    private readonly AdventureThreadKind? _initialKind;

    public void RefreshAll() => RefreshCurrentTab();

    private void InitializeTabs()
    {
        var playHost = BuildKindTab(AdventureThreadKind.Play, labelEditable: false);
        SetTabContent(AdventureThreadKind.Play, playHost.Root);
        var designHost = BuildKindTab(AdventureThreadKind.Design, labelEditable: true);
        SetTabContent(AdventureThreadKind.Design, designHost.Root);
        _utilityTab = BuildUtilityWorkerTab();
        SetTabContent(AdventureThreadKind.UtilityWorker, _utilityTab.Root);

        LoadProjectSummary();
        LoadDeliverySettings();
        SelectInitialTab();
        RefreshCurrentTab();
    }

    private void SelectInitialTab()
    {
        if (_initialKind is not { } kind)
            return;

        for (var i = 0; i < KindTabs.TabItems.Count; i++)
        {
            if (KindTabs.TabItems[i] is TabViewItem tab
                && string.Equals(tab.Tag as string, kind.ToString(), StringComparison.Ordinal))
            {
                KindTabs.SelectedIndex = i;
                return;
            }
        }
    }

    private void SetTabContent(AdventureThreadKind kind, UIElement content)
    {
        foreach (var item in KindTabs.TabItems)
        {
            if (item is TabViewItem tab && string.Equals(tab.Tag as string, kind.ToString(), StringComparison.Ordinal))
            {
                tab.Content = content;
                return;
            }
        }
    }

    private ThreadKindTabHost BuildKindTab(AdventureThreadKind kind, bool labelEditable)
    {
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MinHeight = 220,
            ItemTemplate = (DataTemplate)Resources["ThreadRowTemplate"],
        };
        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ThreadManagerRowModel row)
            {
                _selectedEntryId = row.EntryId;
                UpdateRowActions(_tabHosts[kind]);
            }
        };

        list.DoubleTapped += async (_, _) =>
        {
            if (list.SelectedItem is not ThreadManagerRowModel row || row.IsArchived)
                return;

            if (row.IsActive)
                await RunRowActionAsync(r => OpenAsync(kind, r), row);
            else
                await RunRowActionAsync(r => SetActiveAsync(kind, r), row);
        };

        var actions = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 },
        };
        var actionPanel = (StackPanel)actions.Content!;

        var newSlot = CreateActionButton(ThreadManagerCopy.NewThreadSlotButton, async () =>
        {
            var entryId = await _actions.CreateThreadSlotAsync(kind);
            if (entryId is { } id)
            {
                _selectedEntryId = id;
                Changed = true;
                RefreshCurrentTab();
            }
        });

        var pinCurrent = CreateActionButton(ThreadManagerCopy.PinCurrentToSelectedButton, () =>
            RunSelectedRowAction(kind, row => PinTabToSelectedAsync(kind, row, usePicker: false)));

        var pickTab = CreateActionButton(ThreadManagerCopy.PickTabToPinButton, () =>
            RunSelectedRowAction(kind, row => PinTabToSelectedAsync(kind, row, usePicker: true)));

        var clearPin = CreateActionButton(ThreadManagerCopy.ClearPinButton, () =>
            RunSelectedRowAction(kind, ClearPinAsync));

        var setActive = CreateActionButton("Set active", () =>
            RunSelectedRowAction(kind, (k, row) => SetActiveAsync(k, row)));

        var archive = CreateActionButton("Archive", () =>
            RunSelectedRowAction(kind, ArchiveAsync));

        var remove = CreateActionButton(ThreadManagerCopy.RemoveButton, () =>
            RunSelectedRowAction(kind, RemoveAsync));

        var open = CreateActionButton("Open in browser", () =>
            RunSelectedRowAction(kind, OpenAsync));

        var newNarrative = CreateActionButton(
            kind == AdventureThreadKind.Design
                ? ThreadManagerCopy.StartNewDesignThreadButton
                : PlayThreadRotationCopy.NarrativeFromSourcesButton,
            async () =>
            {
                if (kind == AdventureThreadKind.Play)
                    await _actions.StartNarrativeFromSourcesAsync();
                else if (kind == AdventureThreadKind.Design)
                    await StartNewDesignThreadAsync();

                Changed = true;
                RefreshCurrentTab();
            });

        var handOff = CreateActionButton(PlayThreadRotationCopy.HandoffToNewChatButton, async () =>
        {
            await _actions.OpenPlayHandoffWizardAsync();
            Changed = true;
            RefreshCurrentTab();
        });
        handOff.Visibility = kind == AdventureThreadKind.Play ? Visibility.Visible : Visibility.Collapsed;

        var showArchived = new CheckBox
        {
            Content = ThreadManagerCopy.ShowArchivedCheck,
            IsChecked = _showArchived,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };
        showArchived.Checked += (_, _) => { _showArchived = true; RefreshCurrentTab(); };
        showArchived.Unchecked += (_, _) => { _showArchived = false; RefreshCurrentTab(); };

        var rename = CreateActionButton("Rename…", () =>
            RunSelectedRowAction(kind, RenameLabelAsync));
        rename.Visibility = labelEditable ? Visibility.Visible : Visibility.Collapsed;

        actionPanel.Children.Add(newSlot);
        if (labelEditable)
            actionPanel.Children.Add(rename);
        actionPanel.Children.Add(pinCurrent);
        actionPanel.Children.Add(pickTab);
        actionPanel.Children.Add(clearPin);
        actionPanel.Children.Add(setActive);
        actionPanel.Children.Add(archive);
        actionPanel.Children.Add(remove);
        actionPanel.Children.Add(open);
        actionPanel.Children.Add(newNarrative);
        if (kind == AdventureThreadKind.Play)
            actionPanel.Children.Add(handOff);
        actionPanel.Children.Add(showArchived);

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(list);
        root.Children.Add(actions);

        var host = new ThreadKindTabHost
        {
            Root = root,
            List = list,
            NewSlotButton = newSlot,
            PinCurrentButton = pinCurrent,
            PickTabButton = pickTab,
            ClearPinButton = clearPin,
            SetActiveButton = setActive,
            ArchiveButton = archive,
            RemoveButton = remove,
            OpenButton = open,
            ShowArchivedCheck = showArchived,
            LabelEditable = labelEditable,
            Kind = kind,
        };
        _tabHosts[kind] = host;
        return host;
    }

    private UtilityWorkerTabHost BuildUtilityWorkerTab()
    {
        var intro = new TextBlock
        {
            Text = UtilityWorkerSetupCopy.TabIntro,
            TextWrapping = TextWrapping.Wrap,
            Style = GetTextStyle("ShellSectionHintStyle"),
        };

        var statusBanner = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
        var statusBannerText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        statusBanner.Child = statusBannerText;

        var stepProject = CreateStepBlock();
        var stepWorker = CreateStepBlock();
        var stepVerified = CreateStepBlock();
        var detail = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = GetTextStyle("ShellSectionHintStyle") };

        var setup = CreateActionButton("Set up utility worker…", async () =>
        {
            await _actions.SetupUtilityWorkerAsync();
            Changed = true;
            RefreshCurrentTab();
        });
        var useCurrent = CreateActionButton("Use current browser tab", async () =>
        {
            await _actions.PinUtilityWorkerFromCurrentTabAsync();
            Changed = true;
            RefreshCurrentTab();
        });
        var verify = CreateActionButton("Verify connection", async () =>
        {
            await _actions.ProbeUtilityWorkerAsync();
            Changed = true;
            RefreshCurrentTab();
        });
        var openWorker = CreateActionButton("Open worker chat", async () =>
        {
            await _actions.OpenUtilityWorkerAsync();
            Changed = true;
            RefreshCurrentTab();
        });
        var replace = CreateActionButton("Replace worker chat…", async () =>
        {
            await _actions.SetupUtilityWorkerReplaceAsync(true);
            Changed = true;
            RefreshCurrentTab();
        });

        var workerActions = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 },
        };
        var workerActionPanel = (StackPanel)workerActions.Content!;
        workerActionPanel.Children.Add(setup);
        workerActionPanel.Children.Add(useCurrent);
        workerActionPanel.Children.Add(verify);
        workerActionPanel.Children.Add(openWorker);
        workerActionPanel.Children.Add(replace);

        var root = new StackPanel { Spacing = 12 };
        root.Children.Add(intro);
        root.Children.Add(statusBanner);
        root.Children.Add(stepProject);
        root.Children.Add(stepWorker);
        root.Children.Add(stepVerified);
        root.Children.Add(detail);
        root.Children.Add(workerActions);

        return new UtilityWorkerTabHost
        {
            Root = root,
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
    }

    private static TextBlock CreateStepBlock() =>
        new() { TextWrapping = TextWrapping.Wrap, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

    private static Button CreateActionButton(string label, Func<Task> action)
    {
        var button = new Button
        {
            Content = label,
            Style = GetButtonStyle("ShellGhostButtonStyle"),
            Margin = new Thickness(0, 0, 6, 4),
        };
        button.Click += async (_, _) => await action();
        return button;
    }

    private static Button CreateActionButton(string label, Action action) =>
        CreateActionButton(label, () =>
        {
            action();
            return Task.CompletedTask;
        });

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
            ProjectSummaryBlock.Text =
                "No ChatGPT Project linked — use Link project… to bind this adventure to an existing Project.";
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
        Changed = true;
    }

    private async void ChangeProject_Click(object sender, RoutedEventArgs e)
    {
        await _actions.OpenProjectWorkspaceAsync();
        LoadProjectSummary();
        RefreshCurrentTab();
        Changed = true;
    }

    private void KindTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        RefreshCurrentTab();

    private void RefreshCurrentTab()
    {
        if (_refreshingTab)
            return;

        if (KindTabs.SelectedItem is not TabViewItem tab
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
            else if (_tabHosts.TryGetValue(kind, out var panel))
            {
                if (panel.ShowArchivedCheck.IsChecked != _showArchived)
                    panel.ShowArchivedCheck.IsChecked = _showArchived;

                var rows = AdventureThreadRegistryService.ListEntries(bundle, kind, includeArchived: _showArchived)
                    .Select(entry => ThreadManagerRowModel.FromEntry(bundle, entry))
                    .ToList();
                panel.List.ItemsSource = rows;
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
        if (_utilityTab is null)
            return;

        var status = UtilityWorkerSetupService.Evaluate(bundle);
        _utilityTab.StepProjectBlock.Text = status.StepProject;
        _utilityTab.StepWorkerBlock.Text = status.StepWorkerChat;
        _utilityTab.StepVerifiedBlock.Text = status.StepVerified;
        _utilityTab.DetailBlock.Text = $"{status.Detail}\n{status.CapabilityDetail}";

        ApplyUtilityStepStyle(_utilityTab.StepProjectBlock, status.ProjectLinked);
        ApplyUtilityStepStyle(_utilityTab.StepWorkerBlock, status.WorkerPinned);
        ApplyUtilityStepStyle(
            _utilityTab.StepVerifiedBlock,
            status.ConnectionGreen,
            warning: status.WorkerPinned && status.HostReady && !status.ConnectionGreen,
            failed: status.ProbeError is { Length: > 0 } && !status.ConnectionGreen);

        _utilityTab.StatusBannerText.Text = status.ConnectionBannerText;
        _utilityTab.StatusBanner.Visibility = status.ProjectLinked ? Visibility.Visible : Visibility.Collapsed;

        _utilityTab.SetupButton.IsEnabled = status.CanSetup;
        _utilityTab.UseCurrentTabButton.IsEnabled = status.CanUseCurrentTab;
        _utilityTab.VerifyButton.IsEnabled = status.CanVerify;
        _utilityTab.OpenWorkerButton.IsEnabled = status.CanOpenWorker;
        _utilityTab.ReplaceWorkerButton.IsEnabled = status.CanSetup && status.WorkerPinned;
    }

    private void ApplyUtilityStepStyle(TextBlock block, bool complete, bool warning = false, bool failed = false)
    {
        if (complete)
        {
            block.Foreground = GetBrush("SuccessBrush");
            return;
        }

        if (failed)
        {
            block.Foreground = GetBrush("ErrorBrush");
            return;
        }

        if (warning)
        {
            block.Foreground = GetBrush("WarningBrush");
            return;
        }

        block.Foreground = GetBrush("TextPrimaryBrush");
    }

    private void SelectInitialRow(ThreadKindTabHost panel, IReadOnlyList<ThreadManagerRowModel> rows)
    {
        if (rows.Count == 0)
        {
            panel.List.SelectedItem = null;
            UpdateRowActions(panel);
            return;
        }

        ThreadManagerRowModel? match = null;
        if (_selectedEntryId is { } id)
            match = rows.FirstOrDefault(r => r.EntryId == id);

        match ??= rows.FirstOrDefault(r => r.IsActive) ?? rows[0];
        panel.List.SelectedItem = match;
        _selectedEntryId = match.EntryId;
        UpdateRowActions(panel);
    }

    private static void UpdateRowActions(ThreadKindTabHost panel)
    {
        var row = panel.List.SelectedItem as ThreadManagerRowModel;
        var hasRow = row is not null && !row.IsArchived;
        var hasArchivedRow = row is not null && row.IsArchived;
        panel.NewSlotButton.IsEnabled = true;
        panel.PinCurrentButton.IsEnabled = hasRow;
        panel.PickTabButton.IsEnabled = hasRow;
        panel.ClearPinButton.IsEnabled = hasRow && row!.HasPin;
        panel.SetActiveButton.IsEnabled = hasRow && !row!.IsActive;
        panel.ArchiveButton.IsEnabled = hasRow && !row!.IsActive;
        panel.RemoveButton.IsEnabled = hasArchivedRow && row!.IsActive == false;
        panel.OpenButton.IsEnabled = row is not null;
    }

    private void RunSelectedRowAction(
        AdventureThreadKind kind,
        Func<AdventureThreadKind, ThreadManagerRowModel, Task> action)
    {
        if (_tabHosts[kind].List.SelectedItem is not ThreadManagerRowModel row)
        {
            _ = WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, ThreadManagerCopy.DialogTitle, "Select a thread row first.");
            return;
        }

        _ = RunRowActionAsync(r => action(kind, r), row);
    }

    private void RunSelectedRowAction(AdventureThreadKind kind, Func<ThreadManagerRowModel, Task> action) =>
        RunSelectedRowAction(kind, (_, row) => action(row));

    private async Task RunRowActionAsync(Func<ThreadManagerRowModel, Task> action, ThreadManagerRowModel row)
    {
        try
        {
            await action(row);
        }
        catch (Exception ex)
        {
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, ThreadManagerCopy.DialogTitle, ex.Message);
        }
    }

    private async Task SetActiveAsync(AdventureThreadKind kind, ThreadManagerRowModel row)
    {
        if (row.IsArchived)
        {
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, ThreadManagerCopy.DialogTitle, "Archived threads cannot be set active.");
            return;
        }

        if (row.IsActive)
            return;

        await _actions.ActivateEntryAsync(kind, row.EntryId);
        Changed = true;
        RefreshCurrentTab();
    }

    private async Task ArchiveAsync(AdventureThreadKind kind, ThreadManagerRowModel row)
    {
        if (row.IsActive)
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                ThreadManagerCopy.DialogTitle,
                "Switch to a different active thread before archiving this one.");
            return;
        }

        if (row.IsArchived)
            return;

        if (!await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                ThreadManagerCopy.DialogTitle,
                $"Archive \"{row.Label}\"? The conversation id is kept for reference but will no longer be active.",
                confirmText: "Archive"))
        {
            return;
        }

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.EnsureMigrated(bundle);
        AdventureThreadRegistryService.ArchiveEntry(bundle, row.EntryId);
        AdventureThreadRegistryService.Persist(bundle);
        Changed = true;
        RefreshCurrentTab();
    }

    private async Task PinTabToSelectedAsync(AdventureThreadKind kind, ThreadManagerRowModel row, bool usePicker)
    {
        if (row.IsArchived)
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                ThreadManagerCopy.DialogTitle,
                "Select an active thread row to pin a browser tab.");
            return;
        }

        await _actions.PinTabToEntryAsync(kind, row.EntryId, usePicker);
        Changed = true;
        RefreshCurrentTab();
    }

    private async Task ClearPinAsync(ThreadManagerRowModel row)
    {
        if (row.IsArchived || !row.HasPin)
            return;

        if (!await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                ThreadManagerCopy.DialogTitle,
                ThreadManagerCopy.ClearPinConfirmBody(row.Label),
                confirmText: "Clear pin"))
        {
            return;
        }

        var kind = KindTabs.SelectedItem is TabViewItem { Tag: string tag }
                   && Enum.TryParse<AdventureThreadKind>(tag, out var parsed)
            ? parsed
            : AdventureThreadKind.Play;

        await _actions.ClearEntryPinAsync(kind, row.EntryId);
        Changed = true;
        RefreshCurrentTab();
    }

    private async Task RemoveAsync(ThreadManagerRowModel row)
    {
        if (!row.IsArchived || row.IsActive)
        {
            await WinUiDialogHelper.ShowInfoAsync(
                App.CurrentMainWindow,
                ThreadManagerCopy.DialogTitle,
                "Only archived threads can be removed. Archive the thread first, or switch active to another row.");
            return;
        }

        if (!await WinUiDialogHelper.ConfirmAsync(
                App.CurrentMainWindow,
                ThreadManagerCopy.DialogTitle,
                ThreadManagerCopy.RemoveConfirmBody(row.Label),
                confirmText: "Remove"))
        {
            return;
        }

        await _actions.RemoveEntryAsync(row.EntryId);
        if (_selectedEntryId == row.EntryId)
            _selectedEntryId = null;

        Changed = true;
        RefreshCurrentTab();
    }

    private async Task OpenAsync(AdventureThreadKind kind, ThreadManagerRowModel row)
    {
        await _actions.OpenEntryAsync(kind, row.EntryId);
        Changed = true;
        RefreshCurrentTab();
    }

    private async Task RenameLabelAsync(AdventureThreadKind kind, ThreadManagerRowModel row)
    {
        var (success, newLabel) = await WinUiDialogHostService.PromptAsync(
            App.CurrentMainWindow,
            "Rename thread",
            "Label",
            row.Label,
            confirmButtonText: "Rename");
        if (!success || string.IsNullOrWhiteSpace(newLabel))
            return;

        var bundle = AdventureStore.Load(_adventureId);
        if (bundle is null)
            return;

        AdventureThreadRegistryService.UpdateEntryLabel(bundle, row.EntryId, newLabel.Trim());
        AdventureStore.Save(bundle);
        Changed = true;
        RefreshCurrentTab();
    }

    private async Task StartNewDesignThreadAsync()
    {
        await _actions.StartNewDesignThreadAsync();
        Changed = true;
        RefreshCurrentTab();
    }

    private static Microsoft.UI.Xaml.Style? GetTextStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Microsoft.UI.Xaml.Style style
            ? style
            : null;

    private static Microsoft.UI.Xaml.Style? GetButtonStyle(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Microsoft.UI.Xaml.Style style
            ? style
            : null;

    private static Brush GetBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);

    private sealed class ThreadKindTabHost
    {
        public required UIElement Root { get; init; }
        public required ListView List { get; init; }
        public required Button NewSlotButton { get; init; }
        public required Button PinCurrentButton { get; init; }
        public required Button PickTabButton { get; init; }
        public required Button ClearPinButton { get; init; }
        public required Button SetActiveButton { get; init; }
        public required Button ArchiveButton { get; init; }
        public required Button RemoveButton { get; init; }
        public required Button OpenButton { get; init; }
        public required CheckBox ShowArchivedCheck { get; init; }
        public bool LabelEditable { get; init; }
        public AdventureThreadKind Kind { get; init; }
    }

    private sealed class UtilityWorkerTabHost
    {
        public required UIElement Root { get; init; }
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
}

using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views;

public sealed partial class AdventureDesignPage : UserControl
{
    private readonly Dictionary<string, TextBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);
    private WinUiPlaySessionService? _session;
    private Guid _adventureId;
    private bool _suppressStepChange;
    private bool _suppressFieldChange;
    private TextBox? _freeformBox;
    private CheckBox? _bootstrapLoreCheck;
    private CheckBox? _startPlayCheck;
    private readonly PlayCompanionReferencePanel _castReferencePanel;

    public AdventureDesignPage()
    {
        InitializeComponent();
        _castReferencePanel = new PlayCompanionReferencePanel { Visibility = Visibility.Collapsed };
        EnsureStepComboItems();
    }

    public event EventHandler? LaunchPlayRequested;

    public event EventHandler? ManageThreadsRequested;

    public void Bind(WinUiPlaySessionService session)
    {
        _session = session;
        Cockpit.Bind(session);
        _castReferencePanel.Bind(session);
        session.StatusChanged += (_, _) => RefreshUi();
    }

    public async Task InitializeAsync(Guid adventureId)
    {
        _adventureId = adventureId;
        await (_session?.LoadAdventureAsync(adventureId) ?? Task.CompletedTask);
        EnsureDesignWorkspaceReady();
        RefreshUi();
    }

    private AdventureBundle? CurrentBundle => _session?.CurrentBundle;

    private AdventureDesignStep CurrentStep =>
        CurrentBundle?.DesignWorkspace.CurrentStep ?? AdventureDesignStep.Concept;

    private void EnsureStepComboItems()
    {
        if (StepCombo.Items.Count > 0)
            return;

        foreach (var step in AdventureDesignService.OrderedSteps.Where(s => s is not AdventureDesignStep.Setup))
        {
            StepCombo.Items.Add(new ComboBoxItem
            {
                Content = AdventureDesignService.GetStepDisplayName(step),
                Tag = step.ToString(),
            });
        }
    }

    private void EnsureDesignWorkspaceReady()
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        AdventureDesignService.EnsureWorkspace(bundle);
        AdventureDesignService.HydrateFromScenario(bundle);

        if (!AdventureSourceFileService.HasLocalLoreSourceFiles(bundle))
        {
            var recovered = AdventureSourceFileService.TryBootstrapLocalSourcesFromDesignWorkspace(bundle);
            if (recovered > 0)
                _session?.ReloadBundle(bundle.Metadata.Id);
        }

        if (bundle.DesignWorkspace.CurrentStep == AdventureDesignStep.Setup)
            AdventureDesignService.GoToStep(bundle, AdventureDesignStep.Concept);

        AdventureStore.Save(bundle, AdventureSaveScope.DesignWorkspace);
        _session?.ReloadBundle(bundle.Metadata.Id);
    }

    private void RefreshUi()
    {
        var bundle = CurrentBundle;
        TitleBlock.Text = bundle is null
            ? "Design session"
            : $"Design: {bundle.Metadata.Title}";

        SyncStepCombo();
        RebuildDraftPanel(CurrentStep);
        UpdateFooterButtons();
        Cockpit.ResyncFromStore();

        if (bundle is not null)
        {
            var pending = AdventureDesignService.GetOrCreateStep(bundle, CurrentStep).PendingProposals
                .Count(p => p.Status == DesignProposalStatus.Pending);
            AcceptProposalsButton.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
            AcceptProposalsButton.Content = pending > 0 ? $"Accept proposals ({pending})" : "Accept proposals";
        }
    }

    private void SyncStepCombo()
    {
        var step = CurrentStep;
        _suppressStepChange = true;
        try
        {
            for (var i = 0; i < StepCombo.Items.Count; i++)
            {
                if (StepCombo.Items[i] is ComboBoxItem item
                    && string.Equals(item.Tag as string, step.ToString(), StringComparison.Ordinal))
                {
                    StepCombo.SelectedIndex = i;
                    break;
                }
            }
        }
        finally
        {
            _suppressStepChange = false;
        }
    }

    private void RebuildDraftPanel(AdventureDesignStep step)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        _fieldBoxes.Clear();
        _freeformBox = null;
        _bootstrapLoreCheck = null;
        _startPlayCheck = null;

        while (DraftPanel.Children.Count > 0)
            DraftPanel.Children.RemoveAt(0);

        AppendSourcePromptSection(step);
        AppendFieldEditors(step);

        if (step == AdventureDesignStep.Cast)
        {
            _castReferencePanel.Visibility = Visibility.Visible;
            DraftPanel.Children.Add(CreateSectionHeader("Canon entities"));
            DraftPanel.Children.Add(_castReferencePanel);
            _castReferencePanel.RefreshEntities();
        }
        else
        {
            _castReferencePanel.Visibility = Visibility.Collapsed;
        }

        if (step is AdventureDesignStep.Cast or AdventureDesignStep.Lexicon or AdventureDesignStep.Sources)
            AppendFreeformEditor(step);

        if (step == AdventureDesignStep.Review)
            AppendReviewSection(bundle);

        if (step == AdventureDesignStep.Sources || step == AdventureDesignStep.Instructions)
        {
            DraftPanel.Children.Add(CreateActionButton(
                "Open source manager",
                async (_, _) =>
                {
                    await WinUiDialogHostService.ShowSourceManagerAsync(App.CurrentMainWindow, bundle.Metadata.Id);
                    _session?.ReloadBundle(bundle.Metadata.Id);
                    RefreshUi();
                }));
        }
    }

    private void AppendSourcePromptSection(AdventureDesignStep step)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        var prompts = AdventureDesignSourcePromptService.ForDesignStepInPipelineOrder(step).ToList();
        if (prompts.Count == 0)
            return;

        DraftPanel.Children.Add(CreateSectionHeader(
            step == AdventureDesignStep.Sources ? "Project source file prompts" : "Source file prompt"));

        foreach (var def in prompts)
        {
            var sent = AdventureDesignService.IsSourceFilePromptSent(bundle, def.RelativePath);
            var label = sent ? $"Draft {def.RelativePath} (sent)" : $"Draft {def.RelativePath}";
            DraftPanel.Children.Add(CreateActionButton(label, async (_, _) => await SendSourcePromptAsync(def.RelativePath)));
        }
    }

    private void AppendFieldEditors(AdventureDesignStep step)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        foreach (var field in AdventureDesignService.GetFieldDefinitions(step))
        {
            DraftPanel.Children.Add(CreateSectionHeader(field.Label));
            var box = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = field.Key is "castNotes" or "sourceOutline" or "lexiconRules" or "lexiconPools" or "lexiconAvoid"
                    or InstructionContractService.GlobalBoundariesFieldKey
                    or InstructionContractService.CharacterPortrayalFieldKey
                    or InstructionContractService.InstructionAddendumFieldKey
                    ? 120
                    : 56,
                Tag = field.Key,
                Text = AdventureDesignService.GetField(bundle, step, field.Key) ?? "",
            };
            box.TextChanged += FieldBox_TextChanged;
            _fieldBoxes[field.Key] = box;
            DraftPanel.Children.Add(box);
        }
    }

    private void AppendFreeformEditor(AdventureDesignStep step)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        DraftPanel.Children.Add(CreateSectionHeader("Additional notes"));
        _freeformBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 80,
            Text = AdventureDesignService.GetFreeform(bundle, step),
        };
        _freeformBox.TextChanged += (_, _) =>
        {
            if (_suppressFieldChange || CurrentBundle is not { } b)
                return;

            AdventureDesignService.SetFreeform(b, step, _freeformBox.Text);
            SaveDesignWorkspace();
        };
        DraftPanel.Children.Add(_freeformBox);
    }

    private void AppendReviewSection(AdventureBundle bundle)
    {
        DraftPanel.Children.Add(CreateSectionHeader("Review summary"));
        DraftPanel.Children.Add(new TextBlock
        {
            Text = AdventureDesignFinalizeService.BuildReviewSummary(bundle),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["ShellSectionHintStyle"],
        });

        _bootstrapLoreCheck = new CheckBox
        {
            Content = "Bootstrap story cards on launch",
            IsChecked = bundle.DesignWorkspace.LaunchBootstrapLore,
        };
        _startPlayCheck = new CheckBox
        {
            Content = "Start play after launch",
            IsChecked = bundle.DesignWorkspace.LaunchStartPlay,
        };
        DraftPanel.Children.Add(_bootstrapLoreCheck);
        DraftPanel.Children.Add(_startPlayCheck);
    }

    private static TextBlock CreateSectionHeader(string text) =>
        new()
        {
            Text = text,
            Style = (Style)Application.Current.Resources["ShellSectionHeaderStyle"],
        };

    private Button CreateActionButton(string label, RoutedEventHandler click)
    {
        var button = new Button
        {
            Content = label,
            Style = (Style)Application.Current.Resources["ShellGhostButtonStyle"],
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += click;
        return button;
    }

    private void UpdateFooterButtons()
    {
        var step = CurrentStep;
        BackStepButton.IsEnabled = step != AdventureDesignStep.Concept;
        ContinueButton.IsEnabled = step != AdventureDesignStep.Review;
        ContinueButton.Visibility = step == AdventureDesignStep.Review ? Visibility.Collapsed : Visibility.Visible;
        LaunchButton.Visibility = step == AdventureDesignStep.Review ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FieldBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFieldChange || sender is not TextBox box || box.Tag is not string key)
            return;

        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        AdventureDesignService.SetField(bundle, CurrentStep, key, box.Text);
        SaveDesignWorkspace();
    }

    private void PersistFieldsFromUi()
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        foreach (var (key, box) in _fieldBoxes)
            AdventureDesignService.SetField(bundle, CurrentStep, key, box.Text);

        if (_freeformBox is not null)
            AdventureDesignService.SetFreeform(bundle, CurrentStep, _freeformBox.Text);

        if (_bootstrapLoreCheck is not null)
            bundle.DesignWorkspace.LaunchBootstrapLore = _bootstrapLoreCheck.IsChecked == true;

        if (_startPlayCheck is not null)
            bundle.DesignWorkspace.LaunchStartPlay = _startPlayCheck.IsChecked == true;
    }

    private void SaveDesignWorkspace()
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        AdventureStore.Save(bundle, AdventureSaveScope.DesignWorkspace);
        _session?.NotifyStatusChanged();
    }

    private void StepCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressStepChange || StepCombo.SelectedItem is not ComboBoxItem item
            || item.Tag is not string tag
            || !Enum.TryParse<AdventureDesignStep>(tag, out var step)
            || CurrentBundle is not { } bundle)
        {
            return;
        }

        PersistFieldsFromUi();
        AdventureDesignService.GoToStep(bundle, step);
        SaveDesignWorkspace();
        RefreshUi();
    }

    private void BackStep_Click(object sender, RoutedEventArgs e)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        PersistFieldsFromUi();
        if (AdventureDesignService.TryRetreatStep(bundle, out _))
        {
            SaveDesignWorkspace();
            RefreshUi();
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        PersistFieldsFromUi();
        AdventureDesignService.MarkStepAccepted(bundle, CurrentStep);
        if (AdventureDesignService.TryAdvanceStep(bundle, out _))
        {
            SaveDesignWorkspace();
            RefreshUi();
        }
    }

    private void Launch_Click(object sender, RoutedEventArgs e)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        PersistFieldsFromUi();
        SaveDesignWorkspace();

        LaunchButton.IsEnabled = false;
        try
        {
            var result = AdventureDesignFinalizeService.Finalize(bundle);
            if (!result.Success)
            {
                SetStatus(result.Error ?? "Launch failed.");
                return;
            }

            _session?.ReloadBundle(bundle.Metadata.Id);
            SetStatus("Adventure launched.");

            if (bundle.DesignWorkspace.LaunchStartPlay)
                LaunchPlayRequested?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            LaunchButton.IsEnabled = true;
            RefreshUi();
        }
    }

    private void AcceptProposals_Click(object sender, RoutedEventArgs e)
    {
        var bundle = CurrentBundle;
        if (bundle is null)
            return;

        var count = AdventureDesignService.AcceptAllPendingProposals(bundle, CurrentStep);
        SaveDesignWorkspace();
        _suppressFieldChange = true;
        RefreshUi();
        _suppressFieldChange = false;
        SetStatus(count > 0 ? $"Accepted {count} proposal(s)." : "No proposals to accept.");
    }

    private async Task SendSourcePromptAsync(string relativePath)
    {
        if (_session?.CurrentBundle is not { } bundle)
            return;

        SetStatus($"Sending prompt for {relativePath}…");
        var result = await WinUiDesignChatService.SendSourceFilePromptAsync(
            bundle.Metadata.Id,
            relativePath,
            _session);
        SetStatus(WinUiDesignChatService.FormatSendStatus(result, $"Prompt sent for {relativePath}."));
        RefreshUi();
    }

    private void OnManageThreadsRequested(object? sender, EventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    private async void OnLinkProjectRequested(object? sender, EventArgs e)
    {
        if (CurrentBundle is not { } bundle)
            return;

        await WinUiThreadManagerBridge.OpenProjectWorkspaceAsync(bundle.Metadata.Id);
        _session?.ReloadBundle(bundle.Metadata.Id);
        RefreshUi();
    }

    private void OnCockpitStatusChanged(object? sender, string message)
    {
        SetStatus(message);
        RefreshUi();
    }

    private async void ManageThreads_Click(object sender, RoutedEventArgs e) =>
        await WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, _adventureId);

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new MenuFlyout();

        flyout.Items.Add(MakeItem("Change project…", async (_, _) =>
        {
            if (CurrentBundle is { } bundle)
            {
                await WinUiThreadManagerBridge.OpenProjectWorkspaceAsync(bundle.Metadata.Id);
                _session?.ReloadBundle(bundle.Metadata.Id);
                RefreshUi();
            }
        }));

        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(MakeItem("Manage threads…", async (_, _) =>
            await WinUiDialogHostService.ShowThreadManagerAsync(App.CurrentMainWindow, _adventureId)));

        flyout.Items.Add(MakeItem("Pin current tab as design thread", (_, _) => PinDesignTab()));

        flyout.Items.Add(MakeItem("Open design wizard…", async (_, _) =>
        {
            await WpfDialogHostService.ShowDesignWizardAsync(App.CurrentMainWindow, _adventureId);
            _session?.ReloadBundle(_adventureId);
            EnsureDesignWorkspaceReady();
            RefreshUi();
        }));

        flyout.Items.Add(new MenuFlyoutSeparator());

        flyout.Items.Add(MakeItem("Open source manager…", async (_, _) =>
        {
            await WinUiDialogHostService.ShowSourceManagerAsync(App.CurrentMainWindow, _adventureId);
            _session?.ReloadBundle(_adventureId);
            RefreshUi();
        }));

        flyout.ShowAt(MoreButton);
    }

    private static MenuFlyoutItem MakeItem(string text, RoutedEventHandler click)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += click;
        return item;
    }

    private async void PinDesignTab()
    {
        var bundle = CurrentBundle;
        var host = WinUiShellHost.GetShellChatHost();
        if (bundle is null || host?.GetActiveWebView() is not { } webView)
        {
            SetStatus("Select a Project chat tab first, then pin it.");
            return;
        }

        try
        {
            var tab = host.FindTabForWebView(webView);
            WinUiDesignTabPin.PinActiveTab(bundle, webView, tab);
            _session?.ReloadBundle(bundle.Metadata.Id);
            SetStatus("Design thread pinned.");
            RefreshUi();
        }
        catch (Exception ex)
        {
            await WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Pin design tab", ex.Message);
        }
    }

    private void SetStatus(string message) => StatusLine.Text = message;
}

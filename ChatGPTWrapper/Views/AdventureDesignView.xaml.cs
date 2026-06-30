using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class AdventureDesignView : UserControl
{
    private readonly Dictionary<string, TextBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);
    private AdventureBundle? _bundle;
    private bool _suppressTabChange;
    private bool _suppressFieldChange;
    private string? _bootstrapRecoveryBanner;
    private EntityEditSourceSyncResult? _lastCanonSyncResult;
    private DispatcherTimer? _canonSyncNoticeTimer;
    private bool _canonSyncNoticePersistent;
    private bool _shellChromeActive;

    public event EventHandler? BackRequested;

    public event EventHandler? LinkProjectRequested;

    public event EventHandler? OpenDesignThreadRequested;

    public event EventHandler? PinDesignTabRequested;

    public event EventHandler? StartNewDesignThreadRequested;

    public event EventHandler? ManageThreadsRequested;

    public event EventHandler? DesignStatusRefreshRequested;

    public Func<string, Task<DesignChatSendResult>>? SendStepBriefAsync { get; set; }

    public Func<string, Task<DesignChatSendResult>>? SendSourceFilePromptAsync { get; set; }

    public Func<string?, Task<DesignChatSendResult>>? RefineInstructionsAsync { get; set; }

    public Func<Task>? GenerateInstructionsFileAsync { get; set; }

    public Func<Task>? OpenInstructionDesignerAsync { get; set; }

    public Func<IReadOnlyList<string>, Task<DesignChatSendResult>>? SendCombinedSourceFilePromptsAsync { get; set; }

    public Func<Task<DesignSourcePullResult>>? PullSourcesFromDesignThreadAsync { get; set; }

    public Func<AdventureDesignStep, Task<DesignExtractResult?>>? ExtractStepAsync { get; set; }

    public Func<Task<DesignExtractResult?>>? ProposeJsonImportAsync { get; set; }

    public Func<Task<string?>>? ImportFrameworkDraftAsync { get; set; }

    public Func<bool, bool, Task>? LaunchAdventureAsync { get; set; }

    public Func<Task>? OpenSourceManagerAsync { get; set; }

    public Func<IReadOnlyList<PhraseHighlightRule>>? GetPhraseHighlightRules { get; set; }

    public Guid? AdventureId => _bundle?.Metadata.Id;

    public AdventureDesignView()
    {
        InitializeComponent();
        WireEntityReferencePanel();
    }

    public void SetShellChromeState(bool shellChromeActive)
    {
        _shellChromeActive = shellChromeActive;
        DesignHeaderGrid.Visibility = shellChromeActive ? Visibility.Collapsed : Visibility.Visible;
    }

    public Action<IReadOnlyList<PhraseHighlightRule>>? CommitPhraseHighlightRules { get; set; }

    public void RefreshActiveEntityHighlightState() =>
        EntityReferencePanel.RefreshActiveHighlightState();

    private void WireEntityReferencePanel()
    {
        EntityReferencePanel.Configure(
            new EntityReferencePanelOptions
            {
                ShowPinToggle = false,
                ShowAiActions = false,
                ShowMoreMenu = false,
                PromptCanonReconcile = true,
                EditMode = EntityReferenceEditMode.Modal,
            },
            new EntityReferenceEditCallbacks
            {
                GetPhraseHighlightRules = () => GetPhraseHighlightRules?.Invoke(),
                CommitPhraseHighlightRules = rules => CommitPhraseHighlightRules?.Invoke(rules),
                OpenSourceManagerAsync = () => OpenSourceManagerAsync?.Invoke() ?? Task.CompletedTask,
                OnBundleReloaded = bundle =>
                {
                    _bundle = bundle;
                    EntityReferencePanel.LoadBundle(bundle);
                },
                OnStatusRefreshRequested = () => DesignStatusRefreshRequested?.Invoke(this, EventArgs.Empty),
                OnSourceSyncCompleted = result =>
                {
                    _lastCanonSyncResult = result;
                    if (_bundle is not null)
                        CanonHealthBar.Bind(_bundle);
                    if (result.Synced)
                    {
                        RefreshPipelineChecklist();
                        if (!string.IsNullOrWhiteSpace(result.Summary))
                            ShowCanonSyncNotice(result.Summary, result);
                    }
                    else if (result.Staged)
                    {
                        SetStatus(result.Summary ?? "Entity change staged — use Sync canon when ready.");
                        if (!string.IsNullOrWhiteSpace(result.Summary))
                            ShowCanonSyncNotice(result.Summary, result);
                    }

                    RefreshCanonSyncNoticeIfResolved();
                    DesignStatusRefreshRequested?.Invoke(this, EventArgs.Empty);
                },
            });
        EntityReferencePanel.EntitiesChanged += (_, _) =>
        {
            RefreshPipelineChecklist();
            if (_bundle is not null)
                CanonHealthBar.Bind(_bundle);
        };
        EntityReferencePanel.SizeChanged += (_, _) => ApplyEntityReferenceLayout();
    }

    private void ApplyEntityReferenceLayout()
    {
        if (_bundle is null || EntityReferenceSection.Visibility != Visibility.Visible)
            return;

        var width = EntityReferencePanel.ActualWidth > 0
            ? EntityReferencePanel.ActualWidth
            : DraftPanel.ActualWidth > 0
                ? DraftPanel.ActualWidth
                : 400;
        EntityReferencePanel.ApplyLayout(PlayLayoutCapabilities.FromContentWidth(width));
    }

    private void ParkEntityReferenceSection()
    {
        if (EntityReferenceSection.Parent is Panel parent)
            parent.Children.Remove(EntityReferenceSection);

        if (!EntityReferenceHost.Children.Contains(EntityReferenceSection))
            EntityReferenceHost.Children.Add(EntityReferenceSection);

        EntityReferenceSection.Visibility = Visibility.Collapsed;
    }

    private void AttachEntityReferenceSection()
    {
        if (EntityReferenceSection.Parent is Panel parent)
            parent.Children.Remove(EntityReferenceSection);

        EntityReferenceSection.Visibility = Visibility.Visible;
        DraftPanel.Children.Add(EntityReferenceSection);

        if (_bundle is null)
            return;

        ApplyEntityReferenceLayout();
        EntityReferencePanel.LoadBundle(_bundle);
    }

    public void LoadAdventure(Guid id)
    {
        _bundle = AdventureStore.Load(id);
        if (_bundle is null)
            return;

        AdventureDesignService.EnsureWorkspace(_bundle);
        AdventureDesignService.HydrateFromScenario(_bundle);

        if (!AdventureSourceFileService.HasLocalLoreSourceFiles(_bundle))
        {
            var recovered = AdventureSourceFileService.TryBootstrapLocalSourcesFromDesignWorkspace(_bundle);
            if (recovered > 0)
            {
                AdventureStore.Save(_bundle);
                _bundle = AdventureStore.Load(id);
                if (_bundle is null)
                    return;
            }
        }

        if (!string.IsNullOrWhiteSpace(_bundle.DesignWorkspace.PendingBootstrapNotice))
        {
            SetBootstrapRecoveryBanner(_bundle.DesignWorkspace.PendingBootstrapNotice);
            _bundle.DesignWorkspace.PendingBootstrapNotice = null;
            AdventureStore.Save(_bundle, AdventureSaveScope.DesignWorkspace);
        }
        else if (AdventureSourceFileService.HasLocalLoreSourceFiles(_bundle)
                 && _bootstrapRecoveryBanner is null)
        {
            SetBootstrapRecoveryBanner(null);
        }

        if (_bundle.DesignWorkspace.CurrentStep == AdventureDesignStep.Setup)
            AdventureDesignService.GoToStep(_bundle, AdventureDesignStep.Concept);

        TitleBlock.Text = $"Design: {_bundle.Metadata.Title}";
        var linkLabel = AdventureDesignChatService.CanUseChat(_bundle)
            ? "Change Project…"
            : "Link Project…";
        HeaderLinkProjectMenuItem.Header = linkLabel;
        LinkProjectButton.Content = linkLabel;

        SyncTabToStep();
        RefreshUi();
    }

    public void RefreshAfterGenerationJob()
    {
        if (_bundle is null || AdventureId is not { } id)
            return;

        _bundle = AdventureStore.Load(id);
        RefreshUi();
    }

    public void SetThreadStatus(string line) => ThreadStatusBlock.Text = line;

    private void ThreadStatus_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    public void SetDraftModeBanner(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            DraftModeBanner.Visibility = Visibility.Collapsed;
            DraftModeBannerText.Text = "";
            return;
        }

        DraftModeBannerText.Text = line;
        DraftModeBanner.Visibility = Visibility.Visible;
    }

    public void SetBootstrapRecoveryBanner(string? line)
    {
        _bootstrapRecoveryBanner = string.IsNullOrWhiteSpace(line) ? null : line.Trim();
        ApplyCombinedDraftModeBanner(null);
    }

    public void ApplyCombinedDraftModeBanner(string? draftLine)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(_bootstrapRecoveryBanner))
            parts.Add(_bootstrapRecoveryBanner);
        if (!string.IsNullOrWhiteSpace(draftLine))
            parts.Add(draftLine.Trim());
        SetDraftModeBanner(parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts));
    }

    public void SetStatus(string line) => StatusLine.Text = line;

    private AdventureDesignStep CurrentStep =>
        _bundle?.DesignWorkspace.CurrentStep ?? AdventureDesignStep.Concept;

    private void SyncTabToStep()
    {
        if (_bundle is null)
            return;

        _suppressTabChange = true;
        var designSteps = AdventureDesignService.OrderedSteps
            .Where(s => s is not AdventureDesignStep.Setup)
            .ToList();
        var idx = designSteps.IndexOf(CurrentStep);
        if (idx >= 0 && idx < StepTabs.Items.Count)
            StepTabs.SelectedIndex = idx;
        _suppressTabChange = false;
    }

    private void RefreshUi()
    {
        if (_bundle is null)
            return;

        var step = CurrentStep;
        var hasProject = AdventureDesignChatService.CanUseChat(_bundle);
        var isReview = step == AdventureDesignStep.Review;

        RebuildDraftPanel(step);
        RefreshPipelineChecklist();
        RefreshProposalsPanel(step);

        ContinueButton.Visibility = isReview ? Visibility.Collapsed : Visibility.Visible;
        LaunchButton.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;
        ImportDraftMenuItem.Visibility = step == AdventureDesignStep.Sources ? Visibility.Visible : Visibility.Collapsed;
        BackStepButton.IsEnabled = step != AdventureDesignStep.Concept;

        var canUseAi = hasProject && !isReview;
        SendStepBriefButton.IsEnabled = canUseAi;
        ExtractButton.IsEnabled = canUseAi;
        OpenDesignThreadMenuItem.IsEnabled = hasProject;
        StartNewDesignThreadMenuItem.IsEnabled = hasProject;
        PinDesignTabMenuItem.IsEnabled = hasProject;

        UpdatePipelineExpanderForStep(step);

        if (isReview)
            ShowReviewSummary();

        CanonHealthBar.Bind(_bundle);
    }

    private void CanonHealthBar_PlansChanged(object? sender, EventArgs e)
    {
        if (_bundle is null)
            return;

        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        if (_bundle is null)
            return;

        EntityReferencePanel.LoadBundle(_bundle);
        RefreshPipelineChecklist();
        RefreshUi();
        RefreshCanonSyncNoticeIfResolved();
        SetStatus("Canon synced to local sources.");
    }

    public void ShowCanonSyncNotice(string message, EntityEditSourceSyncResult? syncResult = null)
    {
        if (CanonHealthBar.Visibility == Visibility.Visible)
        {
            HideCanonSyncNotice();
            return;
        }

        _canonSyncNoticePersistent = CanonSyncNoticePolicy.RequiresVerification(_bundle, syncResult);
        CanonSyncNoticeText.Text = message;
        CanonSyncNoticeBanner.Visibility = Visibility.Visible;

        _canonSyncNoticeTimer?.Stop();
        _canonSyncNoticeTimer = null;
        if (!_canonSyncNoticePersistent)
        {
            _canonSyncNoticeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
            _canonSyncNoticeTimer.Tick += (_, _) => HideCanonSyncNotice();
            _canonSyncNoticeTimer.Start();
        }
    }

    private void RefreshCanonSyncNoticeIfResolved()
    {
        if (CanonSyncNoticeBanner.Visibility != Visibility.Visible || !_canonSyncNoticePersistent)
            return;

        if (!CanonSyncNoticePolicy.RequiresVerification(_bundle, _lastCanonSyncResult))
            HideCanonSyncNotice();
    }

    private void HideCanonSyncNotice()
    {
        _canonSyncNoticeTimer?.Stop();
        _canonSyncNoticeTimer = null;
        _canonSyncNoticePersistent = false;
        CanonSyncNoticeBanner.Visibility = Visibility.Collapsed;
    }

    private void DismissCanonSyncNotice_Click(object sender, RoutedEventArgs e) =>
        HideCanonSyncNotice();

    private void ViewLastCanonSyncDiff_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _lastCanonSyncResult is null)
            return;

        var dlg = new EntityChangePlanDiffPreviewDialog(_bundle, _lastCanonSyncResult)
        {
            Owner = Window.GetWindow(this),
        };
        dlg.ShowDialog();
    }

    private async void OpenSourceManagerFromBanner_Click(object sender, RoutedEventArgs e)
    {
        if (OpenSourceManagerAsync is not null)
            await OpenSourceManagerAsync();
    }

    private void CanonInbox_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var dlg = new CanonInboxDialog(_bundle) { Owner = Window.GetWindow(this) };
        dlg.NavigateRequested += (_, item) => NavigateCanonInboxItem(item);
        dlg.ShowDialog();
    }

    private async void NavigateCanonInboxItem(CanonInboxItem item)
    {
        if (_bundle is null)
            return;

        switch (item.Destination)
        {
            case CanonInboxDestination.ReferenceTab:
                AdventureDesignService.GoToStep(_bundle, AdventureDesignStep.Cast);
                Save();
                SyncTabToStep();
                RefreshUi();
                break;
            case CanonInboxDestination.SourcesSettings:
            case CanonInboxDestination.SourceManager:
            case CanonInboxDestination.JsonImportReview:
                if (OpenSourceManagerAsync is not null)
                    await OpenSourceManagerAsync();
                break;
            case CanonInboxDestination.CommitBar:
                CanonHealthBar.Bind(_bundle);
                break;
        }
    }

    private void UpdatePipelineExpanderForStep(AdventureDesignStep step)
    {
        if (PipelineChecklistExpander.Visibility != Visibility.Visible)
            return;

        PipelineChecklistExpander.IsExpanded = step is AdventureDesignStep.Sources or AdventureDesignStep.Review;
    }

    private void RefreshPipelineChecklist()
    {
        PipelineChecklistPanel.Children.Clear();
        if (_bundle is null)
        {
            PipelineChecklistExpander.Visibility = Visibility.Collapsed;
            return;
        }

        PipelineChecklistExpander.Visibility = Visibility.Visible;
        var muted = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        var accent = (System.Windows.Media.Brush)FindResource("AccentPrimaryBrush");
        var warning = (System.Windows.Media.Brush)FindResource("WarningBrush");
        var success = (System.Windows.Media.Brush)FindResource("SuccessBrush");
        var panelBg = (System.Windows.Media.Brush)FindResource("BgSurfaceBrush");
        var accentSubtle = (System.Windows.Media.Brush)FindResource("AccentSubtleBrush");

        foreach (var row in AdventureDesignSourcePromptService.BuildPipelineChecklist(_bundle))
        {
            if (row.IsReferenceFile && PipelineChecklistPanel.Children.Count == 0)
            {
                PipelineChecklistPanel.Children.Add(new TextBlock
                {
                    Text = "Project reference files",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = muted,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }
            else if (!row.IsReferenceFile && row.IsLoreFile && row.Position == 1)
            {
                PipelineChecklistPanel.Children.Add(new TextBlock
                {
                    Text = "Lore pipeline",
                    FontSize = 10,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = muted,
                    Margin = new Thickness(0, 8, 0, 4),
                });
            }

            var prefix = row.IsReferenceFile
                ? "★ "
                : row.IsLoreFile
                    ? $"{row.Position}. "
                    : "→ ";
            string statusText;
            if (row.IsReferenceFile)
            {
                var diskLabel = row.PresentOnDisk ? "✓ on disk" : "○ missing · Refresh export";
                statusText = row.IsPublishedToProject
                    ? $"{diskLabel} · ✓ uploaded"
                    : $"{diskLabel} · ↑ upload to Project";
            }
            else
            {
                var sentLabel = row.PromptSent ? "✓ sent" : "○ not sent";
                var diskLabel = row.PresentOnDisk ? "✓ on disk" : "○ missing";
                statusText = $"{sentLabel} · {diskLabel}";
                if (row.IsNextRecommended)
                    statusText += " · Next";
                if (row.IsBlocked && !string.IsNullOrWhiteSpace(row.BlockedReason))
                    statusText += $" · {row.BlockedReason}";
            }

            var rowPanel = new Border
            {
                Padding = new Thickness(6, 4, 6, 4),
                Margin = new Thickness(0, 0, 0, 2),
                Background = row.IsNextRecommended ? accentSubtle : panelBg,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = row.RelativePath,
            };
            rowPanel.MouseLeftButtonUp += PipelineChecklistRow_Click;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var title = new TextBlock
            {
                Text = $"{prefix}{row.RelativePath}",
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                FontWeight = row.IsNextRecommended ? FontWeights.SemiBold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(title);

            var status = new TextBlock
            {
                Text = statusText,
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Foreground = row.IsBlocked ? warning : row.IsNextRecommended ? accent : muted,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);

            if ((row.IsReferenceFile && row.PresentOnDisk && row.IsPublishedToProject)
                || (!row.IsReferenceFile && row.PromptSent && row.PresentOnDisk))
                title.Foreground = success;
            else if (row.IsReferenceFile && row.PresentOnDisk && !row.IsPublishedToProject)
                title.Foreground = accent;

            rowPanel.Child = grid;
            PipelineChecklistPanel.Children.Add(rowPanel);
        }
    }

    private void PipelineChecklistRow_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_bundle is null || sender is not Border { Tag: string relativePath })
            return;

        NavigateToPipelineFile(relativePath);
    }

    private void NavigateToPipelineFile(string relativePath)
    {
        if (_bundle is null)
            return;

        if (string.Equals(relativePath, SectionSchema.CanonFormatFile, StringComparison.OrdinalIgnoreCase))
        {
            OpenCanonFormatReferenceFile();
            return;
        }

        if (string.Equals(relativePath, SectionSchema.NarratorScalesFile, StringComparison.OrdinalIgnoreCase))
        {
            OpenNarratorScalesReferenceFile();
            return;
        }

        AdventureDesignStep step;
        if (string.Equals(relativePath, InstructionContractService.InstructionsSnippetFile, StringComparison.OrdinalIgnoreCase))
            step = AdventureDesignStep.Instructions;
        else if (AdventureDesignSourcePromptService.TryGetDefinition(relativePath, out var def) && def.PrimaryStep is { } primary)
            step = primary;
        else
            step = AdventureDesignStep.Sources;

        AdventureDesignService.GoToStep(_bundle, step);
        Save();
        SyncTabToStep();
        RefreshUi();
        SetStatus($"Jumped to {AdventureDesignService.GetStepDisplayName(step)} for {relativePath}.");
    }

    private void RebuildDraftPanel(AdventureDesignStep step)
    {
        ParkEntityReferenceSection();
        DraftPanel.Children.Clear();
        _fieldBoxes.Clear();

        RebuildSourceFilePromptPanel(step);

        foreach (var field in AdventureDesignService.GetFieldDefinitions(step))
        {
            DraftPanel.Children.Add(new TextBlock
            {
                Text = field.Label,
                Margin = new Thickness(0, 0, 0, 4),
            });

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
                Margin = new Thickness(0, 0, 0, 12),
                Tag = field.Key,
            };
            box.Text = AdventureDesignService.GetField(_bundle!, step, field.Key) ?? "";
            box.TextChanged += FieldBox_TextChanged;
            _fieldBoxes[field.Key] = box;
            DraftPanel.Children.Add(box);
        }

        if (step == AdventureDesignStep.Cast)
            AttachEntityReferenceSection();

        if (step is AdventureDesignStep.Cast or AdventureDesignStep.Lexicon or AdventureDesignStep.Sources)
        {
            DraftPanel.Children.Add(new TextBlock
            {
                Text = "Additional notes",
                Margin = new Thickness(0, 0, 0, 4),
            });
            var notes = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
                Margin = new Thickness(0, 0, 0, 12),
            };
            notes.Text = AdventureDesignService.GetFreeform(_bundle!, step);
            notes.TextChanged += (_, _) =>
            {
                if (_suppressFieldChange || _bundle is null)
                    return;
                AdventureDesignService.SetFreeform(_bundle, step, notes.Text);
                Save();
            };
            DraftPanel.Children.Add(notes);
        }

        if (ShouldShowLocalSourcesPanel(step))
            AppendLocalSourcesPanel();

        if (step == AdventureDesignStep.Review)
        {
            DraftPanel.Children.Add(new CheckBox
            {
                Content = "Bootstrap story cards on launch",
                IsChecked = _bundle!.DesignWorkspace.LaunchBootstrapLore,
                Margin = new Thickness(0, 0, 0, 8),
                Name = "BootstrapLoreCheck",
            });
            DraftPanel.Children.Add(new CheckBox
            {
                Content = "Start play after launch",
                IsChecked = _bundle.DesignWorkspace.LaunchStartPlay,
                Name = "StartPlayCheck",
            });
        }
    }

    private void RebuildSourceFilePromptPanel(AdventureDesignStep step)
    {
        if (_bundle is null)
            return;

        var prompts = AdventureDesignSourcePromptService.ForDesignStepInPipelineOrder(step).ToList();
        var hasInstructionsWorkflow = prompts.Any(p => IsInstructionsSnippet(p.RelativePath));
        if (prompts.Count == 0)
            return;

        AdventureDesignService.EnsureWorkspace(_bundle);
        var muted = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        var title = _bundle.Metadata.Title;
        var canUseChat = AdventureDesignChatService.CanUseChat(_bundle);

        if (!canUseChat && !hasInstructionsWorkflow)
            return;

        var lorePrompts = prompts
            .Where(p => !IsInstructionsSnippet(p.RelativePath))
            .ToList();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = step == AdventureDesignStep.Sources
                ? "Project source file prompts"
                : "Source file prompt",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        if (lorePrompts.Count == 1 && step != AdventureDesignStep.Sources)
        {
            var only = lorePrompts[0];
            var pipelineIndex = AdventureDesignSourcePromptService.PromptPipelineOrder
                .ToList()
                .FindIndex(p => string.Equals(p, only.RelativePath, StringComparison.OrdinalIgnoreCase));
            if (pipelineIndex >= 0)
            {
                var loreTotal = AdventureDesignSourcePromptService.PromptPipelineOrder.Count(p =>
                    !string.Equals(p, InstructionContractService.InstructionsSnippetFile, StringComparison.OrdinalIgnoreCase));
                panel.Children.Add(new TextBlock
                {
                    Text = $"Source draft {pipelineIndex + 1} of {loreTotal}: {only.RelativePath}",
                    Foreground = muted,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }
        }

        panel.Children.Add(new TextBlock
        {
            Text = step == AdventureDesignStep.Sources
                ? "Draft one file with a button, or select several and send as a single combined prompt."
                : "Ask for a downloadable source file and the same contents inline in the design thread reply.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (!string.IsNullOrWhiteSpace(title))
        {
            var examplePath = AdventureDesignSourcePromptService.BuildPrefixedSourcesPath(
                title,
                SectionSchema.ScenarioFile);
            panel.Children.Add(new TextBlock
            {
                Text = $"Files use wrapper title: \"{title}\" (e.g. {examplePath})",
                TextWrapping = TextWrapping.Wrap,
                Foreground = muted,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }

        var outOfOrderPaths = lorePrompts
            .Where(p => AdventureDesignSourcePromptService.IsOutOfOrder(_bundle, p.RelativePath))
            .Select(p => p.RelativePath)
            .ToList();
        if (outOfOrderPaths.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"Out of pipeline order: {string.Join(", ", outOfOrderPaths)} — send earlier files first, or confirm when drafting.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush"),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }

        if (DesignTabPinService.GetDesignConversationId(_bundle) is null
            && string.IsNullOrWhiteSpace(_bundle.Metadata.PinnedDesignTabKey))
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Open a Project chat (New chat in the Project). Pin with “Use this tab as design thread” to remember it, then click Draft.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = muted,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
            });
        }

        if (hasInstructionsWorkflow)
            AppendInstructionsWorkflowPanel(panel, muted);

        var buttons = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < lorePrompts.Count; i++)
        {
            var def = lorePrompts[i];
            var sent = AdventureDesignService.IsSourceFilePromptSent(_bundle, def.RelativePath);
            var label = lorePrompts.Count > 1
                ? $"{i + 1}. {def.ButtonLabel}{(sent ? " ✓ sent" : "")}"
                : $"{def.ButtonLabel}{(sent ? " ✓ sent" : "")}";

            var tooltip = def.Summary;
            var outOfOrder = AdventureDesignSourcePromptService.GetOutOfOrderTooltip(_bundle, def.RelativePath);
            if (!string.IsNullOrWhiteSpace(outOfOrder))
                tooltip = $"{tooltip}\n{outOfOrder}";

            var btn = new Button
            {
                Content = label,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 6, 6),
                Tag = def.RelativePath,
                ToolTip = tooltip,
            };
            btn.Click += SourceFilePrompt_Click;
            buttons.Children.Add(btn);
        }

        panel.Children.Add(buttons);

        if (lorePrompts.Count > 1)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Combine selected",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4),
            });

            var selectPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            for (var i = 0; i < lorePrompts.Count; i++)
            {
                var def = lorePrompts[i];
                var sent = AdventureDesignService.IsSourceFilePromptSent(_bundle, def.RelativePath);
                var checkboxLabel = $"{i + 1}. {def.ButtonLabel}{(sent ? " ✓ sent" : "")}";
                var tooltip = def.Summary;
                var outOfOrder = AdventureDesignSourcePromptService.GetOutOfOrderTooltip(_bundle, def.RelativePath);
                if (!string.IsNullOrWhiteSpace(outOfOrder))
                    tooltip = $"{tooltip}\n{outOfOrder}";

                selectPanel.Children.Add(new CheckBox
                {
                    Content = checkboxLabel,
                    Tag = def.RelativePath,
                    ToolTip = tooltip,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            panel.Children.Add(selectPanel);

            var sendSelected = new Button
            {
                Content = "Send selected as one prompt",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 4),
                Tag = "send-combined-source-prompts",
            };
            sendSelected.Click += SendSelectedSourcePrompts_Click;
            panel.Children.Add(sendSelected);
        }

        DraftPanel.Children.Add(WrapInShellCard(panel));
    }

    private static bool IsInstructionsSnippet(string relativePath) =>
        string.Equals(relativePath, InstructionContractService.InstructionsSnippetFile, StringComparison.OrdinalIgnoreCase);

    private void AppendInstructionsWorkflowPanel(StackPanel panel, System.Windows.Media.Brush muted)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "Instructions (deterministic + optional AI refine)",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 8, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Define the contract in the designer, generate the file locally without AI, then optionally refine wording in the design thread.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "Tutorial: Design instructions… → Save → Generate instructions file → Copy instructions. Do not use Draft cast/world prompts for instructions.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        var designBtn = new Button
        {
            Content = "Design instructions…",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };
        designBtn.Click += async (_, _) => await RunOpenInstructionDesignerAsync();
        actions.Children.Add(designBtn);

        var generateBtn = new Button
        {
            Content = "Generate instructions file",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };
        generateBtn.Click += async (_, _) => await RunGenerateInstructionsFileAsync();
        actions.Children.Add(generateBtn);

        var sent = AdventureDesignService.IsSourceFilePromptSent(
            _bundle!,
            InstructionContractService.InstructionsSnippetFile);
        var refineBtn = new Button
        {
            Content = $"Refine instructions with AI{(sent ? " ✓ sent" : "")}",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = "Sends the canonical manual version for wording polish only.",
        };
        refineBtn.Click += async (_, _) => await RunRefineInstructionsAsync();
        refineBtn.IsEnabled = _bundle is not null && AdventureDesignChatService.CanUseChat(_bundle);
        actions.Children.Add(refineBtn);
        panel.Children.Add(actions);

        panel.Children.Add(new TextBlock
        {
            Text = "Refinement notes (optional)",
            Margin = new Thickness(0, 0, 0, 4),
        });
        var notes = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 40,
            Margin = new Thickness(0, 0, 0, 4),
        };
        notes.Name = "InstructionsRefineNotesBox";
        panel.Children.Add(notes);
    }

    private string? ReadInstructionsRefineNotes()
    {
        foreach (var border in DraftPanel.Children.OfType<Border>())
        {
            var box = FindNamedTextBox(border.Child, "InstructionsRefineNotesBox");
            if (box is not null)
                return box.Text;
        }

        return null;
    }

    private static TextBox? FindNamedTextBox(System.Windows.DependencyObject root, string name)
    {
        if (root is TextBox textBox && string.Equals(textBox.Name, name, StringComparison.Ordinal))
            return textBox;

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindNamedTextBox(System.Windows.Media.VisualTreeHelper.GetChild(root, i), name);
            if (found is not null)
                return found;
        }

        return null;
    }

    private async Task RunOpenInstructionDesignerAsync()
    {
        if (_bundle is null || OpenInstructionDesignerAsync is null)
            return;

        await OpenInstructionDesignerAsync();
        if (AdventureId is { } id)
            LoadAdventure(id);
        else
            RefreshUi();
    }

    private async Task RunGenerateInstructionsFileAsync()
    {
        if (_bundle is null || GenerateInstructionsFileAsync is null)
            return;

        await GenerateInstructionsFileAsync();
        if (AdventureId is { } id)
            LoadAdventure(id);
        else
            RefreshUi();
    }

    private async Task RunRefineInstructionsAsync()
    {
        if (_bundle is null || RefineInstructionsAsync is null)
            return;

        SetSourcePromptButtonsEnabled(false);
        try
        {
            var result = await RefineInstructionsAsync(ReadInstructionsRefineNotes());
            SetStatus(FormatDesignSendStatus(
                result,
                result.Success
                    ? "Instructions refinement sent — check the design thread."
                    : null));
            if (result.Success && AdventureId is { } id)
                LoadAdventure(id);
        }
        finally
        {
            SetSourcePromptButtonsEnabled(true);
        }
    }

    private static bool ShouldShowLocalSourcesPanel(AdventureDesignStep step) =>
        step is AdventureDesignStep.Sources or AdventureDesignStep.Review
        || AdventureDesignSourcePromptService.ForDesignStep(step).Any();

    private void AppendLocalSourcesPanel()
    {
        if (_bundle is null)
            return;

        AdventureSourceFileService.ReconcileManifest(_bundle);
        var muted = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        var sourcesDir = AdventureSourceFileService.SourcesDirectory(_bundle);

        var panel = new StackPanel();
        AppendCanonFormatReferenceCallout(panel, sourcesDir, muted);
        AppendNarratorScalesReferenceCallout(panel, sourcesDir, muted);

        panel.Children.Add(new TextBlock
        {
            Text = "Local source files",
            Style = (Style)FindResource("ShellSectionHeaderStyle"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(CreateHintText(
            $"Saved under adventures/{{id}}/sources/ — use the pipeline checklist above for draft progress. Downloads map by filename (e.g. \"{_bundle.Metadata.Title} - scenario.md\" → scenario.md)."));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var primaryBar = new StackPanel { Style = (Style)FindResource("ShellCommandBarStyle") };

        var pullBtn = CreateSecondaryButton(
            "Pull from design thread",
            "Read the latest assistant reply and save inline source file blocks to sources/");
        pullBtn.Click += PullSourcesFromThread_Click;
        pullBtn.IsEnabled = PullSourcesFromDesignThreadAsync is not null
                            && AdventureDesignChatService.CanUseChat(_bundle);
        primaryBar.Children.Add(pullBtn);

        var importBtn = CreateSecondaryButton(
            "Import file…",
            "Choose downloaded .md files; prefixed ChatGPT names are mapped to canonical sources/");
        importBtn.Margin = new Thickness(8, 0, 0, 0);
        importBtn.Click += ImportSourceFiles_Click;
        primaryBar.Children.Add(importBtn);

        actions.Children.Add(primaryBar);

        var overflowMenu = new Menu { Background = System.Windows.Media.Brushes.Transparent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        var overflowRoot = new MenuItem { Header = "Import & sync…", Padding = new Thickness(10, 4, 10, 4) };

        var downloadsItem = new MenuItem { Header = "Import chat downloads", ToolTip = "Import recent .md files from the wrapper chat-downloads folder" };
        downloadsItem.Click += ImportChatDownloads_Click;
        overflowRoot.Items.Add(downloadsItem);

        var recoverItem = new MenuItem
        {
            Header = "Recover from design history",
            ToolTip = "Re-extract inline source blocks saved in the design workspace chat log into sources/",
        };
        recoverItem.Click += RecoverSourcesFromDesignHistory_Click;
        overflowRoot.Items.Add(recoverItem);

        var folderItem = new MenuItem { Header = "Open sources folder" };
        folderItem.Click += (_, _) =>
        {
            Directory.CreateDirectory(sourcesDir);
            Process.Start(new ProcessStartInfo(sourcesDir) { UseShellExecute = true });
            SetStatus($"Opened {sourcesDir}");
        };
        overflowRoot.Items.Add(folderItem);

        var regenerateItem = new MenuItem
        {
            Header = "Regenerate JSON from local sources",
            ToolTip = "Parse adventures/{id}/sources/*.md on disk into scenario.json and entities.json. "
                      + "Does not read ChatGPT Project files. After a rename, export JSON to sources first.",
        };
        regenerateItem.Click += RegenerateJsonFromSources_Click;
        overflowRoot.Items.Add(regenerateItem);

        var hasLore = ProjectSourceImportService.ImportableLoreFileNames.Any(fileName =>
            File.Exists(Path.Combine(sourcesDir, fileName)));
        var aiImportItem = new MenuItem
        {
            Header = "Propose JSON from sources (AI)",
            ToolTip = "Fallback when canonical markdown cannot be parsed — runs on the pinned design thread and proposes scenario.json / entities.json updates from project source references",
            IsEnabled = hasLore
                        && ProposeJsonImportAsync is not null
                        && !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId),
        };
        aiImportItem.Click += ProposeJsonImport_Click;
        overflowRoot.Items.Add(aiImportItem);

        overflowMenu.Items.Add(overflowRoot);
        actions.Children.Add(overflowMenu);
        panel.Children.Add(actions);

        AppendJsonImportReviewBanner(panel);

        DraftPanel.Children.Add(WrapInShellCard(panel));
    }

    private async void PullSourcesFromThread_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || PullSourcesFromDesignThreadAsync is null)
            return;

        SetStatus("Pulling source files from design thread…");
        try
        {
            var result = await PullSourcesFromDesignThreadAsync();
            if (result.Success)
            {
                _bundle = AdventureStore.Load(_bundle.Metadata.Id);
                RefreshUi();
                SetStatus(
                    result.SavedCount == 1
                        ? $"Saved {result.SavedPaths[0]} to sources/."
                        : $"Saved {result.SavedCount} files to sources/: {string.Join(", ", result.SavedPaths)}.");
            }
            else
            {
                SetStatus(result.Error ?? "Pull failed.");
            }
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void ImportSourceFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var dlg = new OpenFileDialog
        {
            Title = "Import source files",
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dlg.ShowDialog() != true || dlg.FileNames.Length == 0)
            return;

        var result = AdventureSourceFileService.TryImportFromAbsolutePaths(_bundle, dlg.FileNames);
        AdventureStore.Save(_bundle);
        RefreshUi();
        SetStatus(FormatImportStatus(result));
    }

    private void RecoverSourcesFromDesignHistory_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var saved = AdventureSourceFileService.TryBootstrapLocalSourcesFromDesignWorkspace(_bundle);
        if (saved > 0)
        {
            AdventureStore.Save(_bundle);
            _bundle = AdventureStore.Load(_bundle.Metadata.Id);
            if (_bundle is null)
                return;
            RefreshUi();
            SetBootstrapRecoveryBanner(
                $"Recovered {saved} source file(s) from design workspace history. "
                + "Use Pull from design thread if any files are incomplete.");
            SetStatus($"Recovered {saved} source file(s) from design workspace history.");
            return;
        }

        SetStatus("No missing source files could be recovered from design history.");
    }

    private void ImportChatDownloads_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var result = AdventureSourceFileService.TryImportRecentChatDownloads(_bundle, TimeSpan.FromHours(24));
        if (result.Imported > 0)
            AdventureStore.Save(_bundle);
        RefreshUi();
        var status = FormatImportStatus(result);
        SetStatus(status);
        if (result.Imported == 0)
        {
            var detail = result.Messages.Count > 0
                ? string.Join(Environment.NewLine, result.Messages)
                : status;
            MessageBox.Show(
                Window.GetWindow(this),
                detail + Environment.NewLine + Environment.NewLine
                + $"Folder: {ChatGptWebViewFileDiagnostics.DownloadsDirectory}",
                "Import chat downloads",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private void RegenerateJsonFromSources_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var renameDrifts = CanonEntityNameDriftService.DetectJsonAheadOfLocalSources(_bundle);
        if (renameDrifts.Count > 0)
        {
            var driftLines = string.Join(
                Environment.NewLine,
                renameDrifts.Select(d => $"• {d.SourceName} in {d.FileName} → {d.JsonName} in entities.json"));
            var driftChoice = MessageBox.Show(
                Window.GetWindow(this),
                "Local sources/*.md still use old names for entities you renamed in JSON:"
                + Environment.NewLine + Environment.NewLine
                + driftLines
                + Environment.NewLine + Environment.NewLine
                + "Regenerate JSON reads local sources (not your ChatGPT Project) and will undo those JSON renames."
                + Environment.NewLine + Environment.NewLine
                + "Use Sync canon on the banner above to export JSON to local sources first, then upload to your Project when ready."
                + Environment.NewLine + Environment.NewLine
                + "Yes — sync JSON to local sources now (recommended)"
                + Environment.NewLine
                + "Cancel — stop",
                "Rename drift detected",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (driftChoice != MessageBoxResult.Yes)
            {
                SetStatus("JSON regenerate cancelled — sync canon from JSON first.");
                return;
            }

            var sync = CanonHealthService.TrySyncAll(_bundle);
            AdventureStore.Save(_bundle);
            LoadAdventure(_bundle.Metadata.Id);
            CanonHealthBar.Bind(_bundle);
            SetStatus(sync.Summary ?? "Exported JSON to local sources.");
            return;
        }

        var rollback = ProjectSourceImportService.CaptureImportState(_bundle);
        var result = ProjectSourceImportService.Import(_bundle);
        if (!result.Success)
        {
            ProjectSourceImportService.RestoreImportState(_bundle, rollback);
            SetStatus(result.Summary);
            return;
        }

        var changeReport = result.ChangeReport
            ?? ProjectSourceImportService.BuildChangeReport(rollback, _bundle);

        var warningBlock = result.Warnings.Count > 0
            ? Environment.NewLine + Environment.NewLine
              + string.Join(Environment.NewLine, result.Warnings.Take(8))
              + (result.Warnings.Count > 8 ? Environment.NewLine + $"(+{result.Warnings.Count - 8} more)" : "")
            : "";

        var changeBlock = Environment.NewLine + Environment.NewLine + changeReport.Format();

        var suggestAi = result.Warnings.Count > 0 || !changeReport.HasChanges
            ? Environment.NewLine + Environment.NewLine
              + "Tip: For non-canonical markdown, try Propose JSON from sources (AI)."
            : "";

        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            result.Summary + changeBlock + warningBlock + suggestAi + Environment.NewLine + Environment.NewLine
            + "Apply these changes to scenario.json and entities.json?",
            "Regenerate JSON from sources",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes)
        {
            ProjectSourceImportService.RestoreImportState(_bundle, rollback);
            SetStatus("JSON regenerate cancelled.");
            return;
        }

        AdventureDesignService.HydrateFromScenario(_bundle);
        AdventureStore.Save(_bundle);
        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        if (_bundle is null)
        {
            SetStatus("JSON saved but adventure could not be reloaded.");
            return;
        }

        AdventureDesignService.HydrateFromScenario(_bundle);
        AdventureStore.Save(_bundle);
        RefreshUi();

        var adventureDir = AppDirectories.AdventureDirectory(_bundle.Metadata.Id);
        var status = result.Summary + " Design fields refreshed from JSON.";
        SetStatus(status);

        MessageBox.Show(
            Window.GetWindow(this),
            status + Environment.NewLine + Environment.NewLine
            + changeReport.Format()
            + Environment.NewLine + Environment.NewLine
            + $"Saved to:{Environment.NewLine}{Path.Combine(adventureDir, "scenario.json")}"
            + Environment.NewLine + Path.Combine(adventureDir, "entities.json"),
            "Regenerate JSON from sources",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string FormatImportStatus(AdventureSourceImportResult result)
    {
        if (result.Imported > 0 && result.Skipped == 0)
            return $"Imported {result.Imported} file(s) into sources/.";

        if (result.Imported > 0)
            return $"Imported {result.Imported} file(s); skipped {result.Skipped}.";

        return result.Messages.Count > 0
            ? result.Messages[0]
            : "No files imported.";
    }

    private void ShowReviewSummary()
    {
        DraftPanel.Children.Clear();
        _fieldBoxes.Clear();
        DraftPanel.Children.Add(new TextBlock
        {
            Text = AdventureDesignFinalizeService.BuildReviewSummary(_bundle!),
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 12),
        });
        if (ShouldShowLocalSourcesPanel(AdventureDesignStep.Review))
            AppendLocalSourcesPanel();
    }

    private void RefreshProposalsPanel(AdventureDesignStep step)
    {
        var pending = AdventureDesignService.GetOrCreateStep(_bundle!, step).PendingProposals
            .Where(p => p.Status == DesignProposalStatus.Pending)
            .ToList();

        AcceptProposalsButton.IsEnabled = pending.Count > 0;

        var existing = DraftPanel.Children.OfType<Border>().Where(b => b.Name == "ProposalsPanel").ToList();
        foreach (var el in existing)
            DraftPanel.Children.Remove(el);

        if (pending.Count == 0 || step == AdventureDesignStep.Review)
            return;

        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = "Pending proposals",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        foreach (var proposal in pending)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{proposal.FieldKey}: {proposal.ProposedValue}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            });
        }

        var border = new Border
        {
            Name = "ProposalsPanel",
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = panel,
        };
        DraftPanel.Children.Insert(0, border);
    }

    private void FieldBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFieldChange || _bundle is null || sender is not TextBox box || box.Tag is not string key)
            return;

        AdventureDesignService.SetField(_bundle, CurrentStep, key, box.Text);
        Save();
    }

    private void Save()
    {
        if (_bundle is null)
            return;

        AdventureStore.Save(_bundle);
    }

    private void StepTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTabChange || _bundle is null || StepTabs.SelectedItem is not TabItem tab)
            return;

        if (tab.Tag is not string tag || !Enum.TryParse<AdventureDesignStep>(tag, out var step))
            return;

        AdventureDesignService.GoToStep(_bundle, step);
        Save();
        RefreshUi();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke(this, EventArgs.Empty);

    private void LinkProject_Click(object sender, RoutedEventArgs e) =>
        LinkProjectRequested?.Invoke(this, EventArgs.Empty);

    private void OpenDesignThread_Click(object sender, RoutedEventArgs e) =>
        OpenDesignThreadRequested?.Invoke(this, EventArgs.Empty);

    private void PinDesignTab_Click(object sender, RoutedEventArgs e) =>
        PinDesignTabRequested?.Invoke(this, EventArgs.Empty);

    private void StartNewDesignThread_Click(object sender, RoutedEventArgs e) =>
        StartNewDesignThreadRequested?.Invoke(this, EventArgs.Empty);

    private void ManageThreads_Click(object sender, RoutedEventArgs e) =>
        ManageThreadsRequested?.Invoke(this, EventArgs.Empty);

    private void BackStep_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        if (AdventureDesignService.TryRetreatStep(_bundle, out _))
        {
            Save();
            SyncTabToStep();
            RefreshUi();
        }
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        PersistFieldsFromUi();
        AdventureDesignService.MarkStepAccepted(_bundle, CurrentStep);

        if (AdventureDesignService.TryAdvanceStep(_bundle, out _))
        {
            Save();
            SyncTabToStep();
            RefreshUi();
        }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var bootstrap = DraftPanel.Children.OfType<CheckBox>()
            .FirstOrDefault(c => c.Name == "BootstrapLoreCheck")?.IsChecked == true;
        var startPlay = DraftPanel.Children.OfType<CheckBox>()
            .FirstOrDefault(c => c.Name == "StartPlayCheck")?.IsChecked == true;

        _bundle.DesignWorkspace.LaunchBootstrapLore = bootstrap;
        _bundle.DesignWorkspace.LaunchStartPlay = startPlay;
        Save();

        if (LaunchAdventureAsync is null)
            return;

        LaunchButton.IsEnabled = false;
        try
        {
            await LaunchAdventureAsync(bootstrap, startPlay);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
        finally
        {
            LaunchButton.IsEnabled = true;
        }
    }

    private void AcceptProposals_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var count = AdventureDesignService.AcceptAllPendingProposals(_bundle, CurrentStep);
        Save();
        _suppressFieldChange = true;
        RefreshUi();
        _suppressFieldChange = false;
        SetStatus(count > 0 ? $"Accepted {count} proposal(s)." : "No proposals to accept.");
    }

    private static string FormatDesignSendStatus(DesignChatSendResult result, string? successLabel = null)
    {
        if (result.Success)
        {
            if (string.Equals(result.Error, "sent_no_capture", StringComparison.OrdinalIgnoreCase))
                return "Prompt sent — check the design thread in ChatGPT for the reply.";

            return !string.IsNullOrWhiteSpace(successLabel)
                ? successLabel
                : "Sent — check the design thread in ChatGPT.";
        }

        if (string.IsNullOrWhiteSpace(result.Error))
            return "Send failed.";

        return result.Error switch
        {
            "link_project_first" => "Link a ChatGPT Project first.",
            "design_tab_initializing" => "Design browser tab is still loading — try again.",
            _ => AdventureDesignDomChatService.FormatSendError(result.Error),
        };
    }

    private async void SendSelectedSourcePrompts_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SendCombinedSourceFilePromptsAsync is null)
            return;

        var selected = CollectSelectedSourcePromptPaths();
        if (selected.Count == 0)
        {
            SetStatus("Select at least one source file to send.");
            return;
        }

        var selectionWarning = AdventureDesignSourcePromptService.GetCombinedSelectionWarning(_bundle, selected);
        if (!string.IsNullOrWhiteSpace(selectionWarning)
            && MessageBox.Show(
                selectionWarning + Environment.NewLine + Environment.NewLine + "Send anyway?",
                "Source draft order",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            != MessageBoxResult.Yes)
        {
            return;
        }

        SetSourcePromptButtonsEnabled(false);
        try
        {
            var result = await SendCombinedSourceFilePromptsAsync(selected);
            if (result.Success)
            {
                var label = selected.Count == 1
                    ? "Source file prompt sent — check the design thread."
                    : $"{selected.Count} source file prompts sent as one batch — check the design thread.";
                SetStatus(FormatDesignSendStatus(result, label));
            }
            else
            {
                SetStatus(FormatDesignSendStatus(result));
            }
        }
        finally
        {
            SetSourcePromptButtonsEnabled(true);
        }
    }

    private IReadOnlyList<string> CollectSelectedSourcePromptPaths()
    {
        var paths = new List<string>();
        foreach (var border in DraftPanel.Children.OfType<Border>())
        {
            foreach (var checkbox in FindCheckBoxes(border.Child))
            {
                if (checkbox.IsChecked == true && checkbox.Tag is string path)
                    paths.Add(path);
            }
        }

        return AdventureDesignSourcePromptService.NormalizeSelectedPaths(paths);
    }

    private static IEnumerable<CheckBox> FindCheckBoxes(System.Windows.DependencyObject root)
    {
        if (root is CheckBox checkbox)
        {
            yield return checkbox;
            yield break;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in FindCheckBoxes(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private async void SourceFilePrompt_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null
            || SendSourceFilePromptAsync is null
            || sender is not Button { Tag: string relativePath })
        {
            return;
        }

        if (AdventureDesignSourcePromptService.IsOutOfOrder(_bundle, relativePath))
        {
            var reason = AdventureDesignSourcePromptService.GetOutOfOrderTooltip(_bundle, relativePath)
                         ?? "Earlier pipeline files should be drafted first.";
            if (MessageBox.Show(
                    reason + Environment.NewLine + Environment.NewLine + "Send this draft anyway?",
                    "Source draft order",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            {
                return;
            }
        }

        SetSourcePromptButtonsEnabled(false);
        try
        {
            var result = await SendSourceFilePromptAsync(relativePath);
            if (result.Success)
            {
                SetStatus(
                    AdventureDesignSourcePromptService.TryGetDefinition(relativePath, out var def)
                        ? FormatDesignSendStatus(result, $"{def.ButtonLabel} sent — check the design thread.")
                        : FormatDesignSendStatus(result));
            }
            else
            {
                SetStatus(FormatDesignSendStatus(result));
            }
        }
        finally
        {
            SetSourcePromptButtonsEnabled(true);
        }
    }

    private void SetSourcePromptButtonsEnabled(bool enabled)
    {
        var canUse = enabled && AdventureDesignChatService.CanUseChat(_bundle!);

        foreach (var border in DraftPanel.Children.OfType<Border>())
        {
            foreach (var btn in FindButtons(border.Child))
            {
                if (btn.Tag is string path && path.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    btn.IsEnabled = canUse;
                else if (string.Equals(btn.Tag as string, "send-combined-source-prompts", StringComparison.Ordinal))
                    btn.IsEnabled = canUse;
            }

            foreach (var checkbox in FindCheckBoxes(border.Child))
                checkbox.IsEnabled = canUse;
        }

        SendStepBriefButton.IsEnabled = canUse && CurrentStep != AdventureDesignStep.Review;
        ExtractButton.IsEnabled = SendStepBriefButton.IsEnabled;
    }

    private static IEnumerable<Button> FindButtons(System.Windows.DependencyObject root)
    {
        if (root is Button button)
        {
            yield return button;
            yield break;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var child in FindButtons(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
                yield return child;
        }
    }

    private async void SendStepBrief_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || SendStepBriefAsync is null)
            return;

        var note = BriefNoteBox.Text.Trim();

        SendStepBriefButton.IsEnabled = false;
        ExtractButton.IsEnabled = false;
        SetSourcePromptButtonsEnabled(false);
        try
        {
            var text = string.IsNullOrWhiteSpace(note)
                ? $"Continue designing the {CurrentStep} step."
                : note;
            var result = await SendStepBriefAsync(text);
            if (result.Success)
                SetStatus(FormatDesignSendStatus(result));
            else
                SetStatus(FormatDesignSendStatus(result));
        }
        finally
        {
            SetSourcePromptButtonsEnabled(true);
            RefreshUi();
        }
    }

    private async void Extract_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || ExtractStepAsync is null)
            return;

        ExtractButton.IsEnabled = false;
        try
        {
            var result = await ExtractStepAsync(CurrentStep);
            _bundle = AdventureStore.Load(AdventureId!.Value);
            if (result is { ProposalCount: > 0 })
                SetStatus($"Extracted {result.ProposalCount} proposal(s).");
            else
                SetStatus(result?.Error ?? "No proposals extracted.");
            RefreshUi();
        }
        finally
        {
            if (_bundle is not null)
                ExtractButton.IsEnabled = AdventureDesignChatService.CanUseChat(_bundle);
        }
    }

    private async void ImportDraft_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || ImportFrameworkDraftAsync is null)
            return;

        var markdown = await ImportFrameworkDraftAsync();
        if (string.IsNullOrWhiteSpace(markdown))
            return;

        AdventureDesignService.ImportDraftFrameworkMarkdown(_bundle, markdown);
        Save();
        RefreshUi();
        SetStatus("Imported framework draft into Sources step.");
    }

    private async void ProposeJsonImport_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || ProposeJsonImportAsync is null)
            return;

        SetStatus("Proposing JSON from sources (AI)…");
        try
        {
            var result = await ProposeJsonImportAsync();
            _bundle = AdventureStore.Load(_bundle.Metadata.Id);
            if (result is null)
            {
                SetStatus("JSON import job did not run.");
                return;
            }

            if (result.Success && result.ProposalCount > 0)
            {
                SetStatus($"Queued {result.ProposalCount} JSON import proposal(s).");
                ShowJsonImportReviewDialog();
            }
            else
                SetStatus(result.Error ?? "No JSON import proposals returned.");
            RefreshUi();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    public void OpenProposalReviewHub(ProposalReviewCategory? focusCategory = null)
    {
        if (_bundle is null)
            return;

        var fresh = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
        var dlg = new ProposalReviewHubDialog(fresh, focusCategory)
        {
            Owner = Window.GetWindow(this),
        };
        dlg.ShowDialog();

        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        RefreshUi();
        if (dlg.ChangesSaved)
            SetStatus("Proposal review updated.");
    }

    public bool TryOpenJsonImportReviewDialog() =>
        _bundle is not null
        && _bundle.Scenario.JsonImportReviewQueue.Count > 0
        && ShowJsonImportReviewDialog();

    private bool ShowJsonImportReviewDialog()
    {
        if (_bundle is null || _bundle.Scenario.JsonImportReviewQueue.Count == 0)
            return false;

        var dialog = new JsonImportReviewDialog(_bundle.Metadata.Id)
        {
            Owner = Window.GetWindow(this),
        };
        dialog.ShowDialog();

        _bundle = AdventureStore.Load(_bundle.Metadata.Id);
        RefreshUi();
        if (dialog.ChangesSaved)
            SetStatus("JSON import review updated.");

        return dialog.ChangesSaved;
    }

    private void AppendJsonImportReviewBanner(StackPanel panel)
    {
        if (_bundle is null)
            return;

        var count = _bundle.Scenario.JsonImportReviewQueue.Count;
        if (count == 0)
            return;

        var muted = (System.Windows.Media.Brush)FindResource("TextMutedBrush");
        var borderBrush = (System.Windows.Media.Brush)FindResource("BorderSubtleBrush");
        var unsupported = JsonImportConflictService.AnalyzeQueue(_bundle)
            .Count(a => a.Severity == JsonImportConflictSeverity.Unsupported);

        var summary = count == 1
            ? "1 JSON import proposal awaiting review"
            : $"{count} JSON import proposals awaiting review";
        if (unsupported > 0)
            summary += $" ({unsupported} unsupported)";

        var banner = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        banner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        banner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        banner.Children.Add(new TextBlock
        {
            Text = summary,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = muted,
        });

        var reviewBtn = new Button
        {
            Content = "Review proposals…",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(8, 0, 0, 0),
        };
        reviewBtn.Click += (_, _) => OpenProposalReviewHub(ProposalReviewCategory.JsonImport);
        Grid.SetColumn(reviewBtn, 1);
        banner.Children.Add(reviewBtn);

        panel.Children.Add(new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Background = (System.Windows.Media.Brush)FindResource("BgSurfaceBrush"),
            Child = banner,
        });
    }

    private void AppendCanonFormatReferenceCallout(
        StackPanel panel,
        string sourcesDir,
        System.Windows.Media.Brush muted)
    {
        if (_bundle is null)
            return;

        AdventureSourceFileService.EnsureLayout(_bundle);
        var canonPath = AdventureSourceFileService.ResolveAbsolutePath(_bundle, SectionSchema.CanonFormatFile);
        var present = File.Exists(canonPath);
        var entry = _bundle.SourceManifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, SectionSchema.CanonFormatFile, StringComparison.OrdinalIgnoreCase));
        var uploaded = entry?.IsManuallyCurrent() ?? false;
        var accent = (System.Windows.Media.Brush)FindResource("AccentPrimaryBrush");

        var status = !present
            ? "Missing — run Refresh export in Source Manager or regenerate from JSON."
            : uploaded
                ? "Ready on disk and marked uploaded to Project."
                : "Ready on disk — upload to ChatGPT Project → Files and mark Published in Source Manager.";

        var callout = new StackPanel();
        callout.Children.Add(new TextBlock
        {
            Text = "Canon format reference (canon-format.md)",
            Style = (Style)FindResource("ShellSectionHeaderStyle"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        callout.Children.Add(CreateHintText(
            "Auto-generated model reference for section headers, field labels, and party/NPC rules. Upload with lore files so ChatGPT follows the same canon shape.",
            new Thickness(0, 0, 0, 4)));
        callout.Children.Add(new TextBlock
        {
            Text = status,
            TextWrapping = TextWrapping.Wrap,
            FontSize = HintFontSize,
            Foreground = uploaded ? (System.Windows.Media.Brush)FindResource("SuccessBrush") : accent,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var menu = new Menu { Background = System.Windows.Media.Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new MenuItem { Header = "Canon reference…", Padding = new Thickness(10, 4, 10, 4) };

        var openItem = new MenuItem { Header = "Open canon-format.md", IsEnabled = present };
        openItem.Click += (_, _) => OpenCanonFormatReferenceFile();
        root.Items.Add(openItem);

        var copyItem = new MenuItem
        {
            Header = "Copy for Project upload",
            IsEnabled = present,
            ToolTip = "Copy file contents to paste into ChatGPT Project → Files",
        };
        copyItem.Click += (_, _) =>
        {
            if (!present)
                return;

            Clipboard.SetText(File.ReadAllText(canonPath));
            SetStatus("Copied canon-format.md to clipboard — paste into ChatGPT Project → Files.");
        };
        root.Items.Add(copyItem);

        var sourceMgrItem = new MenuItem
        {
            Header = "Open Source Manager",
            ToolTip = "Refresh export, drag files to Project, and mark Published",
        };
        sourceMgrItem.Click += async (_, _) =>
        {
            if (OpenSourceManagerAsync is not null)
                await OpenSourceManagerAsync();
            else
            {
                Directory.CreateDirectory(sourcesDir);
                Process.Start(new ProcessStartInfo(sourcesDir) { UseShellExecute = true });
                SetStatus($"Opened {sourcesDir}");
            }
        };
        root.Items.Add(sourceMgrItem);

        menu.Items.Add(root);
        callout.Children.Add(menu);

        panel.Children.Add(new Border
        {
            Style = (Style)FindResource("ShellCardStyle"),
            Background = (System.Windows.Media.Brush)FindResource("AccentSubtleBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            Child = callout,
        });
    }

    private void OpenCanonFormatReferenceFile()
    {
        if (_bundle is null)
            return;

        AdventureSourceFileService.EnsureLayout(_bundle);
        var path = AdventureSourceFileService.ResolveAbsolutePath(_bundle, SectionSchema.CanonFormatFile);
        if (!File.Exists(path))
        {
            SetStatus("canon-format.md is missing — run Refresh export in Source Manager.");
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        SetStatus($"Opened {SectionSchema.CanonFormatFile}");
    }

    private void AppendNarratorScalesReferenceCallout(
        StackPanel panel,
        string sourcesDir,
        System.Windows.Media.Brush muted)
    {
        if (_bundle is null)
            return;

        AdventureSourceFileService.EnsureLayout(_bundle);
        var scalesPath = AdventureSourceFileService.ResolveAbsolutePath(_bundle, SectionSchema.NarratorScalesFile);
        var present = File.Exists(scalesPath);
        var entry = _bundle.SourceManifest.Entries.FirstOrDefault(e =>
            string.Equals(e.RelativePath, SectionSchema.NarratorScalesFile, StringComparison.OrdinalIgnoreCase));
        var uploaded = entry?.IsManuallyCurrent() ?? false;
        var accent = (System.Windows.Media.Brush)FindResource("AccentPrimaryBrush");

        var status = !present
            ? "Missing — run Refresh export in Source Manager or regenerate from JSON."
            : uploaded
                ? "Ready on disk and marked uploaded to Project."
                : "Ready on disk — upload to ChatGPT Project → Files and mark Published in Source Manager.";

        var callout = new StackPanel();
        callout.Children.Add(new TextBlock
        {
            Text = "Narrator scales reference (narrator-scales.md)",
            Style = (Style)FindResource("ShellSectionHeaderStyle"),
            Margin = new Thickness(0, 0, 0, 4),
        });
        callout.Children.Add(CreateHintText(
            "Auto-generated definitions for narrator preset selectors (length, detail, tone, difficulty, violence). Upload with canon-format and lore so packets and instructions resolve meaningfully.",
            new Thickness(0, 0, 0, 4)));
        callout.Children.Add(new TextBlock
        {
            Text = status,
            TextWrapping = TextWrapping.Wrap,
            FontSize = HintFontSize,
            Foreground = uploaded ? (System.Windows.Media.Brush)FindResource("SuccessBrush") : accent,
            Margin = new Thickness(0, 0, 0, 8),
        });

        var menu = new Menu { Background = System.Windows.Media.Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Left };
        var root = new MenuItem { Header = "Narrator scales…", Padding = new Thickness(10, 4, 10, 4) };

        var openItem = new MenuItem { Header = "Open narrator-scales.md", IsEnabled = present };
        openItem.Click += (_, _) => OpenNarratorScalesReferenceFile();
        root.Items.Add(openItem);

        var copyItem = new MenuItem
        {
            Header = "Copy for Project upload",
            IsEnabled = present,
            ToolTip = "Copy file contents to paste into ChatGPT Project → Files",
        };
        copyItem.Click += (_, _) =>
        {
            if (!present)
                return;

            Clipboard.SetText(File.ReadAllText(scalesPath));
            SetStatus("Copied narrator-scales.md to clipboard — paste into ChatGPT Project → Files.");
        };
        root.Items.Add(copyItem);

        var sourceMgrItem = new MenuItem
        {
            Header = "Open Source Manager",
            ToolTip = "Refresh export, drag files to Project, and mark Published",
        };
        sourceMgrItem.Click += async (_, _) =>
        {
            if (OpenSourceManagerAsync is not null)
                await OpenSourceManagerAsync();
            else
            {
                Directory.CreateDirectory(sourcesDir);
                Process.Start(new ProcessStartInfo(sourcesDir) { UseShellExecute = true });
                SetStatus($"Opened {sourcesDir}");
            }
        };
        root.Items.Add(sourceMgrItem);

        menu.Items.Add(root);
        callout.Children.Add(menu);

        panel.Children.Add(new Border
        {
            Style = (Style)FindResource("ShellCardStyle"),
            Background = (System.Windows.Media.Brush)FindResource("AccentSubtleBrush"),
            Margin = new Thickness(0, 0, 0, 12),
            Child = callout,
        });
    }

    private void OpenNarratorScalesReferenceFile()
    {
        if (_bundle is null)
            return;

        AdventureSourceFileService.EnsureLayout(_bundle);
        var path = AdventureSourceFileService.ResolveAbsolutePath(_bundle, SectionSchema.NarratorScalesFile);
        if (!File.Exists(path))
        {
            SetStatus("narrator-scales.md is missing — run Refresh export in Source Manager.");
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        SetStatus($"Opened {SectionSchema.NarratorScalesFile}");
    }

    private void PersistFieldsFromUi()
    {
        if (_bundle is null)
            return;

        foreach (var (key, box) in _fieldBoxes)
            AdventureDesignService.SetField(_bundle, CurrentStep, key, box.Text);
    }

    private double HintFontSize => (double)FindResource("FontSizeHint");

    private Border WrapInShellCard(UIElement child, Thickness? margin = null) =>
        new()
        {
            Style = (Style)FindResource("ShellCardStyle"),
            Margin = margin ?? new Thickness(0, 0, 0, 12),
            Child = child,
        };

    private TextBlock CreateHintText(string text, Thickness? margin = null) =>
        new()
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            FontSize = HintFontSize,
            Margin = margin ?? new Thickness(0, 0, 0, 8),
        };

    private Button CreateSecondaryButton(string content, string? toolTip = null)
    {
        var button = new Button
        {
            Content = content,
            Style = (Style)FindResource("ShellCommandBarSecondarySlot"),
        };
        if (!string.IsNullOrWhiteSpace(toolTip))
            button.ToolTip = toolTip;
        return button;
    }
}

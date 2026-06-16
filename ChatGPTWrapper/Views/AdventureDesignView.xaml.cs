using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class AdventureDesignView : UserControl
{
    private readonly Dictionary<string, TextBox> _fieldBoxes = new(StringComparer.OrdinalIgnoreCase);
    private AdventureBundle? _bundle;
    private bool _suppressTabChange;
    private bool _suppressFieldChange;

    public event EventHandler? BackRequested;

    public event EventHandler? LinkProjectRequested;

    public event EventHandler? OpenDesignThreadRequested;

    public event EventHandler? PinDesignTabRequested;

    public event EventHandler? StartNewDesignThreadRequested;

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

    public Guid? AdventureId => _bundle?.Metadata.Id;

    public AdventureDesignView()
    {
        InitializeComponent();
    }

    public void LoadAdventure(Guid id)
    {
        _bundle = AdventureStore.Load(id);
        if (_bundle is null)
            return;

        AdventureDesignService.EnsureWorkspace(_bundle);
        AdventureDesignService.HydrateFromScenario(_bundle);

        if (_bundle.DesignWorkspace.CurrentStep == AdventureDesignStep.Setup)
            AdventureDesignService.GoToStep(_bundle, AdventureDesignStep.Concept);

        TitleBlock.Text = $"Design: {_bundle.Metadata.Title}";
        LinkProjectButton.Content = AdventureDesignChatService.CanUseChat(_bundle)
            ? "Change Project…"
            : "Link Project…";

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
        RefreshProposalsPanel(step);

        ContinueButton.Visibility = isReview ? Visibility.Collapsed : Visibility.Visible;
        LaunchButton.Visibility = isReview ? Visibility.Visible : Visibility.Collapsed;
        ImportDraftButton.Visibility = step == AdventureDesignStep.Sources ? Visibility.Visible : Visibility.Collapsed;
        BackStepButton.IsEnabled = step != AdventureDesignStep.Concept;

        var canUseAi = hasProject && !isReview;
        SendStepBriefButton.IsEnabled = canUseAi;
        ExtractButton.IsEnabled = canUseAi;
        OpenDesignThreadButton.IsEnabled = hasProject;
        StartNewDesignThreadButton.IsEnabled = hasProject;
        PinDesignTabButton.IsEnabled = hasProject;

        if (isReview)
            ShowReviewSummary();
    }

    private void RebuildDraftPanel(AdventureDesignStep step)
    {
        DraftPanel.Children.Clear();
        _fieldBoxes.Clear();

        if (CurrentStep == AdventureDesignStep.Instructions)
        {
            DraftPanel.Children.Add(new Button
            {
                Content = "Open instructions designer…",
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Left,
            });
            if (DraftPanel.Children[^1] is Button designerBtn)
                designerBtn.Click += async (_, _) => await RunOpenInstructionDesignerAsync();
        }

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
        var muted = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        var borderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSubtle");
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

        if (step == AdventureDesignStep.Sources)
        {
            var nextPath = AdventureDesignSourcePromptService.GetNextRecommendedPath(_bundle);
            if (nextPath is not null
                && AdventureDesignSourcePromptService.TryGetDefinition(nextPath, out var nextDef))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"Suggested next: {nextDef.ButtonLabel} ({nextPath})",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = muted,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
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

        DraftPanel.Children.Add(new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12),
            Background = (System.Windows.Media.Brush)FindResource("PanelBgBrush"),
            Child = panel,
        });
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
        var muted = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        var borderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSubtle");
        var sourcesDir = AdventureSourceFileService.SourcesDirectory(_bundle);
        var statuses = AdventureSourceFileService.GetPipelineStatuses(_bundle);

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Local source files",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Saved under adventures/{{id}}/sources/ — downloads from the design chat are mapped by filename (e.g. \"{_bundle.Metadata.Title} - scenario.md\" → scenario.md).",
            TextWrapping = TextWrapping.Wrap,
            Foreground = muted,
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
        });

        foreach (var status in statuses)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{(status.Present ? "✓" : "○")} {status.RelativePath}",
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 11,
                Foreground = status.Present ? muted : System.Windows.Media.Brushes.OrangeRed,
                Margin = new Thickness(0, 0, 0, 2),
            });
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };

        var pullBtn = new Button
        {
            Content = "Pull from design thread",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = "Read the latest assistant reply and save inline source file blocks to sources/",
        };
        pullBtn.Click += PullSourcesFromThread_Click;
        pullBtn.IsEnabled = PullSourcesFromDesignThreadAsync is not null
                            && AdventureDesignChatService.CanUseChat(_bundle);
        actions.Children.Add(pullBtn);

        var importBtn = new Button
        {
            Content = "Import file…",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = "Choose downloaded .md files; prefixed ChatGPT names are mapped to canonical sources/",
        };
        importBtn.Click += ImportSourceFiles_Click;
        actions.Children.Add(importBtn);

        var downloadsBtn = new Button
        {
            Content = "Import chat downloads",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = "Import recent .md files from the wrapper chat-downloads folder",
        };
        downloadsBtn.Click += ImportChatDownloads_Click;
        actions.Children.Add(downloadsBtn);

        var folderBtn = new Button
        {
            Content = "Open sources folder",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
        };
        folderBtn.Click += (_, _) =>
        {
            Directory.CreateDirectory(sourcesDir);
            Process.Start(new ProcessStartInfo(sourcesDir) { UseShellExecute = true });
            SetStatus($"Opened {sourcesDir}");
        };
        actions.Children.Add(folderBtn);

        var regenerateBtn = new Button
        {
            Content = "Regenerate JSON from sources",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            ToolTip = "Parse canonical sources/*.md and update scenario.json and entities.json (offline, deterministic)",
        };
        regenerateBtn.Click += RegenerateJsonFromSources_Click;
        actions.Children.Add(regenerateBtn);

        var hasLore = ProjectSourceImportService.ImportableLoreFileNames.Any(fileName =>
            File.Exists(Path.Combine(sourcesDir, fileName)));
        var aiImportBtn = new Button
        {
            Content = "Propose JSON from sources (AI)",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 6),
            Opacity = 0.92,
            ToolTip = "Fallback when canonical markdown cannot be parsed — runs a utility job to propose scenario.json / entities.json updates",
        };
        aiImportBtn.Click += ProposeJsonImport_Click;
        aiImportBtn.IsEnabled = hasLore
                                && ProposeJsonImportAsync is not null
                                && !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        actions.Children.Add(aiImportBtn);

        panel.Children.Add(actions);

        AppendJsonImportReviewPanel(panel);

        DraftPanel.Children.Add(new Border
        {
            Name = "LocalSourcesPanel",
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 12),
            Background = (System.Windows.Media.Brush)FindResource("PanelBgBrush"),
            Child = panel,
        });
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

    private void ImportChatDownloads_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

        var result = AdventureSourceFileService.TryImportRecentChatDownloads(_bundle, TimeSpan.FromHours(24));
        if (result.Imported > 0)
            AdventureStore.Save(_bundle);
        RefreshUi();
        SetStatus(FormatImportStatus(result));
    }

    private void RegenerateJsonFromSources_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null)
            return;

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
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
            });
        }

        var border = new Border
        {
            Name = "ProposalsPanel",
            BorderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSubtle"),
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
                SetStatus($"Queued {result.ProposalCount} JSON import proposal(s) — review below.");
            else
                SetStatus(result.Error ?? "No JSON import proposals returned.");
            RefreshUi();
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
        }
    }

    private void AppendJsonImportReviewPanel(StackPanel panel)
    {
        if (_bundle is null)
            return;

        var queue = _bundle.Scenario.JsonImportReviewQueue;
        if (queue.Count == 0)
            return;

        var muted = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        var borderBrush = (System.Windows.Media.Brush)FindResource("BorderBrushSubtle");

        var review = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        review.Children.Add(new TextBlock
        {
            Text = $"{queue.Count} JSON import proposal(s) awaiting review",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6),
        });

        foreach (var item in queue.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var summary = FormatJsonImportProposalSummary(item);
            row.Children.Add(new TextBlock
            {
                Text = summary,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
            });

            var acceptBtn = new Button
            {
                Content = "Accept",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(6, 0, 0, 0),
                Tag = item.Id,
            };
            acceptBtn.Click += AcceptJsonImportProposal_Click;
            Grid.SetColumn(acceptBtn, 1);
            row.Children.Add(acceptBtn);

            var rejectBtn = new Button
            {
                Content = "Reject",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(4, 0, 0, 0),
                Tag = item.Id,
            };
            rejectBtn.Click += RejectJsonImportProposal_Click;
            Grid.SetColumn(rejectBtn, 2);
            row.Children.Add(rejectBtn);

            review.Children.Add(row);
        }

        var bulk = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        var acceptAll = new Button
        {
            Content = "Accept all",
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(0, 0, 6, 0),
        };
        acceptAll.Click += AcceptAllJsonImportProposals_Click;
        bulk.Children.Add(acceptAll);

        var rejectAll = new Button
        {
            Content = "Reject all",
            Padding = new Thickness(8, 4, 8, 4),
        };
        rejectAll.Click += RejectAllJsonImportProposals_Click;
        bulk.Children.Add(rejectAll);
        review.Children.Add(bulk);

        panel.Children.Add(new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 8, 0, 0),
            Background = (System.Windows.Media.Brush)FindResource("PanelBgBrush"),
            Child = review,
        });
    }

    private static string FormatJsonImportProposalSummary(JsonImportReviewItem item)
    {
        if (string.Equals(item.Kind, SourceJsonImportService.KindScenarioField, StringComparison.OrdinalIgnoreCase))
        {
            var preview = PreviewProposalText(item.Value);
            var prior = string.IsNullOrWhiteSpace(item.PriorValue) ? "" : $" (was: {PreviewProposalText(item.PriorValue)})";
            return $"scenario.{item.Field} → {preview}{prior}";
        }

        var entityPreview = PreviewProposalText(item.Value);
        return $"{item.Action} {item.EntityType} \"{item.Name}\" → {entityPreview}";
    }

    private static string PreviewProposalText(string value)
    {
        var trimmed = value.Trim().ReplaceLineEndings(" ");
        return trimmed.Length <= 64 ? trimmed : trimmed[..61] + "…";
    }

    private void AcceptJsonImportProposal_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || sender is not Button { Tag: Guid id })
            return;

        var item = _bundle.Scenario.JsonImportReviewQueue.FirstOrDefault(q => q.Id == id);
        if (item is null)
            return;

        if (!SourceJsonImportService.ApplyAccepted(_bundle, item))
        {
            SetStatus("Could not apply JSON import proposal.");
            return;
        }

        _bundle.Scenario.JsonImportReviewQueue.Remove(item);
        AdventureDesignService.HydrateFromScenario(_bundle);
        Save();
        RefreshUi();
        SetStatus("Applied JSON import proposal.");
    }

    private void RejectJsonImportProposal_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || sender is not Button { Tag: Guid id })
            return;

        var item = _bundle.Scenario.JsonImportReviewQueue.FirstOrDefault(q => q.Id == id);
        if (item is null)
            return;

        _bundle.Scenario.JsonImportReviewQueue.Remove(item);
        Save();
        RefreshUi();
        SetStatus("Rejected JSON import proposal.");
    }

    private void AcceptAllJsonImportProposals_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _bundle.Scenario.JsonImportReviewQueue.Count == 0)
            return;

        var applied = 0;
        foreach (var item in _bundle.Scenario.JsonImportReviewQueue.ToList())
        {
            if (SourceJsonImportService.ApplyAccepted(_bundle, item))
                applied++;
        }

        _bundle.Scenario.JsonImportReviewQueue.Clear();
        AdventureDesignService.HydrateFromScenario(_bundle);
        Save();
        RefreshUi();
        SetStatus(applied > 0
            ? $"Applied {applied} JSON import proposal(s)."
            : "No JSON import proposals could be applied.");
    }

    private void RejectAllJsonImportProposals_Click(object sender, RoutedEventArgs e)
    {
        if (_bundle is null || _bundle.Scenario.JsonImportReviewQueue.Count == 0)
            return;

        var count = _bundle.Scenario.JsonImportReviewQueue.Count;
        _bundle.Scenario.JsonImportReviewQueue.Clear();
        Save();
        RefreshUi();
        SetStatus($"Rejected {count} JSON import proposal(s).");
    }

    private void PersistFieldsFromUi()
    {
        if (_bundle is null)
            return;

        foreach (var (key, box) in _fieldBoxes)
            AdventureDesignService.SetField(_bundle, CurrentStep, key, box.Text);
    }
}

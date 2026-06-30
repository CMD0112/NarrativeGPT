using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Core.LocalInference;
using ChatGPTWrapper.Shell;

namespace ChatGPTWrapper.Views;

public partial class LocalInferenceLabDialog : ShellDialogWindow
{
    public static void ShowForOwner(Window? owner)
    {
        var dialog = new LocalInferenceLabDialog { Owner = owner };
        dialog.ShowDialog();
    }

    private CancellationTokenSource? _runCts;

    private bool _suppressAdventureRefresh;

    private readonly Dictionary<string, CheckBox> _fileChecks = new(StringComparer.OrdinalIgnoreCase);

    public LocalInferenceLabDialog()
    {
        InitializeComponent();

        var defaults = LocalInferenceOptions.FromEnvironment();
        BaseUrlBox.Text = defaults.BaseUrl;
        ModelBox.Text = defaults.Model;

        ScenarioCombo.ItemsSource = LocalInferenceLabScenarios.All;
        ScenarioCombo.DisplayMemberPath = nameof(LocalInferenceLabScenario.Label);
        ScenarioCombo.SelectedValuePath = nameof(LocalInferenceLabScenario.Id);
        ScenarioCombo.SelectedValue = LocalInferenceLabScenarios.EntityExtractionId;

        PopulateFileCheckboxes();
        AdventureCombo.ItemsSource = LocalInferenceLabAdventureContextService.ListAdventures();
        ApplyScenario(LocalInferenceLabScenarios.EntityExtractionId);
    }

    private void PopulateFileCheckboxes()
    {
        FileAttachPanel.Children.Clear();
        _fileChecks.Clear();

        foreach (var spec in LocalInferenceLabAdventureContextService.ListAttachableFileSpecs())
        {
            var checkBox = new CheckBox
            {
                Content = spec.Label,
                IsChecked = spec.DefaultForDiagnostic,
                Tag = spec.Id,
                Margin = new Thickness(0, 0, 16, 6),
            };
            _fileChecks[spec.Id] = checkBox;
            FileAttachPanel.Children.Add(checkBox);
        }
    }

    private void ScenarioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ScenarioCombo.SelectedValue is not string scenarioId)
            return;

        ApplyScenario(scenarioId);
    }

    private void ApplyScenario(string scenarioId)
    {
        if (!LocalInferenceLabScenarios.TryGet(scenarioId, out var scenario))
            return;

        var isDiagnostic = LocalInferenceLabScenarios.IsDiagnosticScenario(scenarioId);
        AdventureContextPanel.Visibility = isDiagnostic ? Visibility.Visible : Visibility.Collapsed;

        if (scenarioId == LocalInferenceLabScenarios.CustomId)
        {
            SystemPromptBox.Text = "";
            if (LocalInferenceLabScenarios.IsKnownUserPrompt(UserMessageBox.Text))
                UserMessageBox.Text = scenario.UserPrompt;
            return;
        }

        SystemPromptBox.Text = scenario.SystemPrompt;
        UserMessageBox.Text = scenario.UserPrompt;

        if (isDiagnostic)
        {
            SystemPromptBox.MaxHeight = 200;
            UserMessageBox.MaxHeight = 360;
            RefreshAdventureContextLists();
        }
        else
        {
            SystemPromptBox.MaxHeight = 140;
            UserMessageBox.MaxHeight = 220;
        }
    }

    private void AdventureContextCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAdventureRefresh)
            return;

        RefreshTurnAndRunLists();
    }

    private void UtilityRunCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressAdventureRefresh)
            return;

        if (UtilityRunCombo.SelectedItem is not LocalInferenceLabUtilityRunRef run)
            return;

        if (run.TurnIndex is not int turnIndex)
            return;

        _suppressAdventureRefresh = true;
        try
        {
            TurnCombo.SelectedItem = TurnCombo.Items
                .OfType<LocalInferenceLabTurnRef>()
                .FirstOrDefault(t => t.Index == turnIndex);
        }
        finally
        {
            _suppressAdventureRefresh = false;
        }
    }

    private void LoadFromAdventureButton_Click(object sender, RoutedEventArgs e)
    {
        if (ScenarioCombo.SelectedValue is not string scenarioId
            || !LocalInferenceLabScenarios.IsDiagnosticScenario(scenarioId))
        {
            AdventureLoadStatusBlock.Text = "Select a Diag: scenario first.";
            return;
        }

        if (AdventureCombo.SelectedItem is not LocalInferenceLabAdventureRef adventure)
        {
            AdventureLoadStatusBlock.Text = "Select an adventure folder.";
            return;
        }

        if (!LocalInferenceLabScenarios.TryGet(scenarioId, out var scenario))
            return;

        var selectedFiles = _fileChecks
            .Where(pair => pair.Value.IsChecked == true)
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (selectedFiles.Count == 0
            && UtilityRunCombo.SelectedItem is not LocalInferenceLabUtilityRunRef
            && TurnCombo.SelectedItem is not LocalInferenceLabTurnRef)
        {
            AdventureLoadStatusBlock.Text = "Check at least one file, or select a turn slice / worker capture.";
            return;
        }

        var turnIndex = (TurnCombo.SelectedItem as LocalInferenceLabTurnRef)?.Index;
        var runId = (UtilityRunCombo.SelectedItem as LocalInferenceLabUtilityRunRef)?.RunId;

        var attachments = LocalInferenceLabAdventureContextService.TryLoadAttachments(
            adventure.Id,
            selectedFiles,
            runId,
            turnIndex);

        if (attachments is null)
        {
            AdventureLoadStatusBlock.Text = "No files found on disk for the current selection.";
            return;
        }

        UserMessageBox.Text = LocalInferenceLabDiagnosticPromptComposer.BuildUserPrompt(scenarioId, attachments);
        SystemPromptBox.Text = LocalInferenceLabDiagnosticPromptComposer.AppendCanonInstructions(scenario.SystemPrompt);

        var fileList = string.Join(", ", attachments.Files.Select(f => f.RelativePath));
        var sizeWarning = attachments.TotalCharacters > 120_000
            ? " · large prompt — consider fewer files"
            : "";
        AdventureLoadStatusBlock.Text =
            $"Attached {attachments.Files.Count} file(s) ({attachments.TotalCharacters:N0} chars): {fileList}{sizeWarning}";
    }

    private void RefreshAdventureContextLists()
    {
        AdventureCombo.ItemsSource = LocalInferenceLabAdventureContextService.ListAdventures();
        RefreshTurnAndRunLists();
    }

    private void RefreshTurnAndRunLists()
    {
        _suppressAdventureRefresh = true;
        try
        {
            if (AdventureCombo.SelectedItem is not LocalInferenceLabAdventureRef adventure)
            {
                TurnCombo.ItemsSource = Array.Empty<LocalInferenceLabTurnRef>();
                UtilityRunCombo.ItemsSource = Array.Empty<LocalInferenceLabUtilityRunRef>();
                return;
            }

            TurnCombo.ItemsSource = LocalInferenceLabAdventureContextService.ListAcceptedTurns(adventure.Id);

            var scenarioId = ScenarioCombo.SelectedValue as string;
            var jobFilter = scenarioId is not null
                ? LocalInferenceLabAdventureContextService.ResolveJobFilterForScenario(scenarioId)
                : null;
            var turnFilter = (TurnCombo.SelectedItem as LocalInferenceLabTurnRef)?.Index;

            UtilityRunCombo.ItemsSource = LocalInferenceLabAdventureContextService.ListUtilityRuns(
                adventure.Id,
                jobFilter,
                turnFilter);
        }
        finally
        {
            _suppressAdventureRefresh = false;
        }
    }

    private LocalInferenceLabScenario? GetSelectedScenario() =>
        ScenarioCombo.SelectedValue is string id && LocalInferenceLabScenarios.TryGet(id, out var scenario)
            ? scenario
            : null;

    private LocalInferenceOptions ReadOptions() =>
        new()
        {
            BaseUrl = string.IsNullOrWhiteSpace(BaseUrlBox.Text)
                ? LocalInferenceOptions.DefaultBaseUrl
                : BaseUrlBox.Text.Trim(),
            Model = string.IsNullOrWhiteSpace(ModelBox.Text)
                ? LocalInferenceOptions.DefaultModel
                : ModelBox.Text.Trim(),
        };

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync(async ct =>
        {
            ProbeStatusBlock.Text = "Probing…";
            using var client = new OpenAiCompatibleChatClient(ReadOptions());
            var health = await client.ProbeAsync(ct);
            if (!health.Reachable)
            {
                ProbeStatusBlock.Text = health.Error ?? "Server unreachable.";
                return;
            }

            var models = health.Models.Count == 0
                ? "(no models listed)"
                : string.Join(", ", health.Models);
            var modelLine = health.RequestedModelAvailable
                ? $"Configured model '{health.RequestedModel}' is available."
                : $"Configured model '{health.RequestedModel}' was not found.";
            ProbeStatusBlock.Text = $"{modelLine} Installed: {models}";
        });
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        var userText = UserMessageBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userText))
        {
            RunStatusBlock.Text = "Enter a user message.";
            return;
        }

        await RunBusyAsync(async ct =>
        {
            RunStatusBlock.Text = "Running…";
            ResponseBox.Clear();

            var options = ReadOptions();
            var scenario = GetSelectedScenario();
            using var client = new OpenAiCompatibleChatClient(options);
            var messages = new List<ChatMessage>();
            if (!string.IsNullOrWhiteSpace(SystemPromptBox.Text))
                messages.Add(ChatMessage.System(SystemPromptBox.Text.Trim()));
            messages.Add(ChatMessage.User(userText));

            var result = await client.CompleteAsync(new ChatCompletionRequest
            {
                Model = options.Model,
                Messages = messages,
                Temperature = scenario?.Temperature ?? 0.7,
                JsonObjectResponse = scenario?.JsonObjectResponse ?? false,
            }, ct);

            if (!result.Success)
            {
                RunStatusBlock.Text = result.Error ?? "Request failed.";
                ResponseBox.Text = result.Error ?? "";
                return;
            }

            ResponseBox.Text = result.Content ?? "";
            RunStatusBlock.Text =
                $"Model: {result.Model ?? options.Model} · finish: {result.FinishReason ?? "—"} · tokens: prompt={result.PromptTokens?.ToString() ?? "—"} completion={result.CompletionTokens?.ToString() ?? "—"}";
        });
    }

    private void CancelRunButton_Click(object sender, RoutedEventArgs e) =>
        _runCts?.Cancel();

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        if (_runCts is not null)
            return;

        _runCts = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await action(_runCts.Token);
        }
        catch (OperationCanceledException)
        {
            RunStatusBlock.Text = "Cancelled.";
        }
        catch (Exception ex)
        {
            RunStatusBlock.Text = ex.Message;
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ProbeButton.IsEnabled = !busy;
        RunButton.IsEnabled = !busy;
        ScenarioCombo.IsEnabled = !busy;
        BaseUrlBox.IsEnabled = !busy;
        ModelBox.IsEnabled = !busy;
        SystemPromptBox.IsEnabled = !busy;
        UserMessageBox.IsEnabled = !busy;
        AdventureCombo.IsEnabled = !busy;
        TurnCombo.IsEnabled = !busy;
        UtilityRunCombo.IsEnabled = !busy;
        LoadFromAdventureButton.IsEnabled = !busy;
        foreach (var checkBox in _fileChecks.Values)
            checkBox.IsEnabled = !busy;
        CancelRunButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }
}

using System.Diagnostics;
using System.IO;
using ChatGPTWrapper;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.WinUI.Services;
using ChatGPTWrapper.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

internal sealed partial class PlaySettingsSourcesTab : UserControl, IPlaySettingsTabPanel
{
    private PlaySettingsWorkbenchContext? _ctx;
    private readonly List<SourcePublishRowViewModel> _sourceRows = [];
    private DebouncedAdventureSaver? _sourceAutosave;

    public PlaySettingsSourcesTab()
    {
        InitializeComponent();
        ApplyCardGridLayout();
    }

    private void OnCardsGridSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyCardGridLayout();

    private void ApplyCardGridLayout() =>
        PlaySettingsCardGridLayout.Apply(
            CardsGrid,
            [ReadinessCard, ActionsCard, SourceFilesCard, InstructionsCard],
            [false, false, true, true],
            ActualWidth);

    public event EventHandler? SettingsChanged;

    public void Bind(PlaySettingsWorkbenchContext context)
    {
        _ctx = context;
        ProjectSourceInjectionService.EnsureLoreSourcesMaterialized(context.Bundle);
        var readiness = ProjectSourceInjectionService.Evaluate(context.Bundle);
        UpdateReadinessBanner(readiness, context.Bundle);

        InstructionsPastedLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(context.Bundle);
        ProbeProjectButton.IsEnabled = !string.IsNullOrWhiteSpace(context.Bundle.Metadata.LinkedProjectId);
        ProbeFileButton.IsEnabled = ProbeProjectButton.IsEnabled;
        ApiSyncDiagnosticsButton.IsEnabled = context.Host?.OpenApiSyncDiagnosticsAsync is not null;

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(context.Bundle);
        CanonicalPathLine.Text = $"Canonical folder: {sourcesDir}";

        _sourceRows.Clear();
        foreach (var entry in context.Bundle.SourceManifest.Entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SourcePublishRowViewModel(entry, sourcesDir, context.Bundle);
            row.ManifestEntryChanged += (_, _) => _sourceAutosave?.SaveNow();
            _sourceRows.Add(row);
        }

        SourcesList.ItemsSource = _sourceRows;
        if (_sourceRows.Count > 0 && SourcesList.SelectedItem is null)
            SourcesList.SelectedIndex = 0;

        _sourceAutosave ??= new DebouncedAdventureSaver(
            () => context.Bundle,
            at => SourceAutosaveLine.Text = $"Source changes saved automatically at {at.LocalDateTime:t}.",
            save: AdventureStore.SaveSourceManifestOnly);
    }

    public void Flush(PlaySettingsWorkbenchContext context) =>
        _sourceAutosave?.SaveNow();

    public void RefreshHostDelegates()
    {
        if (_ctx is null)
            return;

        ApiSyncDiagnosticsButton.IsEnabled = _ctx.Host?.OpenApiSyncDiagnosticsAsync is not null;
    }

    private void UpdateReadinessBanner(ProjectSourceReadiness readiness, AdventureBundle bundle)
    {
        var instructionHint = InstructionSourcesPolicy.FormatInstructionDriftHint(bundle);
        var instructionSuffix = string.IsNullOrWhiteSpace(instructionHint) ? "" : $"\n{instructionHint}";
        var probeSuffix = string.IsNullOrWhiteSpace(readiness.ProbeWarning) ? "" : $"\n{readiness.ProbeWarning}";

        if (readiness.CanDelegateStaticContent)
        {
            ReadinessInfoBar.Severity = InfoBarSeverity.Success;
            ReadinessInfoBar.Title = "Source-delegated packets";
            ReadinessInfoBar.Message =
                $"{readiness.SyncedFiles.Count} file(s) published.{instructionSuffix}{probeSuffix}";
            return;
        }

        if (!readiness.HasLinkedProject)
        {
            ReadinessInfoBar.Severity = InfoBarSeverity.Informational;
            ReadinessInfoBar.Title = "No linked project";
            ReadinessInfoBar.Message = "Link a ChatGPT Project to publish sources." + probeSuffix;
            return;
        }

        ReadinessInfoBar.Severity = InfoBarSeverity.Warning;
        ReadinessInfoBar.Title = "Publish sources manually";
        ReadinessInfoBar.Message =
            $"{readiness.NeedsRepublishCount + readiness.OutOfSyncCount} file(s) need attention.{instructionSuffix}{probeSuffix}";
    }

    private SourcePublishRowViewModel? SelectedRow =>
        SourcesList.SelectedItem as SourcePublishRowViewModel;

    private void SourcesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
    }

    private void RefreshExport_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        ProjectSourceExportService.ExportForce(_ctx.Bundle);
        AdventureStore.SaveSourceManifestOnly(_ctx.Bundle);
        Bind(_ctx);
        OnChanged(sender, e);
    }

    private void OpenSourcesFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        var dir = ProjectSourceExportService.SourcesDirectory(_ctx.Bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void ProbeProject_Click(object sender, RoutedEventArgs e) =>
        _ = _ctx?.Host?.ProbeSourcesAsync?.Invoke() ?? Task.CompletedTask;

    private void ApiSyncDiagnostics_Click(object sender, RoutedEventArgs e) =>
        _ = _ctx?.Host?.OpenApiSyncDiagnosticsAsync?.Invoke() ?? Task.CompletedTask;

    private void SyncSourcesFromJson_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        var result = EntityEditSourceSyncService.RepairFromJson(_ctx.Bundle);
        AdventureStore.Save(_ctx.Bundle, AdventureSaveScope.Scenario | AdventureSaveScope.Entities | AdventureSaveScope.SourceManifest);
        Bind(_ctx);
        _ = WinUiDialogHelper.ShowInfoAsync(App.CurrentMainWindow, "Sync sources from JSON", result.Summary ?? "Done.");
        OnChanged(sender, e);
    }

    private void OpenProjectSettings_Click(object sender, RoutedEventArgs e) =>
        _ = _ctx?.Host?.OpenProjectSettingsAsync?.Invoke() ?? Task.CompletedTask;

    private void ProbeFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null || _ctx?.Host?.ProbeSourceFileAsync is not { } probe)
            return;

        _ = probe(SelectedRow.RelativePath);
    }

    private async void SynthesizeSource_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx?.Host?.SynthesizeSourceAsync is not { } synthesize || SelectedRow is null)
            return;

        var parsed = await File.ReadAllTextAsync(Path.Combine(
            ProjectSourceExportService.SourcesDirectory(_ctx.Bundle),
            SelectedRow.RelativePath));
        await synthesize(SelectedRow.RelativePath, parsed);
    }

    private void ReviewAll_Click(object sender, RoutedEventArgs e) =>
        _ctx?.Host?.OpenProposalReviewHub?.Invoke(ProposalReviewCategory.SourceEdit);

    private void CopyInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        var text = InstructionSourcesPolicy.BuildStaticInstructionsBody(_ctx.Bundle);
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void MarkInstructionsPasted_Click(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        InstructionSourcesPolicy.RecordInstructionsManuallyPublished(_ctx.Bundle);
        AdventureStore.Save(_ctx.Bundle, AdventureSaveScope.Metadata);
        Bind(_ctx);
        OnChanged(sender, e);
    }

    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (_ctx is null)
            return;

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        _ctx.RaiseReviewQueueChanged();
        _ctx.NotifySettingsChanged();
    }
}

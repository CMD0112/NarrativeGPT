using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.ChatGptApi;

namespace ChatGPTWrapper.Views;

public partial class SourceManagerDialog : Window
{
    private static readonly Dictionary<Guid, SourceManagerDialog> OpenByAdventure = new();

    private readonly Guid _adventureId;
    private readonly IChatGptProjectHost _host;
    private AdventureBundle _bundle;
    private readonly ObservableCollection<SourceManagerRowViewModel> _rows = [];
    private readonly DebouncedAdventureSaver _autosave;
    private Point _dragStartPoint;
    private bool _isDragging;

    public bool Saved { get; private set; }

    public event EventHandler? ManagerClosed;

    public Func<Task>? OpenProjectSettingsAsync { get; set; }

    public Func<Task>? OpenApiSyncDiagnosticsAsync { get; set; }

    public Func<string, string, Task<string?>>? SynthesizeSourceAsync { get; set; }

    public static bool TryActivateExisting(Guid adventureId)
    {
        if (!OpenByAdventure.TryGetValue(adventureId, out var existing))
            return false;

        existing.Activate();
        if (existing.WindowState == WindowState.Minimized)
            existing.WindowState = WindowState.Normal;
        return true;
    }

    public static SourceManagerDialog ShowNonModal(
        Guid adventureId,
        IChatGptProjectHost host,
        Window? owner,
        Func<Task>? openProjectSettingsAsync = null)
    {
        if (TryActivateExisting(adventureId) && OpenByAdventure.TryGetValue(adventureId, out var focused))
            return focused;

        var dlg = new SourceManagerDialog(adventureId, host)
        {
            Owner = owner,
            OpenProjectSettingsAsync = openProjectSettingsAsync,
        };
        dlg.Closed += (_, _) =>
        {
            OpenByAdventure.Remove(adventureId);
            dlg.ManagerClosed?.Invoke(dlg, EventArgs.Empty);
        };
        OpenByAdventure[adventureId] = dlg;
        dlg.Show();
        return dlg;
    }

    public SourceManagerDialog(Guid adventureId, IChatGptProjectHost host)
    {
        _adventureId = adventureId;
        _host = host;
        _bundle = AdventureStore.Load(adventureId)
                  ?? throw new InvalidOperationException("Adventure not found.");

        InitializeComponent();
        _autosave = new DebouncedAdventureSaver(() => _bundle, at =>
            StatusLine.Text = $"Saved automatically at {at.LocalDateTime:t}.");
        FilesGrid.ItemsSource = _rows;
        Closed += (_, _) => _autosave.Dispose();
        Loaded += async (_, _) =>
        {
            BindUi();
            await _host.EnsureReadyAsync(_adventureId);
        };
    }

    private SourceManagerRowViewModel? SelectedRow => FilesGrid.SelectedItem as SourceManagerRowViewModel;

    private void BindUi()
    {
        _bundle = AdventureStore.Load(_adventureId) ?? _bundle;
        AdventureSourceFileService.ReconcileManifest(_bundle);
        CanonicalPathLine.Text = $"Canonical folder: {ProjectSourceExportService.SourcesDirectory(_bundle)}";
        InstructionStatusLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(_bundle);
        InstructionDriftLine.Text = InstructionSourcesPolicy.FormatInstructionDriftHint(_bundle);

        var hasProject = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        ProbeAllButton.IsEnabled = hasProject;
        ProbeFileButton.IsEnabled = hasProject;
        OpenProjectSettingsButton.IsEnabled = hasProject;

        var sourcesDir = ProjectSourceExportService.SourcesDirectory(_bundle);
        _rows.Clear();
        foreach (var entry in _bundle.SourceManifest.Entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            var row = new SourceManagerRowViewModel(entry, sourcesDir, _adventureId);
            row.ManifestEntryChanged += (_, _) => _autosave.ScheduleSave();
            _rows.Add(row);
        }

        if (_rows.Count > 0 && FilesGrid.SelectedItem is null)
            FilesGrid.SelectedIndex = 0;

        BindRepublishHints();
        BindHistoryForSelection();
        UpdateCompareButton();
    }

    private void BindRepublishHints()
    {
        var hints = _bundle.SourceManifest.Entries
            .SelectMany(SectionDiffService.GetChangedSectionsSincePublish)
            .ToList();
        var republish = SectionDiffService.FormatRepublishHint(hints);

        var mirrorDir = SourceFileHistoryService.ProjectMirrorDirectory(_adventureId);
        var remoteStoryCards = File.Exists(Path.Combine(mirrorDir, "story-cards.md"));
        var deprecatedNote = remoteStoryCards
            ? "Your ChatGPT Project still has story-cards.md — remove it from Project Files; lore now lives in cast.md / world.md / plot.md sections. "
            : "";

        if (string.IsNullOrWhiteSpace(republish) && string.IsNullOrWhiteSpace(deprecatedNote))
        {
            RepublishHintLine.Text = "";
            RepublishHintLine.Visibility = Visibility.Collapsed;
            return;
        }

        RepublishHintLine.Visibility = Visibility.Visible;
        RepublishHintLine.Text = deprecatedNote
                               + (string.IsNullOrWhiteSpace(republish)
                                   ? ""
                                   : "Sections changed since last publish: " + republish);
    }

    private void BindHistoryForSelection()
    {
        HistoryList.ItemsSource = null;
        if (SelectedRow is null)
            return;

        var history = SourceFileHistoryService.ListHistory(_adventureId, SelectedRow.RelativePath)
            .Select(e => new SourceHistoryRowViewModel(e))
            .ToList();
        HistoryList.ItemsSource = history;
        if (history.Count > 0)
            HistoryList.SelectedIndex = 0;
    }

    private void UpdateCompareButton()
    {
        CompareButton.IsEnabled = SelectedRow?.HasMirror == true;
    }

    private void FilesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        BindHistoryForSelection();
        UpdateCompareButton();
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /* selection only */
    }

    private void RefreshExport_Click(object sender, RoutedEventArgs e)
    {
        ProjectSourceExportService.ExportForce(_bundle);
        AdventureStore.Save(_bundle);
        BindUi();
        StatusLine.Text = "Export refreshed.";
    }

    private void OpenCanonicalFolder_Click(object sender, RoutedEventArgs e) =>
        OpenSourcesFolder(showDragHint: false);

    private void OpenFolderForDrag_Click(object sender, RoutedEventArgs e) =>
        OpenSourcesFolder(showDragHint: true);

    private void OpenSourcesFolder(bool showDragHint)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(_bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        StatusLine.Text = showDragHint
            ? "Explorer opened — drag .md files to ChatGPT Project → Files. Keep this window beside the browser."
            : $"Opened {dir}";
    }

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = SourceFileHistoryService.HistoryRootDirectory(_adventureId);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private async void ProbeAll_Click(object sender, RoutedEventArgs e)
    {
        if (_host.ApiCore is not { } core)
        {
            StatusLine.Text = "ChatGPT browser not ready.";
            return;
        }

        try
        {
            ProbeAllButton.IsEnabled = false;
            StatusLine.Text = "Probing project files…";
            await ProjectSourceProbeService.ProbeAllAsync(core, _bundle, _host.Api, new Progress<string>(s => StatusLine.Text = s));
            BindUi();
            StatusLine.Text = "Probe complete.";
        }
        catch (Exception ex)
        {
            StatusLine.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Probe failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ProbeAllButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        }
    }

    private async void ProbeFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null || _host.ApiCore is not { } core)
            return;

        try
        {
            ProbeFileButton.IsEnabled = false;
            StatusLine.Text = $"Probing {SelectedRow.RelativePath}…";
            await ProjectSourceProbeService.ProbeFileAsync(core, _bundle, _host.Api, SelectedRow.RelativePath);
            SelectedRow.RefreshDisplay();
            UpdateCompareButton();
            StatusLine.Text = $"Probed {SelectedRow.RelativePath}: {SelectedRow.ProjectMatch}";
        }
        catch (Exception ex)
        {
            StatusLine.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Probe failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ProbeFileButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        }
    }

    private void MarkAllUploaded_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows)
            row.IsPublished = true;

        AdventureStore.Save(_bundle);
        StatusLine.Text = "All files marked uploaded.";
    }

    private void MarkUploaded_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
            return;

        SelectedRow.IsPublished = true;
        AdventureStore.Save(_bundle);
    }

    private void OpenCanonical_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null || !File.Exists(SelectedRow.AbsolutePath))
            return;

        Process.Start(new ProcessStartInfo(SelectedRow.AbsolutePath) { UseShellExecute = true });
    }

    private void CopyFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null || !File.Exists(SelectedRow.AbsolutePath))
            return;

        try
        {
            Clipboard.SetText(File.ReadAllText(SelectedRow.AbsolutePath));
            StatusLine.Text = $"Copied {SelectedRow.RelativePath}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Compare_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null || !SelectedRow.HasMirror)
            return;

        var mirrorPath = ProjectSourceProbeService.MirrorFilePath(_adventureId, SelectedRow.RelativePath);
        var compareDialog = SourceCompareDialog.FromPaths(
            SelectedRow.AbsolutePath,
            mirrorPath,
            "Canonical",
            "Project mirror",
            SelectedRow.Entry.EffectiveLocalSha256,
            SelectedRow.Entry.LastRemoteProbeSha256,
            SelectedRow.Entry.ManuallyPublishedSha256);
        compareDialog.Owner = this;
        compareDialog.ShowDialog();
    }

    private void ViewHistory_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null)
            return;

        HistoryList.Focus();
        BindHistoryForSelection();
    }

    private void RestoreVersion_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not SourceHistoryRowViewModel historyRow)
        {
            MessageBox.Show(this, "Select a history version to restore.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Restore {SelectedRow?.RelativePath} from {historyRow.Entry.ArchivedAt.LocalDateTime:g}? "
                + "Published status will be cleared for this file.",
                "Restore version",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        if (!SourceFileHistoryService.RestoreVersion(_bundle, historyRow.Entry))
        {
            MessageBox.Show(this, "Archive file missing.", "Restore failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        AdventureStore.Save(_bundle);
        BindUi();
        StatusLine.Text = $"Restored {historyRow.Entry.RelativePath}.";
    }

    private void PreviewArchive_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryList.SelectedItem is not SourceHistoryRowViewModel historyRow)
            return;

        var path = SourceFileHistoryService.ResolveArchiveAbsolutePath(_adventureId, historyRow.Entry);
        if (!File.Exists(path))
            return;

        new ContextViewerDialog(
            File.ReadAllText(path),
            $"{historyRow.Entry.RelativePath} · {historyRow.Entry.ArchivedAt.LocalDateTime:g} · {SourceManifestHelper.ShortHash(historyRow.Entry.Sha256)}")
        {
            Owner = this,
        }.ShowDialog();
    }

    private void CompareArchive_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is null || HistoryList.SelectedItem is not SourceHistoryRowViewModel historyRow)
            return;

        var archivePath = SourceFileHistoryService.ResolveArchiveAbsolutePath(_adventureId, historyRow.Entry);
        var compareDialog = SourceCompareDialog.FromPaths(
            archivePath,
            SelectedRow.AbsolutePath,
            "Archive",
            "Current canonical",
            historyRow.Entry.Sha256,
            SelectedRow.Entry.EffectiveLocalSha256);
        compareDialog.Owner = this;
        compareDialog.ShowDialog();
    }

    private void DesignInstructions_Click(object sender, RoutedEventArgs e)
    {
        if (InstructionDesignerDialog.Show(this, _bundle.Metadata.Id) == true)
        {
            _bundle = AdventureStore.Load(_bundle.Metadata.Id) ?? _bundle;
            InstructionStatusLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(_bundle);
            InstructionDriftLine.Text = InstructionSourcesPolicy.FormatInstructionDriftHint(_bundle);
            StatusLine.Text = "Instructions designer saved.";
        }
    }

    private void CopyInstructions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(InstructionSourcesPolicy.BuildStaticInstructionsBody(_bundle));
            StatusLine.Text = "Instructions copied.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Copy failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void PreviewInstructions_Click(object sender, RoutedEventArgs e)
    {
        new ContextViewerDialog(
            InstructionSourcesPolicy.BuildStaticInstructionsBody(_bundle),
            "Project custom instructions preview")
        {
            Owner = this,
        }.ShowDialog();
    }

    private void MarkInstructionsPasted_Click(object sender, RoutedEventArgs e)
    {
        InstructionSourcesPolicy.RecordInstructionsManuallyPublished(_bundle);
        _autosave.SaveNow();
        InstructionStatusLine.Text = InstructionSourcesPolicy.FormatInstructionsManuallyPublished(_bundle);
        StatusLine.Text = "Instructions marked pasted.";
    }

    private async void OpenProjectSettings_Click(object sender, RoutedEventArgs e)
    {
        if (OpenProjectSettingsAsync is not null)
            await OpenProjectSettingsAsync();
    }

    private void FilesGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        if (sender is not DataGrid grid || grid.SelectedItem is not SourceManagerRowViewModel row)
            return;

        if (!File.Exists(row.AbsolutePath))
            return;

        _isDragging = true;
        try
        {
            var data = new DataObject(DataFormats.FileDrop, new[] { row.AbsolutePath });
            DragDrop.DoDragDrop(grid, data, DragDropEffects.Copy);
        }
        finally
        {
            _isDragging = false;
        }
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        base.OnPreviewMouseLeftButtonDown(e);
    }

    private async void ApiSyncDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (OpenApiSyncDiagnosticsAsync is not null)
            await OpenApiSyncDiagnosticsAsync();
    }

    private async void SynthesizeSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedRow is not { } row)
        {
            MessageBox.Show(this, "Select a source file first.", "Synthesize source");
            return;
        }

        if (SynthesizeSourceAsync is null)
        {
            MessageBox.Show(this, "Synthesis is not available from this host.", "Synthesize source");
            return;
        }

        var parsed = PromptForParsedUtilityOutput(this, row.RelativePath);
        if (string.IsNullOrWhiteSpace(parsed))
            return;

        StatusLine.Text = "Running synthesize_source job…";
        var synthesized = await SynthesizeSourceAsync(row.RelativePath, parsed);
        if (string.IsNullOrWhiteSpace(synthesized))
        {
            StatusLine.Text = "Synthesis failed or returned empty output.";
            return;
        }

        var current = File.Exists(row.AbsolutePath) ? File.ReadAllText(row.AbsolutePath) : "";
        var tempSynth = Path.Combine(Path.GetTempPath(), $"cgw-synth-{Guid.NewGuid():N}.md");
        var tempCurrent = Path.Combine(Path.GetTempPath(), $"cgw-current-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(tempCurrent, current);
            File.WriteAllText(tempSynth, synthesized);
            var compareDialog = SourceCompareDialog.FromPaths(tempCurrent, tempSynth, "Current", "Synthesized");
            compareDialog.Owner = this;
            compareDialog.ShowDialog();

            if (MessageBox.Show(
                    this,
                    $"Write synthesized content to {row.RelativePath}?",
                    "Synthesize source",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                StatusLine.Text = "Synthesis preview dismissed.";
                return;
            }

            SourceSynthesisService.WriteSynthesizedFile(_bundle, row.RelativePath, synthesized);
            AdventureStore.Save(_bundle);
            BindUi();
            StatusLine.Text = $"Wrote synthesized content to {row.RelativePath}.";
        }
        finally
        {
            try { if (File.Exists(tempSynth)) File.Delete(tempSynth); } catch { /* ignore */ }
            try { if (File.Exists(tempCurrent)) File.Delete(tempCurrent); } catch { /* ignore */ }
        }
    }

    private static string? PromptForParsedUtilityOutput(Window owner, string targetPath)
    {
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinWidth = 420,
            MinHeight = 160,
            Margin = new Thickness(12),
        };
        var prompt = new TextBlock
        {
            Text = $"Paste utility output to merge into {targetPath}:",
            Margin = new Thickness(12, 12, 12, 0),
            TextWrapping = TextWrapping.Wrap,
        };
        string? result = null;
        var ok = new Button { Content = "Run synthesis", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 12) };
        Window? dialog = null;
        ok.Click += (_, _) =>
        {
            result = box.Text;
            if (dialog is not null)
                dialog.DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 12, 12), IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel();
        panel.Children.Add(prompt);
        panel.Children.Add(box);
        panel.Children.Add(buttons);
        dialog = new Window
        {
            Title = "Synthesize source",
            Owner = owner,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = owner.Background,
            Foreground = owner.Foreground,
        };
        cancel.Click += (_, _) =>
        {
            if (dialog is not null)
                dialog.DialogResult = false;
        };
        return dialog.ShowDialog() == true ? result?.Trim() : null;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        AdventureStore.Save(_bundle);
        Saved = true;
        StatusLine.Text = "Saved.";
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (Saved)
        {
            Close();
            return;
        }

        AdventureStore.Save(_bundle);
        Close();
    }
}

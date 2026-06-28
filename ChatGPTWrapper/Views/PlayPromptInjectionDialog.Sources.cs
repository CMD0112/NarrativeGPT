using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.Canon;
using ChatGPTWrapper.Adventure.Stores;

namespace ChatGPTWrapper.Views;

public partial class PlayPromptInjectionDialog
{
    public Func<Task>? OpenApiSyncDiagnosticsAsync { get; set; }

    public Func<string, Task>? ProbeSourceFileAsync { get; set; }

    public Func<string, string, Task<string?>>? SynthesizeSourceAsync { get; set; }

    private SourcePublishRowViewModel? SelectedSourceRow =>
        SourcesGrid.SelectedItem as SourcePublishRowViewModel;

    private void BindSourceRepublishHints()
    {
        var hints = _bundle.SourceManifest.Entries
            .SelectMany(SectionDiffService.GetChangedSectionsSincePublish)
            .ToList();
        var republish = SectionDiffService.FormatRepublishHint(hints);

        var mirrorDir = SourceFileHistoryService.ProjectMirrorDirectory(_bundle.Metadata.Id);
        var remoteStoryCards = File.Exists(Path.Combine(mirrorDir, "story-cards.md"));
        var deprecatedNote = remoteStoryCards
            ? "Your ChatGPT Project still has story-cards.md — remove it from Project Files. "
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

    private void BindSourceHistory()
    {
        SourceHistoryList.ItemsSource = null;
        if (SelectedSourceRow is null)
            return;

        var history = SourceFileHistoryService.ListHistory(_bundle.Metadata.Id, SelectedSourceRow.RelativePath)
            .Select(e => new SourceHistoryRowViewModel(e))
            .ToList();
        SourceHistoryList.ItemsSource = history;
        if (history.Count > 0)
            SourceHistoryList.SelectedIndex = 0;
    }

    private void SourceHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        /* selection only */
    }

    private void CheckCanon_Click(object sender, RoutedEventArgs e)
    {
        var issues = CanonValidationService.ValidateBundle(_bundle);
        if (issues.Count == 0)
        {
            MessageBox.Show(
                this,
                "All sectioned lore files pass canon validation.",
                "Check canon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var errors = issues.Count(i => i.Severity == CanonValidationSeverity.Error);
        var body = string.Join(
            Environment.NewLine,
            issues.Take(40).Select(i => i.ToString()));
        if (issues.Count > 40)
            body += Environment.NewLine + $"... and {issues.Count - 40} more.";

        MessageBox.Show(
            this,
            body,
            "Check canon",
            MessageBoxButton.OK,
            errors > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void OpenFolderForDrag_Click(object sender, RoutedEventArgs e) =>
        OpenSourcesFolder(showDragHint: true);

    private void OpenHistoryFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = SourceFileHistoryService.HistoryRootDirectory(_bundle.Metadata.Id);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    private void OpenSourcesFolder_Click(object sender, RoutedEventArgs e) =>
        OpenSourcesFolder(showDragHint: false);

    private void OpenSourcesFolder(bool showDragHint)
    {
        var dir = ProjectSourceExportService.SourcesDirectory(_bundle);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        if (showDragHint)
            SourceAutosaveLine.Text = "Explorer opened — drag .md files to ChatGPT Project → Files.";
    }

    private void OpenSelectedSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSourceRow is null || !File.Exists(SelectedSourceRow.AbsolutePath))
            return;

        Process.Start(new ProcessStartInfo(SelectedSourceRow.AbsolutePath) { UseShellExecute = true });
    }

    private void MarkSelectedPublished_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSourceRow is null)
            return;

        SelectedSourceRow.IsPublished = true;
        _sourceAutosave?.SaveNow();
    }

    private async void ProbeFile_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSourceRow is null || ProbeSourceFileAsync is null)
            return;

        try
        {
            ProbeFileButton.IsEnabled = false;
            await ProbeSourceFileAsync(SelectedSourceRow.RelativePath);
            ReloadBundleFromStore();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Probe failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ProbeFileButton.IsEnabled = !string.IsNullOrWhiteSpace(_bundle.Metadata.LinkedProjectId);
        }
    }

    private void RestoreSourceVersion_Click(object sender, RoutedEventArgs e)
    {
        if (SourceHistoryList.SelectedItem is not SourceHistoryRowViewModel historyRow)
        {
            MessageBox.Show(this, "Select a history version to restore.", "Restore", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (MessageBox.Show(
                this,
                $"Restore {SelectedSourceRow?.RelativePath} from {historyRow.Entry.ArchivedAt.LocalDateTime:g}? "
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
        BindSources();
    }

    private void PreviewSourceArchive_Click(object sender, RoutedEventArgs e)
    {
        if (SourceHistoryList.SelectedItem is not SourceHistoryRowViewModel historyRow)
            return;

        var path = SourceFileHistoryService.ResolveArchiveAbsolutePath(_bundle.Metadata.Id, historyRow.Entry);
        if (!File.Exists(path))
            return;

        new ContextViewerDialog(
            File.ReadAllText(path),
            $"{historyRow.Entry.RelativePath} · {historyRow.Entry.ArchivedAt.LocalDateTime:g} · {SourceManifestHelper.ShortHash(historyRow.Entry.Sha256)}")
        {
            Owner = this,
        }.ShowDialog();
    }

    private void CompareSourceArchive_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSourceRow is null || SourceHistoryList.SelectedItem is not SourceHistoryRowViewModel historyRow)
            return;

        var archivePath = SourceFileHistoryService.ResolveArchiveAbsolutePath(_bundle.Metadata.Id, historyRow.Entry);
        var compareDialog = SourceCompareDialog.FromPaths(
            archivePath,
            SelectedSourceRow.AbsolutePath,
            "Archive",
            "Current canonical",
            historyRow.Entry.Sha256,
            SelectedSourceRow.Entry.EffectiveLocalSha256);
        compareDialog.Owner = this;
        compareDialog.ShowDialog();
    }

    private async void ApiSyncDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (OpenApiSyncDiagnosticsAsync is not null)
            await OpenApiSyncDiagnosticsAsync();
    }

    private async void SynthesizeSource_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedSourceRow is not { } row)
        {
            MessageBox.Show(this, "Select a source file first.", "Synthesize source");
            return;
        }

        if (SynthesizeSourceAsync is null)
        {
            MessageBox.Show(this, "Synthesis is not available from this host.", "Synthesize source");
            return;
        }

        var parsed = PromptForParsedUtilityOutput(row.RelativePath);
        if (string.IsNullOrWhiteSpace(parsed))
            return;

        var synthesized = await SynthesizeSourceAsync(row.RelativePath, parsed);
        if (string.IsNullOrWhiteSpace(synthesized))
        {
            MessageBox.Show(this, "Synthesis failed or returned empty output.", "Synthesize source",
                MessageBoxButton.OK, MessageBoxImage.Warning);
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
                return;

            SourceSynthesisService.WriteSynthesizedFile(_bundle, row.RelativePath, synthesized);
            AdventureStore.Save(_bundle);
            BindSources();
        }
        finally
        {
            try { if (File.Exists(tempSynth)) File.Delete(tempSynth); } catch { /* ignore */ }
            try { if (File.Exists(tempCurrent)) File.Delete(tempCurrent); } catch { /* ignore */ }
        }
    }

    private string? PromptForParsedUtilityOutput(string targetPath)
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
            Owner = this,
            Content = panel,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Background,
            Foreground = Foreground,
        };
        cancel.Click += (_, _) =>
        {
            if (dialog is not null)
                dialog.DialogResult = false;
        };
        return dialog.ShowDialog() == true ? result?.Trim() : null;
    }
}

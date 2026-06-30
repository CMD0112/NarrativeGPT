using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.ChatGptApi;
using ChatGPTWrapper.Shell;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

/// <summary>Optional reference files + instructions before running an AI Tool utility job.</summary>
internal sealed class UtilityJobAttachmentLaunchDialog : ShellDialogWindow
{
    private readonly TextBox _referenceBox;
    private readonly ListBox _fileList;
    private readonly ObservableCollection<string> _paths = [];

    public UtilityJobAttachmentLaunchResult? Result { get; private set; }

    private UtilityJobAttachmentLaunchDialog(string jobLabel, string defaultReferenceNote, IEnumerable<string> suggestedPaths)
    {
        Title = $"Run {jobLabel}";
        Width = 560;
        MinWidth = 480;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Text =
                "Optionally attach reference files (e.g. entities.json). "
                + "Text/json files are embedded in the API job packet; images and other binary files use a hidden composer upload (play tab stays selected).",
        });

        root.Children.Add(new TextBlock { Text = "Reference instructions", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _referenceBox = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 72,
            Margin = new Thickness(0, 0, 0, 12),
            Text = defaultReferenceNote,
        };
        root.Children.Add(_referenceBox);

        root.Children.Add(new TextBlock { Text = "Attached files", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        _fileList = new ListBox { MinHeight = 72, Margin = new Thickness(0, 0, 0, 8), ItemsSource = _paths };
        root.Children.Add(_fileList);

        var fileButtons = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        var addButton = new Button { Content = "Add files…", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        addButton.Click += (_, _) => AddFiles();
        var removeButton = new Button { Content = "Remove", Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 8, 0) };
        removeButton.Click += (_, _) => RemoveSelected();
        var suggestButton = new Button { Content = "Add suggested", Padding = new Thickness(10, 5, 10, 5) };
        suggestButton.Click += (_, _) => AddSuggested(suggestedPaths);
        fileButtons.Children.Add(addButton);
        fileButtons.Children.Add(removeButton);
        fileButtons.Children.Add(suggestButton);
        root.Children.Add(fileButtons);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Cancel", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        var run = new Button { Content = "Run job", Padding = new Thickness(12, 6, 12, 6), IsDefault = true };
        run.Click += (_, _) => Confirm();
        actions.Children.Add(cancel);
        actions.Children.Add(run);
        root.Children.Add(actions);

        Content = root;
        Loaded += (_, _) =>
        {
            foreach (var path in suggestedPaths.Where(File.Exists))
                TryAddPath(path);
            _referenceBox.Focus();
            _referenceBox.SelectAll();
        };
    }

    public static bool TryShow(
        Window owner,
        string jobLabel,
        string defaultReferenceNote,
        IEnumerable<string> suggestedPaths,
        out UtilityJobAttachmentLaunchResult? result)
    {
        var dialog = new UtilityJobAttachmentLaunchDialog(jobLabel, defaultReferenceNote, suggestedPaths)
        {
            Owner = owner,
        };
        if (dialog.ShowDialog() != true || dialog.Result is null)
        {
            result = null;
            return false;
        }

        result = dialog.Result;
        return true;
    }

    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter =
                "Reference files|*.json;*.pdf;*.png;*.jpg;*.jpeg;*.gif;*.webp;*.md;*.txt;*.docx"
                + "|All files|*.*",
        };
        if (dlg.ShowDialog(this) != true)
            return;

        foreach (var path in dlg.FileNames)
            TryAddPath(path);
    }

    private void AddSuggested(IEnumerable<string> suggestedPaths)
    {
        foreach (var path in suggestedPaths)
            TryAddPath(path);
    }

    private void TryAddPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        var full = Path.GetFullPath(path);
        if (_paths.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase)))
            return;

        _paths.Add(full);
    }

    private void RemoveSelected()
    {
        if (_fileList.SelectedItem is string path)
            _paths.Remove(path);
    }

    private void Confirm()
    {
        var note = _referenceBox.Text.Trim();
        var attachments = UtilityJobAttachmentStaging.LoadFromPaths(_paths);
        Result = new UtilityJobAttachmentLaunchResult
        {
            Attachments = attachments,
            ReferenceNote = string.IsNullOrWhiteSpace(note) ? null : note,
        };
        DialogResult = true;
        Close();
    }
}

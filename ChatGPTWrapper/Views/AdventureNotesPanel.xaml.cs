using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ChatGPTWrapper.Adventure.Models;
using ChatGPTWrapper.Adventure.Services;
using ChatGPTWrapper.Adventure.Services.PlayLayout;
using ChatGPTWrapper.Adventure.Stores;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class AdventureNotesPanel : UserControl
{
    private const int AutosaveDelayMs = 400;

    private AdventureBundle? _bundle;
    private DebouncedAdventureSaver? _autosave;
    private bool _suppressTextChanged;
    private bool _useCompactChrome;
    private List<int> _findMatchOffsets = [];
    private int _findMatchIndex = -1;
    private bool _suppressSectionJump;
    private IReadOnlyList<NotesSection> _sections = [];

    public AdventureNotesPanel()
    {
        InitializeComponent();
    }

    public Func<NotesInsertContext>? ResolveNotesInsertContext { get; set; }

    public event EventHandler? NotesContentChanged;

    public void LoadAdventure(Guid id)
    {
        _autosave?.SaveNow();
        _autosave?.Dispose();
        _autosave = null;

        _bundle = AdventureStore.Load(id);
        if (_bundle is null)
            return;

        _suppressTextChanged = true;
        NotesBox.Text = _bundle.Notes ?? "";
        _suppressTextChanged = false;

        _autosave = new DebouncedAdventureSaver(() => _bundle, OnNotesSaved, AutosaveDelayMs);
        UpdateWordCount();
        UpdateSaveStatus(saved: true, savedAt: null);
        RefreshSections();
        ResetFind();
    }

    public string GetNotesPreviewLine()
    {
        var text = NotesBox.Text ?? "";
        if (string.IsNullOrWhiteSpace(text))
            return "Player notes (empty)";

        var words = CountWords(text);
        var wordPart = words == 1 ? "1 word" : $"{words} words";
        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(first))
            return $"{wordPart} · (empty)";

        var preview = first.Length > 40 ? first[..37] + "…" : first;
        return $"{wordPart} · {preview}";
    }

    public void FocusEditor()
    {
        NotesBox.Focus();
        NotesBox.CaretIndex = NotesBox.Text.Length;
        NotesBox.ScrollToEnd();
    }

    public void SaveConfiguration()
    {
        SyncNotesToBundle();
        _autosave?.SaveNow();
    }

    private void SyncNotesToBundle()
    {
        if (_bundle is null)
            return;

        _bundle.Notes = NotesBox.Text ?? "";
    }

    private void OnNotesSaved(DateTimeOffset savedAt) =>
        Dispatcher.Invoke(() => UpdateSaveStatus(saved: true, savedAt: savedAt));

    private void NotesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWordCount();
        if (_suppressTextChanged)
            return;

        SyncNotesToBundle();
        UpdateSaveStatus(saved: false, savedAt: null);
        _autosave?.ScheduleSave();
        RefreshSections();
        if (FindRow.Visibility == Visibility.Visible)
            RefreshFindMatches(selectMatch: _findMatchIndex >= 0, preserveIndex: true);
        NotesContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotesBox_SelectionChanged(object sender, RoutedEventArgs e) =>
        SyncSectionJumpToCaret();

    private void UpdateWordCount()
    {
        var text = NotesBox.Text ?? "";
        var words = CountWords(text);
        var chars = text.Length;
        var wordLabel = words == 1 ? "1 word" : $"{words} words";
        NotesWordCountBlock.Text = chars == 0 ? wordLabel : $"{wordLabel} · {chars:N0} chars";
    }

    private static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    private void UpdateSaveStatus(bool saved, DateTimeOffset? savedAt)
    {
        if (saved && savedAt is { } at)
            NotesSaveStatusBlock.Text = $"Saved {at.LocalDateTime:t}";
        else if (saved)
            NotesSaveStatusBlock.Text = "Saved";
        else if (_autosave is not null)
            NotesSaveStatusBlock.Text = "Saving…";
        else
            NotesSaveStatusBlock.Text = "";
    }

    private void NotesBox_LostFocus(object sender, RoutedEventArgs e) =>
        SaveConfiguration();

    private void NotesBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowFindRow(focusQuery: true);
            e.Handled = true;
            return;
        }

        if (FindRow.Visibility != Visibility.Visible)
            return;

        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                FindPrevious();
            else
                FindNext();
            e.Handled = true;
        }
    }

    private void FindToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (FindRow.Visibility == Visibility.Visible)
            HideFindRow(restoreEditorFocus: true);
        else
            ShowFindRow(focusQuery: true);
    }

    private void ShowFindRow(bool focusQuery)
    {
        FindRow.Visibility = Visibility.Visible;
        RefreshFindMatches(selectMatch: false);
        if (focusQuery)
        {
            FindQueryBox.Focus();
            FindQueryBox.SelectAll();
        }
    }

    private void HideFindRow(bool restoreEditorFocus)
    {
        FindRow.Visibility = Visibility.Collapsed;
        _findMatchIndex = -1;
        UpdateFindMatchCount();
        if (restoreEditorFocus)
            NotesBox.Focus();
    }

    private void FindCloseButton_Click(object sender, RoutedEventArgs e) =>
        HideFindRow(restoreEditorFocus: true);

    private void FindQueryBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshFindMatches(selectMatch: false);

    private void FindCaseSensitiveToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (FindRow.Visibility != Visibility.Visible)
            return;

        RefreshFindMatches(selectMatch: _findMatchIndex >= 0, preserveIndex: true);
    }

    private void FindQueryBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                FindPrevious();
            else
                FindNext();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideFindRow(restoreEditorFocus: true);
            e.Handled = true;
        }
    }

    private void FindPrevButton_Click(object sender, RoutedEventArgs e) => FindPrevious();

    private void FindNextButton_Click(object sender, RoutedEventArgs e) => FindNext();

    private void RefreshFindMatches(bool selectMatch, bool preserveIndex = false)
    {
        var query = FindQueryBox.Text;
        var text = NotesBox.Text ?? "";
        var previousOffset = preserveIndex && _findMatchIndex >= 0 && _findMatchIndex < _findMatchOffsets.Count
            ? _findMatchOffsets[_findMatchIndex]
            : -1;

        _findMatchOffsets = NotesFindService.FindMatchOffsets(
            text,
            query,
            FindCaseSensitiveToggle.IsChecked == true);

        if (_findMatchOffsets.Count == 0)
        {
            _findMatchIndex = -1;
            UpdateFindMatchCount();
            return;
        }

        if (preserveIndex && previousOffset >= 0)
            _findMatchIndex = NotesFindService.FindBestMatchIndex(_findMatchOffsets, previousOffset);
        else if (!selectMatch)
            _findMatchIndex = -1;

        UpdateFindMatchCount();
        if (selectMatch && _findMatchIndex >= 0)
            RevealFindMatch(_findMatchIndex, returnFocusToFind: !NotesBox.IsKeyboardFocusWithin);
    }

    private void UpdateFindMatchCount()
    {
        var query = FindQueryBox.Text;
        if (string.IsNullOrEmpty(query))
        {
            FindMatchCountBlock.Text = "";
            return;
        }

        if (_findMatchOffsets.Count == 0)
        {
            FindMatchCountBlock.Text = "No matches";
            return;
        }

        FindMatchCountBlock.Text = _findMatchIndex >= 0
            ? $"{_findMatchIndex + 1} of {_findMatchOffsets.Count}"
            : FormatMatchCount(_findMatchOffsets.Count);
    }

    private static string FormatMatchCount(int count) =>
        count == 1 ? "1 match" : $"{count} matches";

    private void FindNext()
    {
        if (_findMatchOffsets.Count == 0)
        {
            RefreshFindMatches(selectMatch: false);
            if (_findMatchOffsets.Count == 0)
                return;
        }

        _findMatchIndex = _findMatchIndex < 0
            ? 0
            : (_findMatchIndex + 1) % _findMatchOffsets.Count;
        RevealFindMatch(_findMatchIndex, returnFocusToFind: true);
    }

    private void FindPrevious()
    {
        if (_findMatchOffsets.Count == 0)
        {
            RefreshFindMatches(selectMatch: false);
            if (_findMatchOffsets.Count == 0)
                return;
        }

        _findMatchIndex = _findMatchIndex < 0
            ? _findMatchOffsets.Count - 1
            : _findMatchIndex <= 0
                ? _findMatchOffsets.Count - 1
                : _findMatchIndex - 1;
        RevealFindMatch(_findMatchIndex, returnFocusToFind: true);
    }

    private void RevealFindMatch(int index, bool returnFocusToFind)
    {
        if (index < 0 || index >= _findMatchOffsets.Count)
            return;

        var offset = _findMatchOffsets[index];
        var text = NotesBox.Text ?? "";
        NotesBox.CaretIndex = offset;
        NotesBox.SelectionLength = 0;
        NotesBox.ScrollToLine(GetLineIndex(text, offset));
        UpdateFindMatchCount();

        if (returnFocusToFind && FindRow.Visibility == Visibility.Visible)
            FindQueryBox.Focus();
    }

    private static int GetLineIndex(string text, int charOffset)
    {
        var line = 0;
        for (var i = 0; i < charOffset && i < text.Length; i++)
        {
            if (text[i] == '\n')
                line++;
        }

        return line;
    }

    private void ResetFind()
    {
        FindQueryBox.Text = "";
        FindCaseSensitiveToggle.IsChecked = false;
        FindRow.Visibility = Visibility.Collapsed;
        _findMatchOffsets.Clear();
        _findMatchIndex = -1;
        FindMatchCountBlock.Text = "";
    }

    private void InsertMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        var ctx = ResolveNotesInsertContext?.Invoke()
                  ?? new NotesInsertContext(0, null);

        menu.Items.Add(CreateInsertItem("Timestamp", () => InsertAtCaret($"[{DateTime.Now:yyyy-MM-dd HH:mm}] ")));
        menu.Items.Add(CreateInsertItem(
            ctx.AcceptedTurnCount == 1 ? "Turn 1" : $"Turn {ctx.AcceptedTurnCount}",
            () => InsertAtCaret($"[Turn {ctx.AcceptedTurnCount}] ")));
        var entityItem = CreateInsertItem(
            string.IsNullOrWhiteSpace(ctx.SelectedEntityName) ? "Entity name" : ctx.SelectedEntityName,
            () => InsertAtCaret(ctx.SelectedEntityName!));
        entityItem.IsEnabled = !string.IsNullOrWhiteSpace(ctx.SelectedEntityName);
        menu.Items.Add(entityItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateInsertItem("Section heading", InsertSectionHeading));

        menu.PlacementTarget = InsertMenuButton;
        menu.IsOpen = true;
    }

    private void InsertSectionHeading()
    {
        var text = NotesBox.Text ?? "";
        var caret = NotesBox.CaretIndex;
        var lineStart = caret;
        while (lineStart > 0 && text[lineStart - 1] != '\n')
            lineStart--;

        var prefix = lineStart == caret ? "## " : "\n## ";
        InsertAtCaret(prefix);
    }

    private static MenuItem CreateInsertItem(string header, Action insert) =>
        new()
        {
            Header = header,
            Command = new RelayCommand(_ => insert()),
        };

    private void InsertAtCaret(string text)
    {
        var caret = NotesBox.CaretIndex;
        var current = NotesBox.Text ?? "";
        NotesBox.Text = current.Insert(caret, text);
        NotesBox.CaretIndex = caret + text.Length;
        NotesBox.Focus();
    }

    private void MoreMenuButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        if (_useCompactChrome && _sections.Count > 0)
        {
            var jumpMenu = new MenuItem { Header = "Jump to section" };
            foreach (var section in _sections)
            {
                var captured = section;
                jumpMenu.Items.Add(CreateInsertItem(captured.Title, () => JumpToSection(captured)));
            }

            menu.Items.Add(jumpMenu);
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(CreateInsertItem("Copy all", CopyAllNotes));
        menu.Items.Add(CreateInsertItem("Select all", SelectAllNotes));
        menu.Items.Add(CreateInsertItem("Export…", ExportNotes));
        menu.PlacementTarget = MoreMenuButton;
        menu.IsOpen = true;
    }

    private void SelectAllNotes()
    {
        NotesBox.Focus();
        NotesBox.SelectAll();
    }

    private void CopyAllNotes()
    {
        var text = NotesBox.Text ?? "";
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
    }

    private void ExportNotes()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export player notes",
            FileName = "notes.txt",
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = ".txt",
        };

        if (dialog.ShowDialog() != true)
            return;

        File.WriteAllText(dialog.FileName, NotesBox.Text ?? "");
    }

    private void RefreshSections()
    {
        _sections = NotesSectionParser.Parse(NotesBox.Text);
        _suppressSectionJump = true;
        SectionJumpCombo.ItemsSource = _sections;
        SectionJumpCombo.SelectedIndex = -1;
        _suppressSectionJump = false;

        SectionJumpCombo.Visibility = _sections.Count > 0 && !_useCompactChrome
            ? Visibility.Visible
            : Visibility.Collapsed;

        SyncSectionJumpToCaret();
    }

    private void SyncSectionJumpToCaret()
    {
        if (_sections.Count == 0 || _useCompactChrome)
            return;

        var sectionIndex = NotesSectionParser.GetSectionIndexForOffset(_sections, NotesBox.CaretIndex);
        if (sectionIndex is not { } index)
            return;

        if (SectionJumpCombo.SelectedIndex == index)
            return;

        _suppressSectionJump = true;
        SectionJumpCombo.SelectedIndex = index;
        _suppressSectionJump = false;
    }

    private void SectionJumpCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSectionJump || SectionJumpCombo.SelectedItem is not NotesSection section)
            return;

        JumpToSection(section);
    }

    private void JumpToSection(NotesSection section)
    {
        NotesBox.Focus();
        NotesBox.CaretIndex = section.CharOffset;
        NotesBox.Select(section.CharOffset, section.Title.Length + 3);
        NotesBox.ScrollToLine(section.LineIndex);
    }

    public void ApplyLayout(PlayLayoutContext context)
    {
        if (!context.IsUsable)
            return;

        _useCompactChrome = context.Capabilities.UseCompactNotesChrome;
        NotesHostBorder.Margin = new Thickness(context.Margin);
        NotesHostBorder.Padding = _useCompactChrome
            ? new Thickness(6)
            : new Thickness(10);

        FindToggleButton.Content = _useCompactChrome ? "🔍" : "Find";
        InsertMenuButton.Content = _useCompactChrome ? "+" : "Insert";
        MoreMenuButton.Content = _useCompactChrome ? "…" : "More…";
        FindPrevButton.Content = _useCompactChrome ? "◀" : "Prev";
        FindNextButton.Content = _useCompactChrome ? "▶" : "Next";

        if (_useCompactChrome)
            SectionJumpCombo.Visibility = Visibility.Collapsed;
        else
            RefreshSections();
    }

    public void UpdateResponsiveLayout(double panelWidth) =>
        ApplyLayout(PlayLayoutContext.FromPanel(PlayPanelSide.Right, panelWidth));

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }
}

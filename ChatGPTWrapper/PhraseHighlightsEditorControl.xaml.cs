using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ChatGPTWrapper.Adventure.Stores;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;

namespace ChatGPTWrapper;

public partial class PhraseHighlightsEditorControl : UserControl
{
    private const int MaxRules = 50;
    private const string DefaultColor = "#FFD166";
    private const double SwatchSize = 28;
    private const double SwatchRadius = 5;
    private const int AutoColorPreviewCount = 18;

    private static readonly string[] PresetColors = PhraseHighlightPresetColors.ManualPicker;

    private readonly ObservableCollection<RuleRow> _rows = [];
    private bool _suppressEditorEvents;
    private bool _suppressRulesNotify;

    public event EventHandler? RulesChanged;

    public event EventHandler? ColorAssignmentChanged;

    public Func<Guid?>? ResolveActiveAdventureId { get; set; }

    private UiChromeSettings? _workingChrome;
    private bool _suppressProfileEvents;

    public PhraseHighlightsEditorControl()
    {
        InitializeComponent();

        BuildSwatches(TextColorSwatchesPanel, "text");
        BuildSwatches(BackgroundColorSwatchesPanel, "background");

        RulesListView.ItemsSource = _rows;
        Loaded += (_, _) => RefreshImportAvailability();
    }

    public void AttachChromeSettings(UiChromeSettings working)
    {
        _workingChrome = working;
        HighlightColorAssignmentService.Normalize(working);
        RefreshAutoColorProfileCombo();
    }

    public void ApplyColorAssignmentTo(UiChromeSettings settings)
    {
        if (_workingChrome is null)
            return;

        settings.ActiveHighlightColorProfileId = _workingChrome.ActiveHighlightColorProfileId;
        settings.HighlightColorCustomOptions = _workingChrome.HighlightColorCustomOptions.Clone();
    }

    private void RefreshAutoColorProfileCombo()
    {
        if (_workingChrome is null || AutoColorProfileCombo is null)
            return;

        _suppressProfileEvents = true;
        try
        {
            var profiles = _workingChrome.HighlightColorProfiles
                .Where(p => p.IsBuiltIn)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Concat(
                [
                    new HighlightColorAssignmentProfile
                    {
                        Id = HighlightColorProfileIds.Custom,
                        Name = "Custom",
                        IsBuiltIn = false,
                    },
                ])
                .ToList();

            AutoColorProfileCombo.ItemsSource = profiles;
            var activeId = HighlightColorAssignmentService.ResolveInitialProfileId(_workingChrome);
            AutoColorProfileCombo.SelectedItem = profiles.FirstOrDefault(p =>
                p.Id.Equals(activeId, StringComparison.OrdinalIgnoreCase));

            UpdateAutoColorProfileDescription();
            RefreshAutoColorPalettePreview();
        }
        finally
        {
            _suppressProfileEvents = false;
        }
    }

    private void AutoColorProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents || _workingChrome is null)
            return;

        if (AutoColorProfileCombo.SelectedItem is not HighlightColorAssignmentProfile profile)
            return;

        _workingChrome.ActiveHighlightColorProfileId = profile.Id;
        UpdateAutoColorProfileDescription();
        RefreshAutoColorPalettePreview();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CustomizeAutoColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workingChrome is null)
            return;

        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        if (HighlightColorAssignmentDialog.Show(owner, _workingChrome, out var profileId, out var options) != true)
            return;

        _workingChrome.ActiveHighlightColorProfileId = profileId;
        if (profileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            _workingChrome.HighlightColorCustomOptions = options.Clone();

        RefreshAutoColorProfileCombo();
        ColorAssignmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateAutoColorProfileDescription()
    {
        if (AutoColorProfileDescriptionText is null || _workingChrome is null)
            return;

        var activeId = HighlightColorAssignmentService.ResolveInitialProfileId(_workingChrome);
        if (activeId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            AutoColorProfileDescriptionText.Text =
                "Custom palette and assignment options — open Customize to edit.";
            return;
        }

        AutoColorProfileDescriptionText.Text =
            HighlightColorProfileLibrary.Find(_workingChrome.HighlightColorProfiles, activeId)?.Description
            ?? string.Empty;
    }

    private void RefreshAutoColorPalettePreview()
    {
        if (AutoColorPalettePreviewPanel is null)
            return;

        AutoColorPalettePreviewPanel.Children.Clear();
        if (_workingChrome is null)
            return;

        var options = HighlightColorAssignmentService.ResolveEffectiveOptions(_workingChrome);
        var theme = ThemeRuntime.Current;
        var canvas = ResolveHighlightCanvasBackground();

        foreach (var color in HighlightColorAssignmentEngine.BuildPalette(options, theme, canvas).Take(AutoColorPreviewCount))
            AutoColorPalettePreviewPanel.Children.Add(CreatePaletteSwatch(color, selectable: false));
    }

    public void RefreshImportAvailability()
    {
        var canImport = ResolveActiveAdventureId?.Invoke() is not null;
        ImportFromAdventureButton.IsEnabled = canImport;
        ImportFromAdventureButton.ToolTip = canImport
            ? "Import player, party, and cast names from the active adventure."
            : "Open an adventure in Play or Design mode to import cast names.";
    }

    public void LoadRules(IEnumerable<PhraseHighlightRule> existingRules)
    {
        _rows.Clear();
        foreach (var rule in existingRules)
            _rows.Add(RuleRow.FromRule(rule));

        if (_rows.Count > 0)
            RulesListView.SelectedIndex = 0;
        else
            ClearEditor();

        UpdateRulesEmptyState();
    }

    public IReadOnlyList<PhraseHighlightRule> GetRules()
    {
        CommitEditorToSelection();
        return _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .Select(r => r.ToRule())
            .Take(MaxRules)
            .ToList();
    }

    private void ImportFromAdventureButton_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this) ?? Application.Current.MainWindow as Window;
        var adventureId = ResolveActiveAdventureId?.Invoke();
        if (adventureId is null)
        {
            MessageBox.Show(owner, "Open an adventure in Play or Design mode to import cast names.", "Import from adventure");
            return;
        }

        var bundle = AdventureStore.Load(adventureId.Value);
        if (bundle is null)
        {
            MessageBox.Show(
                owner,
                "Could not load the active adventure. Try reopening it from the dashboard.",
                "Import from adventure",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (CastPhraseImportDialog.Show(
                owner,
                bundle,
                out var imported,
                _workingChrome is not null
                    ? HighlightColorAssignmentService.ResolveEffectiveOptions(_workingChrome)
                    : null,
                ResolveHighlightCanvasBackground(),
                GetRules()) != true)
            return;

        foreach (var rule in imported)
        {
            if (_rows.Count >= MaxRules)
                break;

            if (_rows.Any(r => string.Equals(r.Phrase, rule.Phrase, StringComparison.OrdinalIgnoreCase)))
                continue;

            _rows.Add(RuleRow.FromRule(rule));
        }

        if (_rows.Count > 0)
            RulesListView.SelectedIndex = 0;
        NotifyRulesChanged();
        UpdateRulesEmptyState();
    }

    public bool TryValidate(out string? errorMessage)
    {
        CommitEditorToSelection();

        var nonEmpty = _rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Phrase))
            .ToList();

        if (_rows.Any(r => !string.IsNullOrWhiteSpace(r.Phrase)) && nonEmpty.Count == 0)
        {
            errorMessage = "Each rule needs a non-empty phrase.";
            ShowValidation(errorMessage);
            return false;
        }

        if (_rows.Any(r => string.IsNullOrWhiteSpace(r.Phrase) && HasOtherFields(r)))
        {
            errorMessage = "Remove empty rules or enter a phrase for each row.";
            ShowValidation(errorMessage);
            return false;
        }

        HideValidation();
        errorMessage = null;
        return true;
    }

    private Brush SwatchBorderBrush =>
        (Brush)FindResource("BorderStrongBrush");

    private void BuildSwatches(WrapPanel panel, string kind)
    {
        panel.Children.Clear();
        foreach (var color in PresetColors)
        {
            var swatch = CreatePaletteSwatch(color, selectable: true);
            swatch.MouseLeftButtonUp += (_, _) => OnSwatchClicked(kind, color);
            panel.Children.Add(swatch);
        }
    }

    private Border CreatePaletteSwatch(string color, bool selectable)
    {
        var swatch = new Border
        {
            Width = SwatchSize,
            Height = SwatchSize,
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(SwatchRadius),
            Background = CreateBrush(color),
            BorderBrush = SwatchBorderBrush,
            BorderThickness = new Thickness(1),
            Tag = color,
            ToolTip = color,
        };

        if (selectable)
            swatch.Cursor = Cursors.Hand;

        return swatch;
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
        catch
        {
            return new SolidColorBrush(Colors.Gray);
        }
    }

    private void OnSwatchClicked(string kind, string color)
    {
        if (RulesListView.SelectedItem is not RuleRow row)
            return;

        if (kind == "text")
        {
            row.Color = color;
            ColorTextBox.Text = color;
        }
        else
        {
            row.BackgroundColor = color;
            BackgroundColorTextBox.Text = color;
        }

        UpdateSwatchSelection();
        UpdatePreview();
        NotifyRulesChanged();
    }

    private void PickTextColorButton_Click(object sender, RoutedEventArgs e) =>
        PickColorForField("text");

    private void PickBackgroundColorButton_Click(object sender, RoutedEventArgs e) =>
        PickColorForField("background");

    private void PickColorForField(string kind)
    {
        if (RulesListView.SelectedItem is not RuleRow)
            return;

        var current = kind == "text"
            ? ColorTextBox.Text.Trim()
            : BackgroundColorTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(current))
            current = DefaultColor;

        var owner = Window.GetWindow(this);
        if (owner is null)
            return;

        if (!ColorPickerWorkflow.TryPickHex(owner, current, out var selected))
            return;

        if (kind == "text")
            ColorTextBox.Text = selected;
        else
            BackgroundColorTextBox.Text = selected;
    }

    private void RulesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var hasSelection = RulesListView.SelectedItem is RuleRow;
        RemoveButton.IsEnabled = hasSelection;
        DuplicateButton.IsEnabled = hasSelection;

        if (RulesListView.SelectedItem is RuleRow row)
            LoadEditor(row);
        else
            ClearEditor();
    }

    private void RulesListView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete)
        {
            RemoveButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private void LoadEditor(RuleRow row)
    {
        _suppressEditorEvents = true;
        try
        {
            PhraseTextBox.Text = row.Phrase;
            ColorTextBox.Text = row.Color;
            BackgroundColorTextBox.Text = row.BackgroundColor ?? "";
            BoldCheckBox.IsChecked = row.Bold;
            ItalicCheckBox.IsChecked = row.Italic;
            UpdateSwatchSelection();
            UpdatePreview();
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void ClearEditor()
    {
        _suppressEditorEvents = true;
        try
        {
            PhraseTextBox.Text = "";
            ColorTextBox.Text = DefaultColor;
            BackgroundColorTextBox.Text = "";
            BoldCheckBox.IsChecked = false;
            ItalicCheckBox.IsChecked = false;
            UpdateSwatchSelection();
            UpdatePreview();
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void RuleField_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEditorEvents)
            return;

        if (RulesListView.SelectedItem is not RuleRow row)
            return;

        row.Phrase = PhraseTextBox.Text.Trim();
        var textColor = NormalizeColor(ColorTextBox.Text, DefaultColor);
        var bg = BackgroundColorTextBox.Text.Trim();
        var backgroundColor = string.IsNullOrWhiteSpace(bg)
            ? null
            : NormalizeColor(bg, bg);
        var effectiveBackground = backgroundColor ?? ResolveHighlightCanvasBackground();
        row.Color = ThemeContrast.EnsureReadable(textColor, effectiveBackground);
        row.BackgroundColor = backgroundColor;
        row.Bold = BoldCheckBox.IsChecked == true;
        row.Italic = ItalicCheckBox.IsChecked == true;

        if (!string.Equals(row.Color, textColor, StringComparison.OrdinalIgnoreCase))
            ColorTextBox.Text = row.Color;

        UpdateSwatchSelection();
        UpdatePreview();
        NotifyRulesChanged();
    }

    private void ClearBackgroundButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListView.SelectedItem is not RuleRow row)
            return;

        row.BackgroundColor = null;
        BackgroundColorTextBox.Text = "";
        UpdateSwatchSelection();
        UpdatePreview();
        NotifyRulesChanged();
    }

    private void UpdateSwatchSelection()
    {
        var textColor = NormalizeColor(ColorTextBox.Text, DefaultColor);
        var bgColor = BackgroundColorTextBox.Text.Trim();
        var bgNormalized = string.IsNullOrWhiteSpace(bgColor)
            ? null
            : NormalizeColor(bgColor, bgColor);

        UpdateSwatchPanelSelection(TextColorSwatchesPanel, textColor);
        UpdateSwatchPanelSelection(BackgroundColorSwatchesPanel, bgNormalized);
    }

    private void UpdateSwatchPanelSelection(Panel panel, string? selectedColor)
    {
        foreach (var child in panel.Children)
        {
            if (child is not Border border || border.Tag is not string swatchColor)
                continue;

            var selected = !string.IsNullOrWhiteSpace(selectedColor)
                && string.Equals(swatchColor, selectedColor, StringComparison.OrdinalIgnoreCase);
            border.BorderBrush = selected
                ? new SolidColorBrush(Colors.White)
                : SwatchBorderBrush;
            border.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void UpdateRulesEmptyState()
    {
        if (RulesEmptyStateBorder is null || RulesListView is null)
            return;

        var isEmpty = _rows.Count == 0;
        RulesEmptyStateBorder.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        RulesListView.Visibility = isEmpty ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdatePreview()
    {
        var phrase = string.IsNullOrWhiteSpace(PhraseTextBox.Text)
            ? "Sample phrase"
            : PhraseTextBox.Text.Trim();
        PreviewTextBlock.Text = phrase;

        var textColor = NormalizeColor(ColorTextBox.Text, DefaultColor);
        var bg = BackgroundColorTextBox.Text.Trim();
        var backgroundColor = string.IsNullOrWhiteSpace(bg)
            ? ResolveHighlightCanvasBackground()
            : NormalizeColor(bg, bg);
        var readableText = ThemeContrast.EnsureReadable(textColor, backgroundColor);

        PreviewTextBlock.Foreground = CreateBrush(readableText);
        PreviewTextBlock.Background = string.IsNullOrWhiteSpace(bg)
            ? Brushes.Transparent
            : CreateBrush(backgroundColor);

        PreviewTextBlock.FontWeight = BoldCheckBox.IsChecked == true
            ? FontWeights.Bold
            : FontWeights.Normal;
        PreviewTextBlock.FontStyle = ItalicCheckBox.IsChecked == true
            ? FontStyles.Italic
            : FontStyles.Normal;
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (_rows.Count >= MaxRules)
        {
            ShowValidation($"At most {MaxRules} rules are allowed.");
            return;
        }

        HideValidation();
        var row = new RuleRow
        {
            Phrase = "example",
            Color = DefaultColor,
        };
        _rows.Add(row);
        RulesListView.SelectedIndex = _rows.Count - 1;
        RulesListView.Focus();
        PhraseTextBox.Focus();
        PhraseTextBox.SelectAll();
        NotifyRulesChanged();
        UpdateRulesEmptyState();
    }

    private void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListView.SelectedItem is not RuleRow source)
            return;

        if (_rows.Count >= MaxRules)
        {
            ShowValidation($"At most {MaxRules} rules are allowed.");
            return;
        }

        CommitEditorToSelection();
        HideValidation();
        var row = new RuleRow
        {
            Phrase = source.Phrase + " copy",
            Color = source.Color,
            BackgroundColor = source.BackgroundColor,
            Bold = source.Bold,
            Italic = source.Italic,
        };
        _rows.Add(row);
        RulesListView.SelectedIndex = _rows.Count - 1;
        NotifyRulesChanged();
        UpdateRulesEmptyState();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListView.SelectedItem is not RuleRow row)
            return;

        HideValidation();
        var index = _rows.IndexOf(row);
        _rows.Remove(row);
        if (_rows.Count == 0)
        {
            ClearEditor();
            NotifyRulesChanged();
            UpdateRulesEmptyState();
            return;
        }

        RulesListView.SelectedIndex = Math.Min(index, _rows.Count - 1);
        NotifyRulesChanged();
        UpdateRulesEmptyState();
    }

    private void CommitEditorToSelection()
    {
        if (RulesListView.SelectedItem is not RuleRow)
            return;

        _suppressRulesNotify = true;
        try
        {
            RuleField_Changed(this, new RoutedEventArgs());
        }
        finally
        {
            _suppressRulesNotify = false;
        }
    }

    private static bool HasOtherFields(RuleRow row) =>
        !string.IsNullOrWhiteSpace(row.Color) && row.Color != DefaultColor
        || !string.IsNullOrWhiteSpace(row.BackgroundColor)
        || row.Bold
        || row.Italic;

    private string ResolveHighlightCanvasBackground()
    {
        if (Application.Current?.Resources["BgBaseBrush"] is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";

        return ThemeRuntime.Current.GetHex("BgBase");
    }

    private static string NormalizeColor(string value, string fallback)
    {
        var trimmed = (value ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
            return fallback;

        if (!trimmed.StartsWith('#'))
            trimmed = "#" + trimmed;

        return HexColorRegex().IsMatch(trimmed) ? trimmed.ToUpperInvariant() : fallback;
    }

    private void ShowValidation(string message)
    {
        ValidationTextBlock.Text = message;
        ValidationTextBlock.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationTextBlock.Visibility = Visibility.Collapsed;
        ValidationTextBlock.Text = "";
    }

    private void NotifyRulesChanged()
    {
        if (_suppressRulesNotify)
            return;

        RulesChanged?.Invoke(this, EventArgs.Empty);
    }

    [GeneratedRegex(@"^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{3})$")]
    private static partial Regex HexColorRegex();

    private sealed class RuleRow : INotifyPropertyChanged
    {
        private string _phrase = "";
        private string _color = DefaultColor;
        private string? _backgroundColor;
        private bool _bold;
        private bool _italic;

        public string Phrase
        {
            get => _phrase;
            set
            {
                if (_phrase == value)
                    return;
                _phrase = value;
                OnPropertyChanged();
            }
        }

        public string Color
        {
            get => _color;
            set
            {
                if (_color == value)
                    return;
                _color = value;
                OnPropertyChanged();
            }
        }

        public string? BackgroundColor
        {
            get => _backgroundColor;
            set
            {
                if (_backgroundColor == value)
                    return;
                _backgroundColor = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
            }
        }

        public bool Bold
        {
            get => _bold;
            set
            {
                if (_bold == value)
                    return;
                _bold = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
            }
        }

        public bool Italic
        {
            get => _italic;
            set
            {
                if (_italic == value)
                    return;
                _italic = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StyleSummary));
            }
        }

        public string StyleSummary
        {
            get
            {
                var parts = new List<string>();
                if (Bold) parts.Add("Bold");
                if (Italic) parts.Add("Italic");
                if (!string.IsNullOrWhiteSpace(BackgroundColor)) parts.Add("Bg");
                return parts.Count > 0 ? string.Join(", ", parts) : "—";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static RuleRow FromRule(PhraseHighlightRule rule) =>
            new()
            {
                Phrase = rule.Phrase,
                Color = NormalizeColor(rule.Color, DefaultColor),
                BackgroundColor = string.IsNullOrWhiteSpace(rule.BackgroundColor)
                    ? null
                    : NormalizeColor(rule.BackgroundColor!, rule.BackgroundColor!),
                Bold = rule.Bold,
                Italic = rule.Italic,
            };

        public PhraseHighlightRule ToRule() =>
            new()
            {
                Phrase = Phrase.Trim(),
                Color = NormalizeColor(Color, DefaultColor),
                BackgroundColor = string.IsNullOrWhiteSpace(BackgroundColor)
                    ? null
                    : NormalizeColor(BackgroundColor, BackgroundColor),
                Bold = Bold,
                Italic = Italic,
            };
    }
}

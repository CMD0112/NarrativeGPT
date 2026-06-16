using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ChatGPTWrapper;

public partial class PhraseHighlightsEditorControl : UserControl
{
    private const int MaxRules = 50;
    private const string DefaultColor = "#FFD166";

    private static readonly string[] PresetColors =
    [
        "#FFD166",
        "#FF6B6B",
        "#4ECDC4",
        "#95E1D3",
        "#F38181",
        "#AA96DA",
        "#FCBAD3",
        "#FFFFD2",
        "#A8E6CF",
        "#DCE775",
        "#ECECEC",
        "#FFB347",
    ];

    private readonly ObservableCollection<RuleRow> _rows = [];
    private bool _suppressEditorEvents;
    private bool _suppressRulesNotify;

    public event EventHandler? RulesChanged;

    public PhraseHighlightsEditorControl()
    {
        InitializeComponent();

        BuildSwatches(TextColorSwatchesPanel, "text");
        BuildSwatches(BackgroundColorSwatchesPanel, "background");

        RulesListView.ItemsSource = _rows;
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
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(0),
                Background = CreateBrush(color),
                BorderBrush = SwatchBorderBrush,
                BorderThickness = new Thickness(1),
                Tag = color,
                ToolTip = color,
            };
            swatch.Click += (_, _) => OnSwatchClicked(kind, color);
            panel.Children.Add(swatch);
        }
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
        row.Color = NormalizeColor(ColorTextBox.Text, DefaultColor);
        var bg = BackgroundColorTextBox.Text.Trim();
        row.BackgroundColor = string.IsNullOrWhiteSpace(bg)
            ? null
            : NormalizeColor(bg, bg);
        row.Bold = BoldCheckBox.IsChecked == true;
        row.Italic = ItalicCheckBox.IsChecked == true;

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
            if (child is not Button button)
                continue;

            var swatchColor = button.Tag as string;
            var selected = !string.IsNullOrWhiteSpace(selectedColor)
                && string.Equals(swatchColor, selectedColor, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = selected
                ? new SolidColorBrush(Colors.White)
                : SwatchBorderBrush;
            button.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        }
    }

    private void UpdatePreview()
    {
        var phrase = string.IsNullOrWhiteSpace(PhraseTextBox.Text)
            ? "Sample phrase"
            : PhraseTextBox.Text.Trim();
        PreviewTextBlock.Text = phrase;
        PreviewTextBlock.Foreground = CreateBrush(NormalizeColor(ColorTextBox.Text, DefaultColor));

        var bg = BackgroundColorTextBox.Text.Trim();
        PreviewTextBlock.Background = string.IsNullOrWhiteSpace(bg)
            ? Brushes.Transparent
            : CreateBrush(NormalizeColor(bg, bg));

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
            return;
        }

        RulesListView.SelectedIndex = Math.Min(index, _rows.Count - 1);
        NotifyRulesChanged();
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

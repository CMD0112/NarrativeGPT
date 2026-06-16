using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace ChatGPTWrapper;

public partial class ContinuousViewFormatDialog : Window
{
    private sealed class SliderBinding
    {
        public required Func<ContinuousViewFormatSettings, double> Getter { get; init; }
        public required Action<ContinuousViewFormatSettings, double> Setter { get; init; }
        public required double Tick { get; init; }
        public required string Unit { get; init; }
        public required TextBlock ValueText { get; init; }
        public required Slider Slider { get; init; }
        public bool EnhancedProseOnly { get; init; }
    }

    private static readonly JsonSerializerOptions FormatJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly UiChromeSettings _original;
    private readonly UiChromeSettings _working;
    private readonly Action<UiChromeSettings, bool, int?>? _applySettings;
    private readonly List<SliderBinding> _sliderBindings = [];
    private readonly DispatcherTimer _previewDebounce;
    private bool _livePreviewActive;
    private bool _slidersBuilt;
    private bool _suppressSliderEvents;
    private int _previewNonce;

    public UiChromeSettings ResultSettings { get; private set; }

    public ContinuousViewFormatDialog(
        UiChromeSettings chrome,
        Action<UiChromeSettings, bool, int?>? applySettings = null)
    {
        InitializeComponent();

        _original = CloneSettings(chrome);
        _working = CloneSettings(chrome);
        ResultSettings = CloneSettings(chrome);
        _applySettings = applySettings;

        _previewDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            PushLivePreview();
        };

        LoadBehaviorFields();
        PhraseEditorControl.LoadRules(_working.PhraseHighlightRules);
        PhraseEditorControl.RulesChanged += (_, _) => OnSettingsChanged();

        ProseEnhancementsCheckBox.Checked += (_, _) => OnSettingsChanged();
        ProseEnhancementsCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        HideEditPromptsCheckBox.Checked += (_, _) => OnSettingsChanged();
        HideEditPromptsCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        HideContextTagsCheckBox.Checked += (_, _) => OnSettingsChanged();
        HideContextTagsCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        ExpandHiddenContextCheckBox.Checked += (_, _) => OnSettingsChanged();
        ExpandHiddenContextCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        PhraseHighlightsCheckBox.Checked += (_, _) => OnSettingsChanged();
        PhraseHighlightsCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        ShowImagesCheckBox.Checked += (_, _) => OnSettingsChanged();
        ShowImagesCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        PreviewInChatCheckBox.Checked += (_, _) => OnSettingsChanged();
        PreviewInChatCheckBox.Unchecked += (_, _) =>
        {
            _livePreviewActive = false;
            _applySettings?.Invoke(_original, false, null);
        };

        PreviewInChatCheckBox.IsChecked = _working.ContinuousViewEnabled;

        Loaded += OnDialogLoaded;

        Closing += (_, _) =>
        {
            if (DialogResult != true && _livePreviewActive)
                _applySettings?.Invoke(_original, false, null);
        };
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        if (_slidersBuilt)
            return;

        if (LayoutPanel is null || TypographyPanel is null || CodeHeadingsPanel is null
            || EnhancedTypographyPanel is null)
            return;

        BuildSliders();
        _slidersBuilt = true;
        RefreshAllSliders();
        UpdateCvDependentUi();
        UpdateCssPreview();
    }

    private static UiChromeSettings CloneSettings(UiChromeSettings source) =>
        new()
        {
            ContinuousViewEnabled = source.ContinuousViewEnabled,
            ProseEnhancementsEnabled = source.ProseEnhancementsEnabled,
            HideAssistantEditArtifacts = source.HideAssistantEditArtifacts,
            HideContextTagsInThread = source.HideContextTagsInThread,
            ExpandHiddenContextInThread = source.ExpandHiddenContextInThread,
            PhraseHighlightsEnabled = source.PhraseHighlightsEnabled,
            PhraseHighlightRules = (source.PhraseHighlightRules ?? []).Select(r => r.Clone()).ToList(),
            ContinuousViewFormat = (source.ContinuousViewFormat ?? ContinuousViewFormatSettings.CreateDefaults()).Clone(),
            ChromePreferencesRevision = source.ChromePreferencesRevision,
        };

    private void BuildSliders()
    {
        if (LayoutPanel is null || TypographyPanel is null || CodeHeadingsPanel is null
            || EnhancedTypographyPanel is null)
            return;

        AddSlider(LayoutPanel, "Content max width", 32, 52, 0.5, "rem",
            s => s.ContentMaxWidthRem, (s, v) => s.ContentMaxWidthRem = v);
        AddSlider(LayoutPanel, "Overlay padding (horizontal)", 0.75, 3, 0.05, "rem",
            s => s.OverlayPaddingXRem, (s, v) => s.OverlayPaddingXRem = v);
        AddSlider(LayoutPanel, "Overlay padding (vertical)", 0.5, 2.5, 0.05, "rem",
            s => s.OverlayPaddingYRem, (s, v) => s.OverlayPaddingYRem = v);
        AddSlider(LayoutPanel, "Segment spacing", 0.4, 2, 0.05, "rem",
            s => s.SegmentSpacingRem, (s, v) => s.SegmentSpacingRem = v);
        AddSlider(LayoutPanel, "Block margin", 0.25, 1.5, 0.05, "rem",
            s => s.BlockMarginRem, (s, v) => s.BlockMarginRem = v);
        AddSlider(LayoutPanel, "Paragraph margin", 0.2, 1.2, 0.05, "rem",
            s => s.ProseParagraphMarginRem, (s, v) => s.ProseParagraphMarginRem = v);

        var dividerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var dividerCheck = new CheckBox
        {
            Content = "Show segment dividers",
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            IsChecked = _working.ContinuousViewFormat.ShowSegmentDividers,
        };
        dividerCheck.Checked += (_, _) =>
        {
            _working.ContinuousViewFormat.ShowSegmentDividers = true;
            OnSettingsChanged();
        };
        dividerCheck.Unchecked += (_, _) =>
        {
            _working.ContinuousViewFormat.ShowSegmentDividers = false;
            OnSettingsChanged();
        };
        dividerPanel.Children.Add(dividerCheck);
        LayoutPanel.Children.Add(dividerPanel);

        AddSlider(TypographyPanel, "User font size", 0.85, 1.2, 0.01, "rem",
            s => s.UserFontSizeRem, (s, v) => s.UserFontSizeRem = v);
        AddSlider(TypographyPanel, "User line height", 1.3, 1.9, 0.01, "",
            s => s.UserLineHeight, (s, v) => s.UserLineHeight = v);
        AddSlider(TypographyPanel, "Assistant font size", 0.9, 1.3, 0.01, "rem",
            s => s.AssistantFontSizeRem, (s, v) => s.AssistantFontSizeRem = v);
        AddSlider(TypographyPanel, "Assistant line height", 1.4, 2, 0.01, "",
            s => s.AssistantLineHeight, (s, v) => s.AssistantLineHeight = v);
        AddSlider(TypographyPanel, "Block letter spacing", 0, 0.025, 0.001, "em",
            s => s.BlockLetterSpacingEm, (s, v) => s.BlockLetterSpacingEm = v);

        AddSlider(EnhancedTypographyPanel, "Enhanced prose line height", 1.45, 1.95, 0.01, "",
            s => s.EnhancedProseLineHeight, (s, v) => s.EnhancedProseLineHeight = v,
            enhancedProseOnly: true);
        AddSlider(EnhancedTypographyPanel, "Enhanced prose letter spacing", 0, 0.025, 0.001, "em",
            s => s.EnhancedProseLetterSpacingEm, (s, v) => s.EnhancedProseLetterSpacingEm = v,
            enhancedProseOnly: true);

        AddSlider(CodeHeadingsPanel, "Code font size", 0.75, 1.1, 0.01, "rem",
            s => s.CodeFontSizeRem, (s, v) => s.CodeFontSizeRem = v);
        AddSlider(CodeHeadingsPanel, "Code line height", 1.3, 1.8, 0.01, "",
            s => s.CodeLineHeight, (s, v) => s.CodeLineHeight = v);
        AddSlider(CodeHeadingsPanel, "Code block padding", 0.4, 1.4, 0.05, "rem",
            s => s.CodeBlockPaddingRem, (s, v) => s.CodeBlockPaddingRem = v);
        AddSlider(CodeHeadingsPanel, "Heading margin", 0.25, 1.5, 0.05, "rem",
            s => s.HeadingMarginRem, (s, v) => s.HeadingMarginRem = v);
        AddSlider(CodeHeadingsPanel, "H1 size", 1.1, 1.8, 0.01, "rem",
            s => s.HeadingH1ScaleRem, (s, v) => s.HeadingH1ScaleRem = v);
        AddSlider(CodeHeadingsPanel, "H2 size", 1, 1.6, 0.01, "rem",
            s => s.HeadingH2ScaleRem, (s, v) => s.HeadingH2ScaleRem = v);
        AddSlider(CodeHeadingsPanel, "H3 size", 0.9, 1.4, 0.01, "rem",
            s => s.HeadingH3ScaleRem, (s, v) => s.HeadingH3ScaleRem = v);
        AddSlider(CodeHeadingsPanel, "H4 size", 0.85, 1.3, 0.01, "rem",
            s => s.HeadingH4ScaleRem, (s, v) => s.HeadingH4ScaleRem = v);
        AddSlider(CodeHeadingsPanel, "H5 size", 0.8, 1.2, 0.01, "rem",
            s => s.HeadingH5ScaleRem, (s, v) => s.HeadingH5ScaleRem = v);
        AddSlider(CodeHeadingsPanel, "H6 size", 0.75, 1.1, 0.01, "rem",
            s => s.HeadingH6ScaleRem, (s, v) => s.HeadingH6ScaleRem = v);

        if (ComposerClearancePanel is not null)
        {
            AddSlider(ComposerClearancePanel, "Min clearance (0 = auto)", 0, 320, 4, "px",
                s => s.ComposerClearanceMinPx, (s, v) => s.ComposerClearanceMinPx = (int)Math.Round(v));
            AddSlider(ComposerClearancePanel, "Max clearance (0 = auto)", 0, 320, 4, "px",
                s => s.ComposerClearanceMaxPx, (s, v) => s.ComposerClearanceMaxPx = (int)Math.Round(v));
        }
    }

    private void AddSlider(
        Panel? panel,
        string label,
        double minimum,
        double maximum,
        double tick,
        string unit,
        Func<ContinuousViewFormatSettings, double> getter,
        Action<ContinuousViewFormatSettings, double> setter,
        bool enhancedProseOnly = false)
    {
        if (panel is null)
            return;

        var header = new TextBlock
        {
            Text = label,
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 2),
        };
        panel.Children.Add(header);

        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            TickFrequency = tick,
            IsSnapToTickEnabled = true,
        };

        var valueText = new TextBlock
        {
            Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 52,
            TextAlignment = TextAlignment.Right,
        };

        Grid.SetColumn(slider, 0);
        Grid.SetColumn(valueText, 1);
        row.Children.Add(slider);
        row.Children.Add(valueText);
        panel.Children.Add(row);

        var binding = new SliderBinding
        {
            Getter = getter,
            Setter = setter,
            Tick = tick,
            Unit = unit,
            Slider = slider,
            ValueText = valueText,
            EnhancedProseOnly = enhancedProseOnly,
        };
        _sliderBindings.Add(binding);

        slider.ValueChanged += (_, _) =>
        {
            if (_suppressSliderEvents)
                return;

            var value = Math.Round(slider.Value / tick) * tick;
            setter(_working.ContinuousViewFormat, value);
            UpdateValueText(binding, value);
            OnSettingsChanged();
        };
    }

    private void LoadBehaviorFields()
    {
        ProseEnhancementsCheckBox.IsChecked = _working.ProseEnhancementsEnabled;
        HideEditPromptsCheckBox.IsChecked = _working.HideAssistantEditArtifacts;
        HideContextTagsCheckBox.IsChecked = _working.HideContextTagsInThread;
        ExpandHiddenContextCheckBox.IsChecked = _working.ExpandHiddenContextInThread;
        ExpandHiddenContextCheckBox.IsEnabled = _working.HideContextTagsInThread;
        PhraseHighlightsCheckBox.IsChecked = _working.PhraseHighlightsEnabled;
        ShowImagesCheckBox.IsChecked = _working.ContinuousViewFormat.ShowImages;
    }

    private void SyncBehaviorFieldsToWorking()
    {
        _working.ProseEnhancementsEnabled = ProseEnhancementsCheckBox.IsChecked == true;
        _working.HideAssistantEditArtifacts = HideEditPromptsCheckBox.IsChecked == true;
        _working.HideContextTagsInThread = HideContextTagsCheckBox.IsChecked == true;
        _working.ExpandHiddenContextInThread = ExpandHiddenContextCheckBox.IsChecked == true;
        ExpandHiddenContextCheckBox.IsEnabled = _working.HideContextTagsInThread;
        _working.PhraseHighlightsEnabled = PhraseHighlightsCheckBox.IsChecked == true;
        _working.ContinuousViewFormat.ShowImages = ShowImagesCheckBox.IsChecked != false;
        _working.PhraseHighlightRules = PhraseEditorControl.GetRules().Select(r => r.Clone()).ToList();
    }

    private void RefreshAllSliders()
    {
        _suppressSliderEvents = true;
        try
        {
            foreach (var binding in _sliderBindings)
            {
                var value = binding.Getter(_working.ContinuousViewFormat);
                binding.Slider.Value = value;
                UpdateValueText(binding, value);
            }
        }
        finally
        {
            _suppressSliderEvents = false;
        }
    }

    private static void UpdateValueText(SliderBinding binding, double value)
    {
        var formatted = binding.Tick >= 0.01
            ? value.ToString("0.##")
            : value.ToString("0.###");
        binding.ValueText.Text = string.IsNullOrEmpty(binding.Unit)
            ? formatted
            : $"{formatted} {binding.Unit}";
    }

    private void OnSettingsChanged()
    {
        SyncBehaviorFieldsToWorking();
        UpdateCvDependentUi();
        UpdateCssPreview();
        QueueLivePreviewIfEnabled();
    }

    private void UpdateCvDependentUi()
    {
        var cvOn = _working.ContinuousViewEnabled;
        var proseOn = _working.ProseEnhancementsEnabled;

        if (CvRequiredBanner is not null)
        {
            CvRequiredBanner.Visibility = cvOn ? Visibility.Collapsed : Visibility.Visible;
        }

        if (EnableContinuousViewButton is not null)
        {
            EnableContinuousViewButton.Visibility = cvOn ? Visibility.Collapsed : Visibility.Visible;
        }

        if (LayoutPanel is not null)
            LayoutPanel.IsEnabled = cvOn;
        if (TypographyPanel is not null)
            TypographyPanel.IsEnabled = cvOn;
        if (CodeHeadingsPanel is not null)
            CodeHeadingsPanel.IsEnabled = cvOn;
        if (EnhancedTypographyPanel is not null)
            EnhancedTypographyPanel.IsEnabled = cvOn && proseOn;
        if (PhraseEditorControl is not null)
            PhraseEditorControl.IsEnabled = cvOn && _working.PhraseHighlightsEnabled;

        foreach (var binding in _sliderBindings.Where(b => b.EnhancedProseOnly))
            binding.Slider.IsEnabled = cvOn && proseOn;
    }

    private void UpdateCssPreview()
    {
        if (CssPreviewTextBox is null)
            return;

        CssPreviewTextBox.Text = FormatCssPreview.BuildCssText(_working.ContinuousViewFormat);
    }

    private void QueueLivePreviewIfEnabled()
    {
        if (PreviewInChatCheckBox.IsChecked != true)
            return;

        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void PushLivePreview()
    {
        if (PreviewInChatCheckBox.IsChecked != true)
            return;

        SyncBehaviorFieldsToWorking();
        _livePreviewActive = true;
        _previewNonce++;
        var revision = ChromePreferencesApplier.NextPreviewRevision(_original, _previewNonce);
        _applySettings?.Invoke(CloneSettings(_working), false, revision);
    }

    private void EnableContinuousViewButton_Click(object sender, RoutedEventArgs e)
    {
        _working.ContinuousViewEnabled = true;
        PreviewInChatCheckBox.IsChecked = true;
        UpdateCvDependentUi();
        OnSettingsChanged();
    }

    private void ApplyPreset(FormatPreset preset)
    {
        _working.ContinuousViewFormat.ApplyPreset(preset);
        RefreshAllSliders();
        OnSettingsChanged();
    }

    private void CompactPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(FormatPreset.Compact);

    private void DefaultPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(FormatPreset.Default);

    private void RelaxedPresetButton_Click(object sender, RoutedEventArgs e) =>
        ApplyPreset(FormatPreset.Relaxed);

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        _working.ContinuousViewFormat.CopyFrom(ContinuousViewFormatSettings.CreateDefaults());
        RefreshAllSliders();
        OnSettingsChanged();
    }

    private void ExportFormatButton_Click(object sender, RoutedEventArgs e)
    {
        SyncBehaviorFieldsToWorking();
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "continuous-view-format.json",
        };
        if (dialog.ShowDialog() != true)
            return;

        var json = JsonSerializer.Serialize(_working.ContinuousViewFormat, FormatJsonOptions);
        File.WriteAllText(dialog.FileName, json);
    }

    private void ImportFormatButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var imported = JsonSerializer.Deserialize<ContinuousViewFormatSettings>(
                File.ReadAllText(dialog.FileName),
                FormatJsonOptions);
            if (imported is null)
                return;

            _working.ContinuousViewFormat.CopyFrom(imported);
            RefreshAllSliders();
            OnSettingsChanged();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "Could not import format JSON: " + ex.Message,
                "Import failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private bool TryCommit(out string? error)
    {
        if (!PhraseEditorControl.TryValidate(out error))
            return false;

        SyncBehaviorFieldsToWorking();
        error = null;
        return true;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCommit(out _))
            return;

        ResultSettings = CloneSettings(_working);
        DialogResult = true;
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCommit(out _))
            return;

        ResultSettings = CloneSettings(_working);
        _applySettings?.Invoke(ResultSettings, true, null);
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_livePreviewActive)
            _applySettings?.Invoke(_original, false, null);

        DialogResult = false;
        Close();
    }
}

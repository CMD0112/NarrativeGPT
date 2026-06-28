using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;
using ChatGPTWrapper.Views;
using Microsoft.Win32;

namespace ChatGPTWrapper;

public partial class ContinuousViewFormatDialog : ShellDialogWindow
{
    private sealed class SliderBinding
    {
        public required string SettingKey { get; init; }
        public required Func<ContinuousViewFormatSettings, double> Getter { get; init; }
        public required Action<ContinuousViewFormatSettings, double> Setter { get; init; }
        public required FormatNumericBounds Bounds { get; init; }
        public required double Tick { get; init; }
        public required string Unit { get; init; }
        public required TextBlock ValueText { get; init; }
        public required Slider Slider { get; init; }
        public required FrameworkElement RootBlock { get; init; }
        public TextBox? ValueInput { get; init; }
        public bool IntegerValue { get; init; }
        public Func<double, string>? ValueLabelFormatter { get; set; }

        public string SearchText => FormatSettingDisplay.GetSearchText(SettingKey);
    }

    private sealed class SearchableSettingRow
    {
        public required string SettingKey { get; init; }
        public required FrameworkElement Root { get; init; }
        public required string SearchText { get; init; }
    }

    private sealed class FontFamilyBinding
    {
        public string? SettingKey { get; init; }

        public required Func<ContinuousViewFormatSettings, string?> Getter { get; init; }

        public required Action<ContinuousViewFormatSettings, string?> Setter { get; init; }

        public required ComboBox PresetCombo { get; init; }

        public required TextBox CustomBox { get; init; }

        public required Button BrowseButton { get; init; }
    }

    private sealed class FontWeightBinding
    {
        public required SliderBinding SliderBinding { get; init; }
        public required ComboBox NamedCombo { get; init; }
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
    private readonly List<SearchableSettingRow> _searchableRows = [];
    private readonly Dictionary<string, SliderBinding> _sliderBindingsByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, FrameworkElement> _colorRowsByKey = new(StringComparer.Ordinal);
    private readonly List<FontFamilyBinding> _fontFamilyBindings = [];
    private readonly List<FontWeightBinding> _fontWeightBindings = [];
    private readonly Dictionary<string, TextBox> _colorBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _colorSwatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ComboBox> _enumComboByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CheckBox> _boolCheckBoxesByKey = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _previewDebounce;
    private bool _livePreviewActive;
    private bool _uiBuilt;
    private bool _suppressSliderEvents;
    private bool _suppressFontFamilyEvents;
    private bool _suppressFontWeightEvents;
    private bool _suppressColorEvents;
    private bool _suppressValueInputEvents;
    private bool _dirty;
    private int _previewNonce;
    private string _selectedProfileId = FormatProfileIds.Default;
    private string _originalSelectedProfileId = FormatProfileIds.Default;
    private bool _suppressProfileEvents;
    private bool _suppressSettingsEvents;
    private StackPanel? _proseGuidesOptionsPanel;
    private StackPanel? _segmentDividerDetailPanel;
    private CheckBox? _showRuledLinesCheckBox;
    private ContinuousViewFormatSettings _formatBaseline = ContinuousViewFormatSettings.CreateDefaults();
    private FormatRefinementCategory _selectedRefinementCategory = FormatRefinementCategory.Layout;
    private bool _suppressRefinementCategoryEvents;

    public UiChromeSettings ResultSettings { get; private set; }

    public ContinuousViewFormatDialog(
        UiChromeSettings chrome,
        Action<UiChromeSettings, bool, int?>? applySettings = null,
        Func<Guid?>? resolveActiveAdventureId = null)
    {
        InitializeComponent();

        PreviewKeyDown += OnDialogPreviewKeyDown;

        var normalized = CloneSettings(chrome);
        FormatDialogChangeService.NormalizeForDialog(normalized);
        _original = CloneSettings(normalized);
        _working = CloneSettings(normalized);
        ResultSettings = CloneSettings(normalized);
        _applySettings = applySettings;

        _originalSelectedProfileId = FormatProfileService.ResolveInitialProfileId(_working.ActiveModeSettings());
        _selectedProfileId = _originalSelectedProfileId;

        PhraseEditorControl.ResolveActiveAdventureId = resolveActiveAdventureId;
        PhraseEditorControl.AttachChromeSettings(_working);

        _previewDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            PushLivePreview();
        };

        PhraseEditorControl.LoadRules(_working.PhraseHighlightRules);
        PhraseEditorControl.RulesChanged += (_, _) => OnPhraseRulesChanged();
        PhraseEditorControl.ColorAssignmentChanged += (_, _) => OnSettingsChanged();

        HideEditPromptsCheckBox.Checked += (_, _) => OnSettingsChanged();
        HideEditPromptsCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        HideContextTagsCheckBox.Checked += (_, _) =>
        {
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        HideContextTagsCheckBox.Unchecked += (_, _) =>
        {
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        ExpandHiddenContextCheckBox.Checked += (_, _) => OnSettingsChanged();
        ExpandHiddenContextCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        PhraseHighlightsCheckBox.Checked += (_, _) =>
        {
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        PhraseHighlightsCheckBox.Unchecked += (_, _) =>
        {
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        ShowImagesCheckBox.Checked += (_, _) => OnSettingsChanged();
        ShowImagesCheckBox.Unchecked += (_, _) => OnSettingsChanged();
        PreviewInChatCheckBox.Checked += (_, _) => OnSettingsChanged();
        PreviewInChatCheckBox.Unchecked += (_, _) =>
        {
            _livePreviewActive = false;
            _applySettings?.Invoke(_original, false, null);
        };

        _suppressSettingsEvents = true;
        try
        {
            LoadBehaviorFields();
            PreviewInChatCheckBox.IsChecked = _working.IsTranscriptOverlayActive;
        }
        finally
        {
            _suppressSettingsEvents = false;
        }

        Loaded += OnDialogLoaded;

        Closing += OnDialogClosing;
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        if (_uiBuilt)
            return;

        if (LayoutPanel is null || StructurePanel is null || SegmentDividersPanel is null
            || ProseGuidesPanel is null || UserMessagesPanel is null || AssistantMessagesPanel is null
            || CodeHeadingsPanel is null || ColorEditorsPanel is null
            || EssentialsLayoutPanel is null || EssentialsUserPanel is null || EssentialsAssistantPanel is null
            || EssentialsColorPanel is null)
            return;

        _suppressSettingsEvents = true;
        try
        {
            BuildSliders();
            BuildWeaveSliders();
            BuildColorEditors();
            PopulateFormatProfiles();
            InitializeRefinementPanel();
            AllowOutsideRecommendedRangeCheckBox.IsChecked = _working.AllowFormatValuesOutsideRecommendedRange;
            _uiBuilt = true;
            RefreshAllSliders();
            UpdateAdvancedNumericUi();
            LoadColorFieldsFromWorking();
            UpdateCvDependentUi();
            UpdateConditionalFormatPanels();
            UpdateProfileUi();
            RefreshPreviewPanels();
            PhraseEditorControl.RefreshImportAvailability();
            if (ShowRoleLabelsCheckBox is not null)
            {
                RegisterSearchableRow(FormatSettingKeys.ShowRoleLabels, ShowRoleLabelsCheckBox);
                AttachResetBesideCheckBox(ShowRoleLabelsCheckBox, FormatSettingKeys.ShowRoleLabels);
            }

            if (ShowImagesCheckBox is not null)
                AttachResetBesideCheckBox(ShowImagesCheckBox, nameof(ContinuousViewFormatSettings.ShowImages));

            AttachWeaveEmbedKindReset();
            _formatBaseline = _working.ContinuousViewFormat.Clone();
        }
        finally
        {
            _suppressSettingsEvents = false;
        }

        RecomputeDirtyState();
    }

    private void OnDialogClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (DialogResult == true)
            return;

        RecomputeDirtyState();
        if (_dirty)
        {
            var result = MessageBox.Show(
                this,
                "Discard unsaved format changes?",
                "Format settings",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        if (_livePreviewActive)
            _applySettings?.Invoke(_original, false, null);
    }

    private static UiChromeSettings CloneSettings(UiChromeSettings source) =>
        new()
        {
            ContinuousViewEnabled = source.ContinuousViewEnabled,
            TranscriptViewMode = source.TranscriptViewMode,
            NativeSettings = source.NativeSettings.Clone(),
            ContinuousSettings = source.ContinuousSettings.Clone(),
            WeaveSettings = source.WeaveSettings.Clone(),
            ActiveHighlightColorProfileId = source.ActiveHighlightColorProfileId,
            HighlightColorProfiles = (source.HighlightColorProfiles ?? []).Select(p => p.Clone()).ToList(),
            HighlightColorCustomOptions = (source.HighlightColorCustomOptions ?? new HighlightColorAssignmentOptions()).Clone(),
            ActiveHighlightColorGroupingProfileId = source.ActiveHighlightColorGroupingProfileId,
            HighlightColorGroupingProfiles = (source.HighlightColorGroupingProfiles ?? []).Select(p => p.Clone()).ToList(),
            HighlightColorGroupingCustomProfile = (source.HighlightColorGroupingCustomProfile ?? new HighlightColorGroupingProfile()).Clone(),
            ChromePreferencesRevision = source.ChromePreferencesRevision,
            ThemeRevision = source.ThemeRevision,
            Theme = source.Theme.Clone(),
        };

    private void BuildWeaveSliders()
    {
        if (WeaveLayoutPanel is null)
            return;

        AddSlider(WeaveLayoutPanel, FormatSettingKeys.WeaveEmbedMarginBlockRem, 0, 3, 0.05, "rem",
            s => s.WeaveEmbedMarginBlockRem, (s, v) => s.WeaveEmbedMarginBlockRem = v,
            absoluteMin: 0, absoluteMax: 8);
        SelectWeaveEmbedKindCombo();
    }

    private void AttachWeaveEmbedKindReset()
    {
        if (WeaveEmbedKindCombo?.Parent is not Panel parent)
            return;

        var index = parent.Children.IndexOf(WeaveEmbedKindCombo);
        if (index < 0)
            return;

        var row = new Grid { Margin = WeaveEmbedKindCombo.Margin };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, row);

        WeaveEmbedKindCombo.Margin = new Thickness(0);
        Grid.SetColumn(WeaveEmbedKindCombo, 0);
        row.Children.Add(WeaveEmbedKindCombo);

        var resetButton = CreateSettingResetButton(FormatSettingKeys.WeaveEmbedKind);
        Grid.SetColumn(resetButton, 1);
        row.Children.Add(resetButton);
    }

    private void SelectWeaveEmbedKindCombo()
    {
        if (WeaveEmbedKindCombo is null)
            return;

        var target = _working.ContinuousViewFormat.WeaveEmbedKind.ToString();
        foreach (ComboBoxItem item in WeaveEmbedKindCombo.Items)
        {
            if (string.Equals(item.Tag as string, target, StringComparison.OrdinalIgnoreCase))
            {
                WeaveEmbedKindCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void WeaveEmbedKindCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents || WeaveEmbedKindCombo?.SelectedItem is not ComboBoxItem item)
            return;

        if (Enum.TryParse<WeaveEmbedKind>(item.Tag as string, true, out var kind))
        {
            _working.ContinuousViewFormat.WeaveEmbedKind = kind;
            OnSettingsChanged();
        }
    }

    private void BuildSliders()
    {
        AddSlider(EssentialsLayoutPanel, FormatSettingKeys.ContentMaxWidthRem, 24, 72, 0.5, "rem",
            s => s.ContentMaxWidthRem, (s, v) => s.ContentMaxWidthRem = v,
            absoluteMin: 4, absoluteMax: 200);
        AddSlider(EssentialsLayoutPanel, FormatSettingKeys.SegmentSpacingRem, 0, 3, 0.05, "rem",
            s => s.SegmentSpacingRem, (s, v) => s.SegmentSpacingRem = v,
            absoluteMin: 0, absoluteMax: 8);

        AddSlider(EssentialsUserPanel, FormatSettingKeys.UserFontSizeRem, 0.7, 1.5, 0.01, "rem",
            s => s.UserFontSizeRem, (s, v) => s.UserFontSizeRem = v,
            absoluteMin: 0.25, absoluteMax: 5);
        AddSlider(EssentialsUserPanel, FormatSettingKeys.UserLineHeight, 1.1, 2.4, 0.01, "",
            s => s.UserLineHeight, (s, v) => s.UserLineHeight = v,
            absoluteMin: 0.5, absoluteMax: 5);
        AddFontWeightControl(EssentialsUserPanel, FormatSettingDisplay.GetLabel(FormatSettingKeys.UserFontWeight),
            s => s.UserFontWeight, (s, v) => s.UserFontWeight = (int)v, FormatSettingKeys.UserFontWeight);
        AddFontFamilyPicker(EssentialsUserPanel, FormatSettingDisplay.GetLabel(FormatSettingKeys.UserFontFamily),
            s => s.UserFontFamily, (s, v) => s.UserFontFamily = v, FormatSettingKeys.UserFontFamily);

        AddSlider(EssentialsAssistantPanel, FormatSettingKeys.AssistantFontSizeRem, 0.75, 1.6, 0.01, "rem",
            s => s.AssistantFontSizeRem, (s, v) => s.AssistantFontSizeRem = v,
            absoluteMin: 0.25, absoluteMax: 5);
        AddSlider(EssentialsAssistantPanel, FormatSettingKeys.AssistantLineHeight, 1.2, 2.6, 0.01, "",
            s => s.AssistantLineHeight, (s, v) => s.AssistantLineHeight = v,
            absoluteMin: 0.5, absoluteMax: 5);
        AddFontWeightControl(EssentialsAssistantPanel, FormatSettingDisplay.GetLabel(FormatSettingKeys.AssistantFontWeight),
            s => s.AssistantFontWeight, (s, v) => s.AssistantFontWeight = (int)v, FormatSettingKeys.AssistantFontWeight);
        AddFontFamilyPicker(EssentialsAssistantPanel, FormatSettingDisplay.GetLabel(FormatSettingKeys.AssistantFontFamily),
            s => s.AssistantFontFamily, (s, v) => s.AssistantFontFamily = v, FormatSettingKeys.AssistantFontFamily);

        AddSlider(StructurePanel, FormatSettingKeys.OverlayPaddingXRem, 0, 4, 0.05, "rem",
            s => s.OverlayPaddingXRem, (s, v) => s.OverlayPaddingXRem = v,
            absoluteMin: 0, absoluteMax: 12);
        AddSlider(StructurePanel, FormatSettingKeys.OverlayPaddingYRem, 0, 4, 0.05, "rem",
            s => s.OverlayPaddingYRem, (s, v) => s.OverlayPaddingYRem = v,
            absoluteMin: 0, absoluteMax: 12);
        AddSlider(StructurePanel, FormatSettingKeys.BlockMarginRem, 0, 2, 0.05, "rem",
            s => s.BlockMarginRem, (s, v) => s.BlockMarginRem = v,
            absoluteMin: 0, absoluteMax: 6);
        AddSlider(StructurePanel, FormatSettingKeys.ProseParagraphMarginRem, 0, 2, 0.05, "rem",
            s => s.ProseParagraphMarginRem, (s, v) => s.ProseParagraphMarginRem = v,
            absoluteMin: 0, absoluteMax: 6);
        AddSlider(StructurePanel, FormatSettingKeys.SegmentBorderRadiusPx, 0, 16, 1, "px",
            s => s.SegmentBorderRadiusPx, (s, v) => s.SegmentBorderRadiusPx = v,
            absoluteMin: 0, absoluteMax: 64);

        BuildSegmentDividerControls(SegmentDividersPanel);
        BuildProseGuideControls(ProseGuidesPanel);

        AddSlider(UserMessagesPanel, FormatSettingKeys.UserLetterSpacingEm, -0.02, 0.06, 0.001, "em",
            s => s.UserLetterSpacingEm, (s, v) => s.UserLetterSpacingEm = v,
            absoluteMin: -0.2, absoluteMax: 0.3);
        AddSlider(UserMessagesPanel, FormatSettingKeys.UserAccentBorderWidthPx, 0, 8, 1, "px",
            s => s.UserAccentBorderWidthPx, (s, v) => s.UserAccentBorderWidthPx = v,
            absoluteMin: 0, absoluteMax: 32);
        AddSlider(UserMessagesPanel, FormatSettingKeys.UserBackgroundOpacity, 0, 100, 1, "%",
            s => s.UserBackgroundOpacity, (s, v) => s.UserBackgroundOpacity = v,
            absoluteMin: 0, absoluteMax: 100, hardClamp: true);
        AddSlider(UserMessagesPanel, FormatSettingKeys.UserIndentRem, 0, 2, 0.05, "rem",
            s => s.UserIndentRem, (s, v) => s.UserIndentRem = v,
            absoluteMin: -5, absoluteMax: 10);

        AddSlider(AssistantMessagesPanel, FormatSettingKeys.AssistantLetterSpacingEm, -0.02, 0.06, 0.001, "em",
            s => s.AssistantLetterSpacingEm, (s, v) => s.AssistantLetterSpacingEm = v,
            absoluteMin: -0.2, absoluteMax: 0.3);
        AddSlider(AssistantMessagesPanel, FormatSettingKeys.AssistantAccentBorderWidthPx, 0, 8, 1, "px",
            s => s.AssistantAccentBorderWidthPx, (s, v) => s.AssistantAccentBorderWidthPx = v,
            absoluteMin: 0, absoluteMax: 32);
        AddSlider(AssistantMessagesPanel, FormatSettingKeys.AssistantBackgroundOpacity, 0, 100, 1, "%",
            s => s.AssistantBackgroundOpacity, (s, v) => s.AssistantBackgroundOpacity = v,
            absoluteMin: 0, absoluteMax: 100, hardClamp: true);
        AddSlider(AssistantMessagesPanel, FormatSettingKeys.AssistantIndentRem, 0, 2, 0.05, "rem",
            s => s.AssistantIndentRem, (s, v) => s.AssistantIndentRem = v,
            absoluteMin: -5, absoluteMax: 10);


        AddSlider(CodeHeadingsPanel, FormatSettingKeys.CodeFontSizeRem, 0.65, 1.25, 0.01, "rem",
            s => s.CodeFontSizeRem, (s, v) => s.CodeFontSizeRem = v,
            absoluteMin: 0.25, absoluteMax: 3);
        AddFontFamilyPicker(CodeHeadingsPanel, FormatSettingDisplay.GetLabel(FormatSettingKeys.CodeFontFamily),
            s => s.CodeFontFamily, (s, v) => s.CodeFontFamily = v, FormatSettingKeys.CodeFontFamily);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.CodeLineHeight, 1.3, 1.8, 0.01, "",
            s => s.CodeLineHeight, (s, v) => s.CodeLineHeight = v,
            absoluteMin: 0.5, absoluteMax: 5);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.CodeBlockPaddingRem, 0.2, 2, 0.05, "rem",
            s => s.CodeBlockPaddingRem, (s, v) => s.CodeBlockPaddingRem = v,
            absoluteMin: 0, absoluteMax: 6);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.CodeBorderRadiusPx, 0, 16, 1, "px",
            s => s.CodeBorderRadiusPx, (s, v) => s.CodeBorderRadiusPx = v,
            absoluteMin: 0, absoluteMax: 64);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingMarginRem, 0, 2, 0.05, "rem",
            s => s.HeadingMarginRem, (s, v) => s.HeadingMarginRem = v,
            absoluteMin: 0, absoluteMax: 6);
        AddFontFamilyPicker(CodeHeadingsPanel, FormatSettingDisplay.GetLabel(FormatSettingKeys.HeadingFontFamily),
            s => s.HeadingFontFamily, (s, v) => s.HeadingFontFamily = v, FormatSettingKeys.HeadingFontFamily);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingH1ScaleRem, 0.88, 2.16, 0.01, "rem",
            s => s.HeadingH1ScaleRem, (s, v) => s.HeadingH1ScaleRem = v,
            absoluteMin: 0.4, absoluteMax: 4);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingH2ScaleRem, 0.8, 1.92, 0.01, "rem",
            s => s.HeadingH2ScaleRem, (s, v) => s.HeadingH2ScaleRem = v,
            absoluteMin: 0.4, absoluteMax: 4);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingH3ScaleRem, 0.72, 1.68, 0.01, "rem",
            s => s.HeadingH3ScaleRem, (s, v) => s.HeadingH3ScaleRem = v,
            absoluteMin: 0.4, absoluteMax: 4);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingH4ScaleRem, 0.68, 1.56, 0.01, "rem",
            s => s.HeadingH4ScaleRem, (s, v) => s.HeadingH4ScaleRem = v,
            absoluteMin: 0.4, absoluteMax: 4);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingH5ScaleRem, 0.64, 1.44, 0.01, "rem",
            s => s.HeadingH5ScaleRem, (s, v) => s.HeadingH5ScaleRem = v,
            absoluteMin: 0.4, absoluteMax: 4);
        AddSlider(CodeHeadingsPanel, FormatSettingKeys.HeadingH6ScaleRem, 0.6, 1.32, 0.01, "rem",
            s => s.HeadingH6ScaleRem, (s, v) => s.HeadingH6ScaleRem = v,
            absoluteMin: 0.4, absoluteMax: 4);

        if (ComposerClearancePanel is not null)
        {
            AddSlider(ComposerClearancePanel, FormatSettingKeys.ComposerClearanceMinPx, 0, 480, 4, "px",
                s => s.ComposerClearanceMinPx, (s, v) => s.ComposerClearanceMinPx = (int)Math.Round(v),
                absoluteMin: 0, absoluteMax: 2000, integerValue: true);
            AddSlider(ComposerClearancePanel, FormatSettingKeys.ComposerClearanceMaxPx, 0, 480, 4, "px",
                s => s.ComposerClearanceMaxPx, (s, v) => s.ComposerClearanceMaxPx = (int)Math.Round(v),
                absoluteMin: 0, absoluteMax: 2000, integerValue: true);
        }
    }

    private void AddSegmentDividersCheckBox(Panel panel)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var dividerCheck = new CheckBox
        {
            Content = FormatSettingDisplay.GetLabel(FormatSettingKeys.ShowSegmentDividers),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            IsChecked = _working.ContinuousViewFormat.ShowSegmentDividers,
            ToolTip = FormatSettingDisplay.GetHelpText(FormatSettingKeys.ShowSegmentDividers),
        };
        dividerCheck.Checked += (_, _) =>
        {
            _working.ContinuousViewFormat.ShowSegmentDividers = true;
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        dividerCheck.Unchecked += (_, _) =>
        {
            _working.ContinuousViewFormat.ShowSegmentDividers = false;
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        block.Children.Add(dividerCheck);
        AttachResetBesideCheckBox(dividerCheck, FormatSettingKeys.ShowSegmentDividers);
        panel.Children.Add(block);
        RegisterSearchableRow(FormatSettingKeys.ShowSegmentDividers, block);
    }

    private void BuildSegmentDividerControls(Panel panel)
    {
        AddSegmentDividersCheckBox(panel);

        _segmentDividerDetailPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        panel.Children.Add(_segmentDividerDetailPanel);

        AddSlider(_segmentDividerDetailPanel, FormatSettingKeys.SegmentDividerOpacity, 0, 100, 1, "%",
            s => s.SegmentDividerOpacity, (s, v) => s.SegmentDividerOpacity = v,
            absoluteMin: 0, absoluteMax: 100, hardClamp: true);
        AddSlider(_segmentDividerDetailPanel, FormatSettingKeys.SegmentDividerWidthPx, 1, 4, 1, "px",
            s => s.SegmentDividerWidthPx, (s, v) => s.SegmentDividerWidthPx = v,
            absoluteMin: 1, absoluteMax: 8, integerValue: true);
        AddEnumCombo(
            _segmentDividerDetailPanel,
            FormatSettingKeys.SegmentDividerStyle,
            [
                ("Solid", SegmentDividerStyle.Solid),
                ("Dashed", SegmentDividerStyle.Dashed),
                ("Dotted", SegmentDividerStyle.Dotted),
            ],
            s => s.SegmentDividerStyle,
            (s, v) => s.SegmentDividerStyle = v);
    }

    private void BuildProseGuideControls(Panel panel)
    {
        AddRuledLinesCheckBox(panel);

        _proseGuidesOptionsPanel = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        panel.Children.Add(_proseGuidesOptionsPanel);

        AddEnumCombo(
            _proseGuidesOptionsPanel,
            FormatSettingKeys.RuledLineStyle,
            [
                ("Ruled lines", RuledLineStyle.Line),
                ("Row bands", RuledLineStyle.Band),
                ("Paragraph zebra", RuledLineStyle.ParagraphZebra),
                ("Dashed underlines", RuledLineStyle.Underline),
                ("Margin ticks", RuledLineStyle.MarginRail),
            ],
            s => s.RuledLineStyle,
            (s, v) => s.RuledLineStyle = v);

        var styleDescription = new TextBlock
        {
            Style = (Style)FindResource("ShellSectionHintStyle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        _proseGuidesOptionsPanel.Children.Add(styleDescription);
        _proseGuideStyleDescription = styleDescription;

        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledLineOpacity, 0, 100, 1, "%",
            s => s.RuledLineOpacity, (s, v) => s.RuledLineOpacity = v,
            absoluteMin: 0, absoluteMax: 100, hardClamp: true);
        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledBandOpacity, 0, 100, 1, "%",
            s => s.RuledBandOpacity, (s, v) => s.RuledBandOpacity = v,
            absoluteMin: 0, absoluteMax: 100, hardClamp: true);
        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledLineThicknessPx, 1, 4, 1, "px",
            s => s.RuledLineThicknessPx, (s, v) => s.RuledLineThicknessPx = v,
            absoluteMin: 1, absoluteMax: 6, integerValue: true);
        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledUnderlineDashEm, 0.15, 1.2, 0.05, "em",
            s => s.RuledUnderlineDashEm, (s, v) => s.RuledUnderlineDashEm = v,
            absoluteMin: 0.1, absoluteMax: 2);
        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledUnderlineGapEm, 0.1, 1.0, 0.05, "em",
            s => s.RuledUnderlineGapEm, (s, v) => s.RuledUnderlineGapEm = v,
            absoluteMin: 0.05, absoluteMax: 1.5);
        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledZebraContrastRatio, 0.1, 1.0, 0.05, "×",
            s => s.RuledZebraContrastRatio, (s, v) => s.RuledZebraContrastRatio = v,
            absoluteMin: 0.1, absoluteMax: 1);
        AddSlider(_proseGuidesOptionsPanel, FormatSettingKeys.RuledMarginTickRatio, 0.2, 0.8, 0.02, "× lh",
            s => s.RuledMarginTickRatio, (s, v) => s.RuledMarginTickRatio = v,
            absoluteMin: 0.1, absoluteMax: 1);
        AddProseGuideColorHint(_proseGuidesOptionsPanel);
        AddBandInvertCheckBox(_proseGuidesOptionsPanel);
        AddProseGuideClipCheckBox(_proseGuidesOptionsPanel);
    }

    private TextBlock? _proseGuideStyleDescription;

    private void AddProseGuideColorHint(Panel panel)
    {
        panel.Children.Add(new TextBlock
        {
            Text = "Guide color is under Colors → Prose guide color.",
            Style = (Style)FindResource("ShellSectionHintStyle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
    }

    private void AddProseGuideClipCheckBox(Panel panel) => AddBoolCheckBox(
        panel,
        FormatSettingKeys.ProseGuideClipToText,
        s => s.ProseGuideClipToText,
        (s, v) => s.ProseGuideClipToText = v);

    private void AddBandInvertCheckBox(Panel panel) => AddBoolCheckBox(
        panel,
        FormatSettingKeys.RuledBandInvertPhase,
        s => s.RuledBandInvertPhase,
        (s, v) => s.RuledBandInvertPhase = v);

    private void AddBoolCheckBox(
        Panel panel,
        string settingKey,
        Func<ContinuousViewFormatSettings, bool> get,
        Action<ContinuousViewFormatSettings, bool> set)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var check = new CheckBox
        {
            Content = FormatSettingDisplay.GetLabel(settingKey),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            IsChecked = get(_working.ContinuousViewFormat),
            ToolTip = FormatSettingDisplay.GetHelpText(settingKey),
        };
        check.Checked += (_, _) =>
        {
            set(_working.ContinuousViewFormat, true);
            OnSettingsChanged();
        };
        check.Unchecked += (_, _) =>
        {
            set(_working.ContinuousViewFormat, false);
            OnSettingsChanged();
        };
        block.Children.Add(check);
        AttachResetBesideCheckBox(check, settingKey);
        panel.Children.Add(block);
        RegisterSearchableRow(settingKey, block);
        _boolCheckBoxesByKey[settingKey] = check;
    }

    private void UpdateConditionalFormatPanels()
    {
        if (!_uiBuilt)
            return;

        var format = _working.ContinuousViewFormat;
        var chrome = _working;
        var applySettingConditionals = !HasActiveSettingsSearch();

        if (applySettingConditionals)
        {
            if (_segmentDividerDetailPanel is not null)
            {
                _segmentDividerDetailPanel.Visibility = FormatConditionalUi.IsDividerDetailVisible(format)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (_proseGuidesOptionsPanel is not null)
            {
                _proseGuidesOptionsPanel.Visibility = FormatConditionalUi.IsGuidesEnabled(format)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            if (_proseGuideStyleDescription is not null)
            {
                _proseGuideStyleDescription.Text = FormatConditionalUi.IsGuidesEnabled(format)
                    ? FormatConditionalUi.ProseGuideStyleDescription(format.RuledLineStyle)
                    : string.Empty;
            }

            foreach (var binding in _sliderBindings)
            {
                if (IsProseGuideConditionalSetting(binding.SettingKey))
                {
                    binding.RootBlock.Visibility = FormatConditionalUi.IsProseGuideSettingVisible(
                            binding.SettingKey,
                            format)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                    continue;
                }

                if (IsRoleAppearanceConditionalSetting(binding.SettingKey))
                {
                    binding.RootBlock.Visibility = FormatConditionalUi.IsRoleAppearanceSliderVisible(
                            binding.SettingKey,
                            format)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }

            foreach (var (settingKey, checkBox) in _boolCheckBoxesByKey)
            {
                if (settingKey is not (FormatSettingKeys.ProseGuideClipToText or FormatSettingKeys.RuledBandInvertPhase))
                    continue;

                if (checkBox.Parent is FrameworkElement row)
                {
                    row.Visibility = FormatConditionalUi.IsProseGuideSettingVisible(settingKey, format)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }

            foreach (var (property, row) in _colorRowsByKey)
            {
                row.Visibility = FormatConditionalUi.IsColorSettingVisible(property, format)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        if (ComposerClearanceSection is not null)
        {
            ComposerClearanceSection.Visibility = FormatConditionalUi.IsComposerClearanceVisible(chrome)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (WeaveSettingsExpander is not null)
        {
            WeaveSettingsExpander.Visibility = FormatConditionalUi.IsWeaveSectionVisible(chrome)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (PhraseEditorControl is not null)
        {
            PhraseEditorControl.Visibility = FormatConditionalUi.IsPhraseHighlightEditorVisible(chrome)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (ExpandHiddenContextCheckBox is not null)
        {
            ExpandHiddenContextCheckBox.Visibility = FormatConditionalUi.IsExpandHiddenContextVisible(chrome)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private static bool IsProseGuideConditionalSetting(string settingKey) =>
        settingKey is FormatSettingKeys.RuledLineOpacity
            or FormatSettingKeys.RuledBandOpacity
            or FormatSettingKeys.RuledLineThicknessPx
            or FormatSettingKeys.RuledUnderlineDashEm
            or FormatSettingKeys.RuledUnderlineGapEm
            or FormatSettingKeys.RuledZebraContrastRatio
            or FormatSettingKeys.RuledMarginTickRatio;

    private static bool IsRoleAppearanceConditionalSetting(string settingKey) =>
        settingKey is FormatSettingKeys.UserIndentRem or FormatSettingKeys.AssistantIndentRem;

    private void AddRuledLinesCheckBox(Panel panel)
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var ruledCheck = new CheckBox
        {
            Content = FormatSettingDisplay.GetLabel(FormatSettingKeys.ShowRuledLines),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            IsChecked = _working.ContinuousViewFormat.ShowRuledLines,
            ToolTip = FormatSettingDisplay.GetHelpText(FormatSettingKeys.ShowRuledLines),
        };
        ruledCheck.Checked += (_, _) =>
        {
            _working.ContinuousViewFormat.ShowRuledLines = true;
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        ruledCheck.Unchecked += (_, _) =>
        {
            _working.ContinuousViewFormat.ShowRuledLines = false;
            OnSettingsChanged();
            UpdateConditionalFormatPanels();
        };
        block.Children.Add(ruledCheck);
        AttachResetBesideCheckBox(ruledCheck, FormatSettingKeys.ShowRuledLines);
        panel.Children.Add(block);
        RegisterSearchableRow(FormatSettingKeys.ShowRuledLines, block);
        _showRuledLinesCheckBox = ruledCheck;
        _boolCheckBoxesByKey[FormatSettingKeys.ShowRuledLines] = ruledCheck;
    }

    private void AddLayoutSectionHeader(Panel panel, string title)
    {
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            Margin = new Thickness(0, 12, 0, 8),
        });
    }

    private void AddEnumCombo<TEnum>(
        Panel panel,
        string settingKey,
        IReadOnlyList<(string Label, TEnum Value)> options,
        Func<ContinuousViewFormatSettings, TEnum> get,
        Action<ContinuousViewFormatSettings, TEnum> set)
        where TEnum : struct, Enum
    {
        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = FormatSettingDisplay.GetLabel(settingKey),
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            ToolTip = FormatSettingDisplay.GetHelpText(settingKey),
        };
        Grid.SetColumn(labelBlock, 0);
        headerRow.Children.Add(labelBlock);

        var resetButton = CreateSettingResetButton(settingKey);
        Grid.SetColumn(resetButton, 1);
        headerRow.Children.Add(resetButton);
        block.Children.Add(headerRow);

        var combo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (label, value) in options)
            AddEnumComboItem(combo, label, value);

        var current = get(_working.ContinuousViewFormat);
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag is TEnum tagged && EqualityComparer<TEnum>.Default.Equals(tagged, current))
            {
                combo.SelectedItem = item;
                break;
            }
        }

        combo.SelectionChanged += (_, _) =>
        {
            if (_suppressSettingsEvents || combo.SelectedItem is not ComboBoxItem selected)
                return;
            if (selected.Tag is TEnum parsed)
            {
                set(_working.ContinuousViewFormat, parsed);
                OnSettingsChanged();
                if (settingKey == FormatSettingKeys.RuledLineStyle)
                    UpdateConditionalFormatPanels();
            }
        };

        block.Children.Add(combo);
        panel.Children.Add(block);
        _enumComboByKey[settingKey] = combo;
        RegisterSearchableRow(settingKey, block);
    }

    private static void AddEnumComboItem<TEnum>(ComboBox combo, string label, TEnum value)
        where TEnum : struct, Enum
    {
        combo.Items.Add(new ComboBoxItem
        {
            Content = label,
            Tag = value,
        });
    }

    private void BuildColorEditors()
    {
        foreach (var group in FormatTokenCatalog.ColorTokens.GroupBy(t => t.Group).OrderBy(g => g.Key))
        {
            ColorEditorsPanel.Children.Add(new TextBlock
            {
                Text = group.Key.ToString(),
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                Margin = new Thickness(0, 16, 0, 10),
            });

            foreach (var token in group.OrderBy(t => t.TokenKey))
            {
                if (token.SettingsProperty is FormatSettingKeys.UserTextColor or FormatSettingKeys.AssistantTextColor)
                    continue;

                AddColorEditorRow(ColorEditorsPanel, token);
            }
        }

        foreach (var property in new[] { FormatSettingKeys.UserTextColor, FormatSettingKeys.AssistantTextColor })
        {
            if (FormatTokenCatalog.BySettingsProperty.TryGetValue(property, out var token))
                AddColorEditorRow(EssentialsColorPanel, token);
        }
    }

    private void AddColorEditorRow(Panel panel, FormatColorTokenDefinition token)
    {
        var settingKey = token.SettingsProperty;
        var row = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 120 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = FormatSettingDisplay.GetLabel(settingKey);
        row.Children.Add(new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = FormatSettingDisplay.GetHelpText(settingKey),
        });

        var swatchButton = new Button
        {
            Width = 36,
            Height = 36,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Pick color",
            Cursor = System.Windows.Input.Cursors.Hand,
            Style = TryFindResource("ShellCommandBarSecondarySlot") as Style,
        };
        var swatchInner = new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(4),
            BorderBrush = (Brush)FindResource("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
        };
        swatchButton.Content = swatchInner;
        swatchButton.Click += (_, _) => PickColor(token.SettingsProperty);
        Grid.SetColumn(swatchButton, 1);
        row.Children.Add(swatchButton);

        var box = new TextBox
        {
            MaxLength = 9,
            FontFamily = new FontFamily("Consolas"),
        };
        box.TextChanged += (_, _) => OnColorEdited(token.SettingsProperty, box, swatchInner);
        Grid.SetColumn(box, 2);
        row.Children.Add(box);

        var inheritButton = new Button
        {
            Content = "Inherit",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Style = TryFindResource("ShellCommandBarSecondarySlot") as Style,
            ToolTip = "Reset to default (inherit page/theme color)",
        };
        inheritButton.Click += (_, _) => ResetFormatSetting(settingKey);
        Grid.SetColumn(inheritButton, 3);
        row.Children.Add(inheritButton);

        _colorBoxes[token.SettingsProperty] = box;
        _colorSwatches[token.SettingsProperty] = swatchInner;
        _colorRowsByKey[settingKey] = row;
        RegisterSearchableRow(settingKey, row, $"{label} {token.TokenKey}");
        panel.Children.Add(row);
    }

    private void AddSlider(
        Panel? panel,
        string settingKey,
        double recommendedMin,
        double recommendedMax,
        double tick,
        string unit,
        Func<ContinuousViewFormatSettings, double> getter,
        Action<ContinuousViewFormatSettings, double> setter,
        double? absoluteMin = null,
        double? absoluteMax = null,
        bool hardClamp = false,
        bool integerValue = false)
    {
        if (panel is null)
            return;

        var display = FormatSettingDisplay.Get(settingKey);
        var bounds = new FormatNumericBounds
        {
            RecommendedMin = recommendedMin,
            RecommendedMax = recommendedMax,
            AbsoluteMin = absoluteMin,
            AbsoluteMax = absoluteMax,
            HardClamp = hardClamp,
        };

        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

        var headerRow = new Grid();
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var header = new TextBlock
        {
            Text = display.DisplayLabel,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 8, 0),
            ToolTip = display.HelpText,
        };
        Grid.SetColumn(header, 0);
        headerRow.Children.Add(header);

        var resetButton = CreateSettingResetButton(settingKey);
        Grid.SetColumn(resetButton, 1);
        headerRow.Children.Add(resetButton);

        var valueText = new TextBlock
        {
            Foreground = (Brush)FindResource("TextMutedBrush"),
            FontFamily = new FontFamily("Consolas"),
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 56,
            TextAlignment = TextAlignment.Right,
        };
        Grid.SetColumn(valueText, 2);
        headerRow.Children.Add(valueText);
        block.Children.Add(headerRow);

        if (!string.IsNullOrWhiteSpace(display.HelpText))
        {
            block.Children.Add(new TextBlock
            {
                Text = display.HelpText,
                Style = (Style)FindResource("ShellSectionHintStyle"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0),
            });
        }

        var slider = new Slider
        {
            Minimum = recommendedMin,
            Maximum = recommendedMax,
            TickFrequency = tick,
            IsSnapToTickEnabled = true,
            Margin = new Thickness(0, 6, 0, 0),
        };
        block.Children.Add(slider);

        var valueInput = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            VerticalContentAlignment = VerticalAlignment.Center,
            Width = 88,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed,
            ToolTip = "Direct numeric entry (advanced mode).",
        };
        block.Children.Add(valueInput);
        panel.Children.Add(block);

        var binding = new SliderBinding
        {
            SettingKey = settingKey,
            Getter = getter,
            Setter = setter,
            Bounds = bounds,
            Tick = tick,
            Unit = unit,
            Slider = slider,
            ValueText = valueText,
            ValueInput = valueInput,
            IntegerValue = integerValue,
            RootBlock = block,
        };
        _sliderBindings.Add(binding);
        _sliderBindingsByKey[settingKey] = binding;
        RegisterSearchableRow(settingKey, block);

        slider.ValueChanged += (_, _) =>
        {
            if (_suppressSliderEvents)
                return;

            var rounded = binding.Tick > 0
                ? Math.Round(slider.Value / binding.Tick) * binding.Tick
                : slider.Value;
            var value = ApplyNumericValue(binding, rounded, fromDirectInput: false);
            setter(_working.ContinuousViewFormat, value);
            UpdateBindingDisplay(binding, value);
            OnSettingsChanged();
        };

        valueInput.LostFocus += (_, _) => CommitValueInput(binding);
        valueInput.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                CommitValueInput(binding);
        };
    }

    private void RegisterSearchableRow(string settingKey, FrameworkElement root, string? extraSearch = null)
    {
        var search = string.IsNullOrWhiteSpace(extraSearch)
            ? FormatSettingDisplay.GetSearchText(settingKey)
            : $"{FormatSettingDisplay.GetSearchText(settingKey)} {extraSearch}";
        _searchableRows.Add(new SearchableSettingRow
        {
            SettingKey = settingKey,
            Root = root,
            SearchText = search,
        });
    }

    private void CommitValueInput(SliderBinding binding)
    {
        if (_suppressValueInputEvents || binding.ValueInput is null)
            return;

        if (!_working.AllowFormatValuesOutsideRecommendedRange)
            return;

        if (!TryParseNumericInput(binding.ValueInput.Text, binding.IntegerValue, out var parsed))
            return;

        var value = ApplyNumericValue(binding, parsed, fromDirectInput: true);
        binding.Setter(_working.ContinuousViewFormat, value);
        UpdateBindingDisplay(binding, value);
        OnSettingsChanged();
    }

    private double ApplyNumericValue(SliderBinding binding, double rawValue, bool fromDirectInput)
    {
        var value = FormatNumericBounds.ClampAbsolute(rawValue, binding.Bounds);

        if (!fromDirectInput && !_working.AllowFormatValuesOutsideRecommendedRange)
        {
            value = Math.Clamp(value, binding.Bounds.RecommendedMin, binding.Bounds.RecommendedMax);
        }

        if (binding.IntegerValue)
            value = Math.Round(value);

        if (binding.Bounds.HardClamp && binding.Tick >= 100)
            value = Math.Round(value / 100d) * 100;

        return value;
    }

    private static bool TryParseNumericInput(string? text, bool integerValue, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!double.TryParse(text.Trim(), out value))
            return false;

        if (integerValue)
            value = Math.Round(value);

        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    private void UpdateBindingDisplay(SliderBinding binding, double value)
    {
        _suppressSliderEvents = true;
        _suppressValueInputEvents = true;
        try
        {
            var sliderValue = Math.Clamp(value, binding.Slider.Minimum, binding.Slider.Maximum);
            binding.Slider.Value = sliderValue;
            UpdateValueText(binding, value);
            if (binding.ValueInput is not null)
                binding.ValueInput.Text = FormatNumericDisplay(binding, value);
            SyncFontWeightCombo(binding, value);
        }
        finally
        {
            _suppressSliderEvents = false;
            _suppressValueInputEvents = false;
        }
    }

    private void SyncFontWeightCombo(SliderBinding binding, double value)
    {
        foreach (var weightBinding in _fontWeightBindings)
        {
            if (!ReferenceEquals(weightBinding.SliderBinding, binding))
                continue;

            _suppressFontWeightEvents = true;
            try
            {
                weightBinding.NamedCombo.SelectedItem = null;
                foreach (ComboBoxItem item in weightBinding.NamedCombo.Items)
                {
                    if (item.Tag is int weight && weight == (int)Math.Round(value))
                    {
                        weightBinding.NamedCombo.SelectedItem = item;
                        break;
                    }
                }
            }
            finally
            {
                _suppressFontWeightEvents = false;
            }
        }
    }

    private static string FormatNumericDisplay(SliderBinding binding, double value)
    {
        if (binding.IntegerValue)
            return ((int)Math.Round(value)).ToString();

        return binding.Tick >= 0.01
            ? value.ToString("0.##")
            : value.ToString("0.###");
    }

    private void AddFontFamilyPicker(
        Panel? panel,
        string label,
        Func<ContinuousViewFormatSettings, string?> getter,
        Action<ContinuousViewFormatSettings, string?> setter,
        string? settingKey = null)
    {
        if (panel is null)
            return;

        var block = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var headerRow = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            ToolTip = settingKey is null ? null : FormatSettingDisplay.GetHelpText(settingKey),
        };
        Grid.SetColumn(labelBlock, 0);
        headerRow.Children.Add(labelBlock);

        if (!string.IsNullOrWhiteSpace(settingKey))
        {
            var resetButton = CreateSettingResetButton(settingKey);
            Grid.SetColumn(resetButton, 1);
            headerRow.Children.Add(resetButton);
        }

        block.Children.Add(headerRow);

        var presetCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var (id, displayLabel) in FormatFontFamilies.PresetOptions)
        {
            var item = new ComboBoxItem
            {
                Content = displayLabel,
                Tag = id,
            };
            var previewFont = FormatFontFamilies.ResolveWpfFontFamily(
                id.Equals(FormatFontFamilies.Custom, StringComparison.OrdinalIgnoreCase)
                    ? null
                    : FormatFontFamilies.NormalizeStored(id, null));
            if (previewFont is not null)
                item.FontFamily = previewFont;
            presetCombo.Items.Add(item);
        }

        var customRow = new Grid { Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var customBox = new TextBox
        {
            FontFamily = new FontFamily("Consolas"),
            ToolTip = "Custom CSS font-family stack (e.g. Palatino, serif)",
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(customBox, 0);
        customRow.Children.Add(customBox);

        var browseButton = new Button
        {
            Content = "Browse…",
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 4, 10, 4),
            Style = TryFindResource("ShellCommandBarSecondarySlot") as Style,
            ToolTip = "Pick an installed system font",
        };
        Grid.SetColumn(browseButton, 1);
        customRow.Children.Add(browseButton);

        block.Children.Add(presetCombo);
        block.Children.Add(customRow);
        panel.Children.Add(block);
        if (!string.IsNullOrWhiteSpace(settingKey))
            RegisterSearchableRow(settingKey, block);

        var binding = new FontFamilyBinding
        {
            SettingKey = settingKey,
            Getter = getter,
            Setter = setter,
            PresetCombo = presetCombo,
            CustomBox = customBox,
            BrowseButton = browseButton,
        };
        _fontFamilyBindings.Add(binding);

        void ApplyPresetSelection()
        {
            if (presetCombo.SelectedItem is not ComboBoxItem item)
                return;

            var presetId = item.Tag as string ?? FormatFontFamilies.Inherit;
            var isCustom = presetId.Equals(FormatFontFamilies.Custom, StringComparison.OrdinalIgnoreCase);
            customRow.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;

            if (isCustom)
            {
                setter(_working.ContinuousViewFormat, FormatFontFamilies.NormalizeStored(presetId, customBox.Text));
            }
            else
            {
                setter(_working.ContinuousViewFormat, FormatFontFamilies.NormalizeStored(presetId, null));
            }
        }

        presetCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressFontFamilyEvents)
                return;

            ApplyPresetSelection();
            OnSettingsChanged();
        };

        customBox.TextChanged += (_, _) =>
        {
            if (_suppressFontFamilyEvents)
                return;

            if (presetCombo.SelectedItem is not ComboBoxItem item
                || !FormatFontFamilies.Custom.Equals(item.Tag as string, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            setter(_working.ContinuousViewFormat, FormatFontFamilies.NormalizeStored(FormatFontFamilies.Custom, customBox.Text));
            OnSettingsChanged();
        };

        browseButton.Click += (_, _) =>
        {
            var picker = new FormatSystemFontPickerWindow { Owner = this };
            if (picker.ShowDialog() != true || string.IsNullOrWhiteSpace(picker.SelectedFontFamilyName))
                return;

            _suppressFontFamilyEvents = true;
            try
            {
                foreach (ComboBoxItem item in presetCombo.Items)
                {
                    if (!FormatFontFamilies.Custom.Equals(item.Tag as string, StringComparison.OrdinalIgnoreCase))
                        continue;

                    presetCombo.SelectedItem = item;
                    break;
                }

                customRow.Visibility = Visibility.Visible;
                var stack = FormatFontFamilies.ToCustomStack(picker.SelectedFontFamilyName);
                customBox.Text = stack;
                setter(_working.ContinuousViewFormat, stack);
            }
            finally
            {
                _suppressFontFamilyEvents = false;
            }

            OnSettingsChanged();
        };
    }

    private void AddFontWeightControl(
        Panel? panel,
        string label,
        Func<ContinuousViewFormatSettings, int> getter,
        Action<ContinuousViewFormatSettings, int> setter,
        string settingKey)
    {
        if (panel is null)
            return;

        AddSlider(panel, settingKey, 300, 700, 100, "",
            s => getter(s), (s, v) => setter(s, (int)v),
            absoluteMin: 100, absoluteMax: 900, hardClamp: true);

        var sliderBinding = _sliderBindings[^1];
        sliderBinding.ValueLabelFormatter = v => FormatFontWeights.FormatLabel((int)v);
        var namedCombo = new ComboBox
        {
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Common font weights",
        };
        namedCombo.Items.Add(new ComboBoxItem { Content = "Named weights…", Tag = -1, IsEnabled = false });
        foreach (var (value, stepLabel) in FormatFontWeights.NamedSteps)
        {
            namedCombo.Items.Add(new ComboBoxItem
            {
                Content = $"{stepLabel} ({value})",
                Tag = value,
            });
        }

        if (panel.Children[^1] is StackPanel sliderBlock)
            sliderBlock.Children.Add(namedCombo);

        var weightBinding = new FontWeightBinding
        {
            SliderBinding = sliderBinding,
            NamedCombo = namedCombo,
        };
        _fontWeightBindings.Add(weightBinding);

        namedCombo.SelectionChanged += (_, _) =>
        {
            if (_suppressFontWeightEvents)
                return;

            if (namedCombo.SelectedItem is not ComboBoxItem item || item.Tag is not int weight || weight < 0)
                return;

            sliderBinding.Slider.Value = weight;
            setter(_working.ContinuousViewFormat, weight);
            UpdateBindingDisplay(sliderBinding, weight);
            OnSettingsChanged();
        };
    }

    private void RefreshFontWeightControls()
    {
        _suppressFontWeightEvents = true;
        try
        {
            foreach (var binding in _fontWeightBindings)
            {
                var value = (int)binding.SliderBinding.Getter(_working.ContinuousViewFormat);
                binding.NamedCombo.SelectedItem = null;
                foreach (ComboBoxItem item in binding.NamedCombo.Items)
                {
                    if (item.Tag is int weight && weight == value)
                    {
                        binding.NamedCombo.SelectedItem = item;
                        break;
                    }
                }
            }
        }
        finally
        {
            _suppressFontWeightEvents = false;
        }
    }

    private void RefreshFontFamilyPickers()
    {
        _suppressFontFamilyEvents = true;
        try
        {
            foreach (var binding in _fontFamilyBindings)
            {
                var stored = binding.Getter(_working.ContinuousViewFormat);
                var presetId = FormatFontFamilies.GetPresetId(stored);
                ComboBoxItem? selected = null;
                foreach (ComboBoxItem item in binding.PresetCombo.Items)
                {
                    if (presetId.Equals(item.Tag as string, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = item;
                        break;
                    }
                }

                binding.PresetCombo.SelectedItem = selected ?? binding.PresetCombo.Items[0];
                var isCustom = presetId.Equals(FormatFontFamilies.Custom, StringComparison.OrdinalIgnoreCase);
                if (binding.CustomBox.Parent is Grid customRow)
                    customRow.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
                else
                    binding.CustomBox.Visibility = isCustom ? Visibility.Visible : Visibility.Collapsed;
                binding.CustomBox.Text = isCustom ? stored ?? string.Empty : string.Empty;
            }
        }
        finally
        {
            _suppressFontFamilyEvents = false;
        }
    }

    private void LoadBehaviorFields()
    {
        HideEditPromptsCheckBox.IsChecked = _working.HideAssistantEditArtifacts;
        HideContextTagsCheckBox.IsChecked = _working.HideContextTagsInThread;
        ExpandHiddenContextCheckBox.IsChecked = _working.ExpandHiddenContextInThread;
        ExpandHiddenContextCheckBox.IsEnabled = _working.HideContextTagsInThread;
        PhraseHighlightsCheckBox.IsChecked = _working.PhraseHighlightsEnabled;
        ShowImagesCheckBox.IsChecked = _working.ContinuousViewFormat.ShowImages;
        ShowRoleLabelsCheckBox.IsChecked = _working.ContinuousViewFormat.ShowRoleLabels;
    }

    private void SyncBehaviorFieldsToWorking()
    {
        _working.HideAssistantEditArtifacts = HideEditPromptsCheckBox.IsChecked == true;
        _working.HideContextTagsInThread = HideContextTagsCheckBox.IsChecked == true;
        _working.ExpandHiddenContextInThread = ExpandHiddenContextCheckBox.IsChecked == true;
        ExpandHiddenContextCheckBox.IsEnabled = _working.HideContextTagsInThread;
        _working.PhraseHighlightsEnabled = PhraseHighlightsCheckBox.IsChecked == true;
        _working.ContinuousViewFormat.ShowImages = ShowImagesCheckBox.IsChecked != false;
        _working.ContinuousViewFormat.ShowRoleLabels = ShowRoleLabelsCheckBox.IsChecked == true;
    }

    private void SyncPhraseRulesFromEditor()
    {
        _working.PhraseHighlightRules = PhraseEditorControl.GetRules().Select(r => r.Clone()).ToList();
        PhraseEditorControl.ApplyColorAssignmentTo(_working);
    }

    private void OnPhraseRulesChanged()
    {
        SyncPhraseRulesFromEditor();
        OnSettingsChanged();
    }

    private Button CreateSettingResetButton(string settingKey)
    {
        var button = new Button
        {
            Content = "↺",
            ToolTip = "Reset to default",
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(4, 0, 0, 0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Style = TryFindResource("ShellCommandBarSecondarySlot") as Style,
        };
        button.Click += (_, _) => ResetFormatSetting(settingKey);
        return button;
    }

    private void AttachResetBesideCheckBox(CheckBox checkBox, string settingKey)
    {
        if (checkBox.Parent is not Panel parent)
            return;

        var index = parent.Children.IndexOf(checkBox);
        if (index < 0)
            return;

        var row = new Grid { Margin = checkBox.Margin };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        parent.Children.RemoveAt(index);
        parent.Children.Insert(index, row);

        checkBox.Margin = new Thickness(0);
        Grid.SetColumn(checkBox, 0);
        row.Children.Add(checkBox);

        var resetButton = CreateSettingResetButton(settingKey);
        Grid.SetColumn(resetButton, 1);
        row.Children.Add(resetButton);

        _boolCheckBoxesByKey[settingKey] = checkBox;
    }

    private void ResetFormatSetting(string settingKey)
    {
        if (!FormatSettingResetService.TryReset(_working.ContinuousViewFormat, settingKey))
            return;

        RefreshFormatSettingUi(settingKey);
        OnSettingsChanged();
    }

    private void RefreshFormatSettingUi(string settingKey)
    {
        if (_sliderBindingsByKey.TryGetValue(settingKey, out var sliderBinding))
        {
            UpdateBindingDisplay(sliderBinding, sliderBinding.Getter(_working.ContinuousViewFormat));
            return;
        }

        if (_enumComboByKey.TryGetValue(settingKey, out var enumCombo))
        {
            RefreshEnumComboSelection(settingKey, enumCombo);
            return;
        }

        if (_boolCheckBoxesByKey.TryGetValue(settingKey, out var checkBox))
        {
            RefreshBoolCheckBox(settingKey, checkBox);
            return;
        }

        if (_colorBoxes.ContainsKey(settingKey))
        {
            LoadColorFieldsFromWorking();
            return;
        }

        if (_fontFamilyBindings.Any(b => settingKey.Equals(b.SettingKey, StringComparison.Ordinal)))
        {
            RefreshFontFamilyPickers();
            return;
        }

        if (settingKey == FormatSettingKeys.WeaveEmbedKind)
            SelectWeaveEmbedKindCombo();
    }

    private void RefreshEnumComboSelection(string settingKey, ComboBox combo)
    {
        var property = typeof(ContinuousViewFormatSettings).GetProperty(settingKey);
        if (property is null)
            return;

        var current = property.GetValue(_working.ContinuousViewFormat);
        _suppressSettingsEvents = true;
        try
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag is null)
                    continue;

                if (Equals(item.Tag, current)
                    || (item.Tag is string tagText
                        && current is Enum enumValue
                        && tagText.Equals(enumValue.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }
        finally
        {
            _suppressSettingsEvents = false;
        }
    }

    private void RefreshBoolCheckBox(string settingKey, CheckBox checkBox)
    {
        var property = typeof(ContinuousViewFormatSettings).GetProperty(settingKey);
        if (property?.PropertyType != typeof(bool))
            return;

        _suppressSettingsEvents = true;
        try
        {
            checkBox.IsChecked = property.GetValue(_working.ContinuousViewFormat) as bool? ?? false;
        }
        finally
        {
            _suppressSettingsEvents = false;
        }
    }

    private void RefreshDerivedFormatControls()
    {
        foreach (var (settingKey, combo) in _enumComboByKey)
            RefreshEnumComboSelection(settingKey, combo);

        foreach (var (settingKey, checkBox) in _boolCheckBoxesByKey)
            RefreshBoolCheckBox(settingKey, checkBox);

        SelectWeaveEmbedKindCombo();
    }

    private void LoadColorFieldsFromWorking()
    {
        _suppressColorEvents = true;
        try
        {
            foreach (var token in FormatTokenCatalog.ColorTokens)
            {
                if (!_colorBoxes.TryGetValue(token.SettingsProperty, out var box))
                    continue;

                var value = GetColorProperty(token.SettingsProperty);
                box.Text = value ?? string.Empty;
                if (_colorSwatches.TryGetValue(token.SettingsProperty, out var swatch))
                    swatch.Background = TryBrush(value);
            }
        }
        finally
        {
            _suppressColorEvents = false;
        }
    }

    private void RefreshAllSliders()
    {
        _suppressSliderEvents = true;
        _suppressValueInputEvents = true;
        try
        {
            foreach (var binding in _sliderBindings)
            {
                binding.Slider.Minimum = binding.Bounds.RecommendedMin;
                binding.Slider.Maximum = binding.Bounds.RecommendedMax;
                var value = binding.Getter(_working.ContinuousViewFormat);
                UpdateBindingDisplay(binding, value);
            }

            RefreshFontFamilyPickers();
            RefreshFontWeightControls();
        }
        finally
        {
            _suppressSliderEvents = false;
            _suppressValueInputEvents = false;
        }
    }

    private void UpdateAdvancedNumericUi()
    {
        var allowOutside = _working.AllowFormatValuesOutsideRecommendedRange;
        foreach (var binding in _sliderBindings)
        {
            if (binding.ValueInput is null)
                continue;

            binding.ValueInput.Visibility = allowOutside ? Visibility.Visible : Visibility.Collapsed;
            binding.ValueText.Visibility = allowOutside ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static void UpdateValueText(SliderBinding binding, double value)
    {
        if (binding.ValueLabelFormatter is not null)
        {
            binding.ValueText.Text = binding.ValueLabelFormatter(value);
            return;
        }

        var formatted = binding.Tick >= 0.01
            ? value.ToString("0.##")
            : value.ToString("0.###");
        binding.ValueText.Text = string.IsNullOrEmpty(binding.Unit)
            ? formatted
            : $"{formatted} {binding.Unit}";
    }

    private void OnSettingsChanged()
    {
        if (_suppressSettingsEvents)
            return;

        SyncBehaviorFieldsToWorking();
        MarkCustomProfileIfFormatDrifted();
        UpdateCvDependentUi();
        UpdateProfileUi();
        UpdateBoundsWarnings();
        RefreshSettingsSearchFilter();
        UpdateConditionalFormatPanels();
        RefreshPreviewPanels();
        QueueLivePreviewIfEnabled();
        RecomputeDirtyState();
    }

    private void OnDialogPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Tab
            || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control)
            != System.Windows.Input.ModifierKeys.Control
            || SettingsTabs is null
            || SettingsTabs.Items.Count == 0)
        {
            return;
        }

        var nextIndex = SettingsTabs.SelectedIndex + 1;
        if (nextIndex >= SettingsTabs.Items.Count)
            nextIndex = 0;

        SettingsTabs.SelectedIndex = nextIndex;
        e.Handled = true;
    }

    private void RecomputeDirtyState()
    {
        SyncBehaviorFieldsToWorking();
        SyncPhraseRulesFromEditor();
        _dirty = FormatDialogChangeService.HasUnsavedChanges(
            _original,
            _working,
            _originalSelectedProfileId,
            _selectedProfileId);
        Title = _dirty
            ? "Format — Continuous view & thread (unsaved changes)"
            : "Format — Continuous view & thread";
    }

    private void MarkCustomProfileIfFormatDrifted()
    {
        var selected = GetSelectedProfile();
        if (selected is not null)
        {
            // Keep the profile selected while editing; dirty state is shown in UpdateProfileUi.
            _selectedProfileId = selected.Id;
            return;
        }

        if (_selectedProfileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return;

        var tracked = FormatProfileLibrary.Find(_working.FormatProfiles, _selectedProfileId);
        if (tracked is not null
            && FormatProfileService.SettingsMatch(_working.ContinuousViewFormat, tracked.Format))
        {
            return;
        }

        var matched = _working.FormatProfiles.FirstOrDefault(p =>
            FormatProfileService.SettingsMatch(p.Format, _working.ContinuousViewFormat));
        if (matched is not null)
        {
            _selectedProfileId = matched.Id;
            if (FormatProfileCombo is not null && !_suppressProfileEvents)
            {
                _suppressProfileEvents = true;
                FormatProfileCombo.SelectedItem = matched;
                _suppressProfileEvents = false;
            }

            return;
        }

        _selectedProfileId = FormatProfileIds.Custom;
    }

    private void UpdateBoundsWarnings() => UpdateReadabilityWarnings();

    private void UpdateReadabilityWarnings()
    {
        var warnings = FormatReadabilityAnalyzer.Analyze(
            _working.ContinuousViewFormat,
            _working.PhraseHighlightRules,
            _working.PhraseHighlightsEnabled);

        if (FormatBoundsWarningText is not null)
        {
            if (warnings.Count == 0)
            {
                FormatBoundsWarningText.Visibility = Visibility.Collapsed;
                FormatBoundsWarningText.Text = string.Empty;
            }
            else
            {
                FormatBoundsWarningText.Visibility = Visibility.Visible;
                FormatBoundsWarningText.Text = string.Join(" ", warnings.Select(w => w.Message));
            }
        }

        RefreshRefinementPanel();
    }

    private void FocusSetting(string settingKey)
    {
        var tabIndex = settingKey is FormatSettingKeys.UserTextColor or FormatSettingKeys.AssistantTextColor
            && FormatSettingDisplay.GetTier(settingKey) == FormatSettingTier.Essential
            ? 0
            : settingKey is FormatSettingKeys.RuledLineColor
                ? 4
            : _sliderBindingsByKey.ContainsKey(settingKey) && FormatSettingDisplay.GetTier(settingKey) == FormatSettingTier.Essential
                ? 0
                : settingKey is FormatSettingKeys.UserLetterSpacingEm
                    or FormatSettingKeys.AssistantLetterSpacingEm
                    or FormatSettingKeys.UserAccentBorderWidthPx
                    or FormatSettingKeys.AssistantAccentBorderWidthPx
                    or FormatSettingKeys.UserBackgroundOpacity
                    or FormatSettingKeys.AssistantBackgroundOpacity
                    or FormatSettingKeys.UserIndentRem
                    or FormatSettingKeys.AssistantIndentRem
                    ? 2
                : settingKey.StartsWith("Heading", StringComparison.Ordinal)
                    || settingKey.StartsWith("Code", StringComparison.Ordinal)
                    ? 3
                : settingKey is FormatSettingKeys.ShowSegmentDividers
                    or FormatSettingKeys.ShowRuledLines
                    or FormatSettingKeys.RuledLineStyle
                    or FormatSettingKeys.ProseGuideClipToText
                    or FormatSettingKeys.RuledBandInvertPhase
                    or FormatSettingKeys.OverlayPaddingXRem
                    or FormatSettingKeys.BlockMarginRem
                    ? 1
                : _colorRowsByKey.ContainsKey(settingKey)
                    ? 4
                    : 1;

        if (SettingsTabs is not null && tabIndex >= 0 && tabIndex < SettingsTabs.Items.Count)
            SettingsTabs.SelectedIndex = tabIndex;

        if (_sliderBindingsByKey.TryGetValue(settingKey, out var sliderBinding))
        {
            sliderBinding.RootBlock.BringIntoView();
            sliderBinding.Slider.Focus();
            return;
        }

        if (_colorRowsByKey.TryGetValue(settingKey, out var colorRow))
        {
            colorRow.BringIntoView();
            if (_colorBoxes.TryGetValue(settingKey, out var box))
                box.Focus();
        }
    }

    private void FormatSettingsSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSettingsSearchFilter();
        UpdateConditionalFormatPanels();
    }

    private void RefreshSettingsSearchFilter()
    {
        if (!_uiBuilt)
            return;

        var query = FormatSettingsSearchBox?.Text?.Trim() ?? string.Empty;
        var hasQuery = query.Length > 0;

        foreach (var row in _searchableRows)
        {
            var visible = !hasQuery
                || row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            row.Root.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var binding in _sliderBindings)
        {
            var visible = !hasQuery
                || binding.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
            binding.RootBlock.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private sealed record RefinementCategoryItem(FormatRefinementCategory Category, string Label);

    private void InitializeRefinementPanel()
    {
        if (RefinementCategoryList is null)
            return;

        _suppressRefinementCategoryEvents = true;
        try
        {
            RefinementCategoryList.ItemsSource = FormatRefinementCatalog.Categories
                .Select(c => new RefinementCategoryItem(c, RefinementCategoryLabel(c)))
                .ToList();
            RefinementCategoryList.DisplayMemberPath = nameof(RefinementCategoryItem.Label);
            RefinementCategoryList.SelectedIndex = 0;
            _selectedRefinementCategory = FormatRefinementCategory.Layout;
        }
        finally
        {
            _suppressRefinementCategoryEvents = false;
        }

        RefreshRefinementPanel();
    }

    private void RefinementCategoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRefinementCategoryEvents || RefinementCategoryList?.SelectedItem is not RefinementCategoryItem item)
            return;

        _selectedRefinementCategory = item.Category;
        RefreshRefinementPanel();
    }

    private void RefreshRefinementPanel()
    {
        if (!_uiBuilt
            || RefinementSuggestedPanel is null
            || RefinementCommonPanel is null
            || RefinementSuggestedHeader is null)
        {
            return;
        }

        var context = BuildRefinementContext();
        var format = _working.ContinuousViewFormat;
        var suggested = FormatRefinementSuggester.GetSuggestedForCategory(format, context, _selectedRefinementCategory);
        var common = FormatRefinementCatalog.GetCommonActions(_selectedRefinementCategory);

        RefinementSuggestedPanel.Children.Clear();
        if (suggested.Count == 0)
        {
            RefinementSuggestedHeader.Visibility = Visibility.Collapsed;
        }
        else
        {
            RefinementSuggestedHeader.Visibility = Visibility.Visible;
            RefinementSuggestedHeader.Text = suggested.Count == 1
                ? "Suggested for you"
                : $"Suggested for you ({suggested.Count})";

            foreach (var suggestion in suggested)
            {
                RefinementSuggestedPanel.Children.Add(
                    CreateRefinementButton(suggestion.Action, suggestion.Detail, suggested: true));
            }
        }

        RefinementCommonPanel.Children.Clear();
        foreach (var action in common)
        {
            RefinementCommonPanel.Children.Add(CreateRefinementButton(action, action.Description, suggested: false));
        }
    }

    private FormatRefinementContext BuildRefinementContext() =>
        new()
        {
            TranscriptViewMode = _working.TranscriptViewMode,
            PhraseHighlightsEnabled = _working.PhraseHighlightsEnabled,
            PhraseHighlightRules = _working.PhraseHighlightRules,
        };

    private Button CreateRefinementButton(FormatRefinementAction action, string? detail, bool suggested)
    {
        var settingLabel = FormatSettingDisplay.GetLabel(action.PrimarySettingKey);
        var toolTip = string.IsNullOrWhiteSpace(detail)
            ? $"{action.Description}\n\nRight-click: go to {settingLabel}"
            : $"{detail}\n\nRight-click: go to {settingLabel}";

        var styleKey = suggested ? "ShellCommandBarPrimarySlot" : "ShellCommandBarSecondarySlot";
        var button = new Button
        {
            Content = action.Label,
            Tag = action.Id,
            ToolTip = toolTip,
            Style = TryFindResource(styleKey) as Style,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 0, 8, 8),
        };

        var actionId = action.Id;
        var settingKey = action.PrimarySettingKey;
        button.Click += (_, _) => ApplyRefinementAction(actionId);
        button.PreviewMouseRightButtonUp += (_, e) =>
        {
            FocusSetting(settingKey);
            e.Handled = true;
        };

        return button;
    }

    private void ApplyRefinementAction(string actionId)
    {
        if (!FormatRefinementCatalog.TryApply(actionId, _working.ContinuousViewFormat))
            return;

        _suppressSettingsEvents = true;
        try
        {
            RefreshAllSliders();
            LoadColorFieldsFromWorking();
            if (ShowRoleLabelsCheckBox is not null)
                ShowRoleLabelsCheckBox.IsChecked = _working.ContinuousViewFormat.ShowRoleLabels;
            UpdateCvDependentUi();
            UpdateProfileUi();
            UpdateBoundsWarnings();
            RefreshPreviewPanels();
        }
        finally
        {
            _suppressSettingsEvents = false;
        }

        MarkCustomProfileIfFormatDrifted();
        QueueLivePreviewIfEnabled();
        RecomputeDirtyState();
    }

    private static string RefinementCategoryLabel(FormatRefinementCategory category) =>
        category switch
        {
            FormatRefinementCategory.Layout => "Layout",
            FormatRefinementCategory.Typography => "Typography",
            FormatRefinementCategory.Colors => "Colors",
            FormatRefinementCategory.RoleDistinction => "Role",
            FormatRefinementCategory.CodeHeadings => "Code",
            FormatRefinementCategory.Weave => "Weave",
            _ => category.ToString(),
        };

    private void AllowOutsideRecommendedRangeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiBuilt)
            return;

        _working.AllowFormatValuesOutsideRecommendedRange =
            AllowOutsideRecommendedRangeCheckBox.IsChecked == true;
        UpdateAdvancedNumericUi();
        RefreshAllSliders();
        OnSettingsChanged();
    }

    private void UpdateCvDependentUi()
    {
        var overlayOn = _working.IsTranscriptOverlayActive;
        var weaveOn = _working.TranscriptViewMode == TranscriptViewMode.Weave;

        if (CvRequiredBannerPanel is not null)
            CvRequiredBannerPanel.Visibility = overlayOn ? Visibility.Collapsed : Visibility.Visible;

        if (StructurePanel is not null)
            StructurePanel.IsEnabled = overlayOn;
        if (SegmentDividersPanel is not null)
            SegmentDividersPanel.IsEnabled = overlayOn;
        if (ProseGuidesPanel is not null)
            ProseGuidesPanel.IsEnabled = overlayOn;
        if (LayoutPanel is not null)
            LayoutPanel.IsEnabled = overlayOn;
        if (EssentialsLayoutPanel is not null)
            EssentialsLayoutPanel.IsEnabled = overlayOn;
        if (EssentialsUserPanel is not null)
            EssentialsUserPanel.IsEnabled = overlayOn;
        if (EssentialsAssistantPanel is not null)
            EssentialsAssistantPanel.IsEnabled = overlayOn;
        if (EssentialsColorPanel is not null)
            EssentialsColorPanel.IsEnabled = overlayOn;
        if (UserMessagesPanel is not null)
            UserMessagesPanel.IsEnabled = overlayOn;
        if (AssistantMessagesPanel is not null)
            AssistantMessagesPanel.IsEnabled = overlayOn;
        if (CodeHeadingsPanel is not null)
            CodeHeadingsPanel.IsEnabled = overlayOn;
        if (ColorEditorsPanel is not null)
            ColorEditorsPanel.IsEnabled = overlayOn;
        if (PhraseEditorControl is not null)
            PhraseEditorControl.IsEnabled = overlayOn && _working.PhraseHighlightsEnabled;
        if (WeaveSettingsExpander is not null)
            WeaveSettingsExpander.IsEnabled = overlayOn;
        if (WeaveLayoutPanel is not null)
            WeaveLayoutPanel.IsEnabled = overlayOn && weaveOn;
        if (WeaveEmbedKindCombo is not null)
            WeaveEmbedKindCombo.IsEnabled = overlayOn && weaveOn;
    }

    private bool HasActiveSettingsSearch() =>
        (FormatSettingsSearchBox?.Text?.Trim().Length ?? 0) > 0;

    private void RefreshPreviewPanels()
    {
        var weaveOn = _working.TranscriptViewMode == TranscriptViewMode.Weave;
        if (FormatPreview is not null)
            FormatPreview.Visibility = weaveOn ? Visibility.Collapsed : Visibility.Visible;
        if (WeaveFormatPreview is not null)
        {
            WeaveFormatPreview.Visibility = weaveOn ? Visibility.Visible : Visibility.Collapsed;
            if (weaveOn)
                WeaveFormatPreview.ApplySettings(_working.ContinuousViewFormat);
        }

        if (!weaveOn && FormatPreview is not null)
        {
            FormatPreview.ApplySettings(_working.ContinuousViewFormat);
            FormatPreview.ApplyPhraseHighlights(
                _working.PhraseHighlightRules,
                _working.PhraseHighlightsEnabled,
                _working.ContinuousViewFormat.AssistantFontWeight);
        }

        if (CssPreviewTextBox is not null)
        {
            var css = FormatCssPreview.BuildCssText(_working.ContinuousViewFormat);
            if (weaveOn)
                css += FormatCssBuilder.BuildWeaveCssText(_working.ContinuousViewFormat);
            CssPreviewTextBox.Text = css;
        }
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
        SyncPhraseRulesFromEditor();
        _livePreviewActive = true;
        _previewNonce++;
        var revision = ChromePreferencesApplier.NextPreviewRevision(_original, _previewNonce);
        _applySettings?.Invoke(CloneSettings(_working), false, revision);
    }

    private void OnColorEdited(string propertyName, TextBox box, Border swatch)
    {
        if (_suppressColorEvents)
            return;

        var hex = box.Text.Trim();
        swatch.Background = TryBrush(hex);
        if (hex.Length is 7 or 6 or 9)
        {
            SetColorProperty(propertyName, hex.StartsWith('#') ? hex : "#" + hex);
            OnSettingsChanged();
        }
        else if (string.IsNullOrWhiteSpace(hex))
        {
            SetColorProperty(propertyName, null);
            OnSettingsChanged();
        }
    }

    private void PickColor(string propertyName)
    {
        if (!_colorBoxes.TryGetValue(propertyName, out var box))
            return;

        var current = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(current))
            current = "#5B9FD4";

        var background = ColorPickerContextResolver.ResolveFormatColorBackground(
            propertyName,
            _working.ContinuousViewFormat);
        var context = ColorPickerContextFactory.ForFormatColor(
            propertyName,
            _working.ContinuousViewFormat,
            background);

        if (!ColorPickerWorkflow.TryPickHex(this, current, background, context, out var selected))
            return;

        box.Text = selected;
    }

    private string? GetColorProperty(string propertyName)
    {
        var prop = typeof(ContinuousViewFormatSettings).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        return prop?.GetValue(_working.ContinuousViewFormat) as string;
    }

    private void SetColorProperty(string propertyName, string? value)
    {
        var prop = typeof(ContinuousViewFormatSettings).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public);
        prop?.SetValue(_working.ContinuousViewFormat, value);
    }

    private static Brush TryBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return Brushes.Transparent;

        try
        {
            var normalized = hex.Trim().StartsWith('#') ? hex.Trim() : "#" + hex.Trim();
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalized)!);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }

    private void ShowRoleLabelsCheckBox_Changed(object sender, RoutedEventArgs e) =>
        OnSettingsChanged();

    private void EnableContinuousViewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_working.TranscriptViewMode == TranscriptViewMode.Native)
            _working.TranscriptViewMode = TranscriptViewMode.Continuous;
        PreviewInChatCheckBox.IsChecked = true;
        UpdateCvDependentUi();
        OnSettingsChanged();
    }

    private void ApplyProfileSnapshot(FormatProfile profile)
    {
        _working.ContinuousViewFormat.CopyFrom(profile.Format);
        _selectedProfileId = profile.Id;
        _suppressSettingsEvents = true;
        try
        {
            RefreshAllSliders();
            LoadColorFieldsFromWorking();
            ShowRoleLabelsCheckBox.IsChecked = _working.ContinuousViewFormat.ShowRoleLabels;
            RefreshDerivedFormatControls();
            UpdateCvDependentUi();
            UpdateProfileUi();
            UpdateBoundsWarnings();
            RefreshPreviewPanels();
        }
        finally
        {
            _suppressSettingsEvents = false;
        }

        _formatBaseline = _working.ContinuousViewFormat.Clone();
        RecomputeDirtyState();
    }

    private void CaptureFormatBaseline() =>
        _formatBaseline = _working.ContinuousViewFormat.Clone();

    private void PopulateFormatProfiles()
    {
        if (FormatProfileCombo is null)
            return;

        _suppressProfileEvents = true;
        try
        {
            FormatProfileCombo.ItemsSource = _working.FormatProfiles
                .OrderBy(p => p.IsBuiltIn ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (_selectedProfileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            {
                FormatProfileCombo.SelectedItem = null;
            }
            else
            {
                var selected = FormatProfileLibrary.Find(_working.FormatProfiles, _selectedProfileId);
                FormatProfileCombo.SelectedItem = selected;
            }

            RefreshFormatProfileComboDisplay();
        }
        finally
        {
            _suppressProfileEvents = false;
        }
    }

    private void RefreshFormatProfileComboDisplay()
    {
        if (FormatProfileCombo is null)
            return;

        // DisplayMemberPath caches item text; force rebind after rename or reorder.
        var displayPath = FormatProfileCombo.DisplayMemberPath;
        FormatProfileCombo.DisplayMemberPath = null;
        FormatProfileCombo.DisplayMemberPath = displayPath;
    }

    private FormatProfile? GetSelectedProfile() =>
        FormatProfileCombo?.SelectedItem as FormatProfile;

    private bool IsProfileDirty()
    {
        var selected = GetSelectedProfile();
        if (selected is null)
            return false;

        return !FormatProfileService.SettingsMatch(_working.ContinuousViewFormat, selected.Format);
    }

    private bool HasUnsavedProfileFormatChanges()
    {
        SyncBehaviorFieldsToWorking();
        return !FormatProfileService.SettingsMatch(_working.ContinuousViewFormat, _formatBaseline);
    }

    private void UpdateProfileUi()
    {
        if (FormatProfileStatusText is null)
            return;

        bool duplicateEnabled;
        bool renameEnabled;
        bool deleteEnabled;
        bool saveEnabled;

        if (_selectedProfileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            FormatProfileStatusText.Text = "Custom — differs from saved profiles";
            duplicateEnabled = true;
            renameEnabled = false;
            deleteEnabled = false;
            saveEnabled = false;
            SetProfileActionMenus(duplicateEnabled, renameEnabled, deleteEnabled, saveEnabled);
            return;
        }

        var selected = GetSelectedProfile();
        var dirty = IsProfileDirty();

        if (selected is null)
        {
            FormatProfileStatusText.Text = "Unsaved changes";
        }
        else if (dirty)
        {
            FormatProfileStatusText.Text = $"Modified from “{selected.Name}”";
        }
        else if (!string.IsNullOrWhiteSpace(selected.Description))
        {
            FormatProfileStatusText.Text = selected.Description;
        }
        else
        {
            FormatProfileStatusText.Text = "";
        }

        var isBuiltIn = selected?.IsBuiltIn == true;
        duplicateEnabled = true;
        renameEnabled = selected is not null && !isBuiltIn;
        deleteEnabled = selected is not null && !isBuiltIn;
        saveEnabled = selected is not null && dirty && !isBuiltIn;
        SetProfileActionMenus(duplicateEnabled, renameEnabled, deleteEnabled, saveEnabled);
    }

    private void SetProfileActionMenus(bool duplicate, bool rename, bool delete, bool save)
    {
        if (DuplicateFormatProfileMenuItem is not null)
            DuplicateFormatProfileMenuItem.IsEnabled = duplicate;
        if (RenameFormatProfileMenuItem is not null)
            RenameFormatProfileMenuItem.IsEnabled = rename;
        if (DeleteFormatProfileMenuItem is not null)
            DeleteFormatProfileMenuItem.IsEnabled = delete;
        if (SaveFormatProfileMenuItem is not null)
            SaveFormatProfileMenuItem.IsEnabled = save;
    }

    private bool ConfirmDiscardProfileChanges(FormatProfile? targetProfile = null)
    {
        if (!HasUnsavedProfileFormatChanges())
            return true;

        if (targetProfile is not null
            && FormatProfileService.SettingsMatch(_working.ContinuousViewFormat, targetProfile.Format))
        {
            return true;
        }

        return MessageBox.Show(
            this,
            "Discard unsaved changes to the current format profile?",
            "Format profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
    }

    private void FormatProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressProfileEvents || !_uiBuilt)
            return;

        if (FormatProfileCombo.SelectedItem is not FormatProfile profile)
            return;

        if (profile.Id.Equals(_selectedProfileId, StringComparison.OrdinalIgnoreCase))
            return;

        if (!ConfirmDiscardProfileChanges(profile))
        {
            _suppressProfileEvents = true;
            if (_selectedProfileId.Equals(FormatProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
                FormatProfileCombo.SelectedItem = null;
            else
                FormatProfileCombo.SelectedItem = FormatProfileLibrary.Find(_working.FormatProfiles, _selectedProfileId);
            _suppressProfileEvents = false;
            return;
        }

        ApplyProfileSnapshot(profile);
    }

    private void NewFormatProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TextPromptDialog.TryPrompt(
                this,
                "New format profile",
                "Profile name",
                "My reading layout",
                out var name,
                confirmButtonText: "Create"))
        {
            return;
        }

        SyncBehaviorFieldsToWorking();
        var profile = FormatProfileLibrary.CreateCustom(name, _working.ContinuousViewFormat);
        _working.FormatProfiles.Add(profile);
        _selectedProfileId = profile.Id;
        PopulateFormatProfiles();
        UpdateProfileUi();
        OnSettingsChanged();
    }

    private void DuplicateFormatProfileButton_Click(object sender, RoutedEventArgs e)
    {
        SyncBehaviorFieldsToWorking();

        var source = GetSelectedProfile();
        var baseName = source?.Name ?? "Custom";
        var format = source is not null && !IsProfileDirty()
            ? source.Format
            : _working.ContinuousViewFormat;

        var profile = FormatProfileLibrary.CreateCustom($"{baseName} copy", format);
        _working.FormatProfiles.Add(profile);
        _selectedProfileId = profile.Id;
        PopulateFormatProfiles();
        UpdateProfileUi();
        OnSettingsChanged();
    }

    private void RenameFormatProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedProfile();
        if (profile is null || profile.IsBuiltIn)
            return;

        if (!TextPromptDialog.TryPrompt(this, "Rename profile", "Profile name", profile.Name, out var name, confirmButtonText: "Rename"))
            return;

        if (!FormatProfileService.TryRenameProfile(profile, name, out var error))
        {
            MessageBox.Show(this, error, "Rename profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PopulateFormatProfiles();
        UpdateProfileUi();
    }

    private void DeleteFormatProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedProfile();
        if (profile is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete profile “{profile.Name}”?",
                "Delete profile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!FormatProfileService.TryDeleteProfile(_working.FormatProfiles, profile.Id, out var error))
        {
            MessageBox.Show(this, error, "Delete profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _selectedProfileId = FormatProfileIds.Default;
        var fallback = FormatProfileLibrary.Find(_working.FormatProfiles, _selectedProfileId);
        if (fallback is not null)
            ApplyProfileSnapshot(fallback);

        PopulateFormatProfiles();
        UpdateProfileUi();
    }

    private void SaveFormatProfileButton_Click(object sender, RoutedEventArgs e)
    {
        var profile = GetSelectedProfile();
        if (profile is null || profile.IsBuiltIn)
            return;

        SyncBehaviorFieldsToWorking();
        FormatProfileService.SaveWorkingToProfile(profile, _working.ContinuousViewFormat);
        CaptureFormatBaseline();
        UpdateProfileUi();
        MessageBox.Show(
            this,
            $"Saved changes to “{profile.Name}”.",
            "Format profile",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ResetDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaultProfile = FormatProfileLibrary.Find(_working.FormatProfiles, FormatProfileIds.Default);
        if (defaultProfile is not null)
        {
            ApplyProfileSnapshot(defaultProfile);
            return;
        }

        _working.ContinuousViewFormat.CopyFrom(ContinuousViewFormatSettings.CreateDefaults());
        RefreshAllSliders();
        LoadColorFieldsFromWorking();
        RefreshDerivedFormatControls();
        OnSettingsChanged();
    }

    private void ResetLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        _working.ContinuousViewFormat.ResetLayout();
        RefreshAllSliders();
        RefreshDerivedFormatControls();
        OnSettingsChanged();
    }

    private void ResetColorsButton_Click(object sender, RoutedEventArgs e)
    {
        _working.ContinuousViewFormat.ResetColors();
        LoadColorFieldsFromWorking();
        OnSettingsChanged();
    }

    private void ResetRoleDistinctionButton_Click(object sender, RoutedEventArgs e)
    {
        _working.ContinuousViewFormat.ResetRoleDistinction();
        RefreshAllSliders();
        LoadColorFieldsFromWorking();
        RefreshDerivedFormatControls();
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

            if (imported.UserLetterSpacingEm == 0 && imported.AssistantLetterSpacingEm == 0
                && imported.BlockLetterSpacingEm > 0)
            {
                imported.UserLetterSpacingEm = imported.BlockLetterSpacingEm;
                imported.AssistantLetterSpacingEm = imported.BlockLetterSpacingEm;
            }

            var choice = MessageBox.Show(
                this,
                "Import as a new profile?\n\nYes — create a new profile from this JSON.\nNo — replace the current working copy only.\nCancel — abort import.",
                "Import format JSON",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (choice == MessageBoxResult.Cancel)
                return;

            if (choice == MessageBoxResult.Yes)
            {
                if (!TextPromptDialog.TryPrompt(
                        this,
                        "Import profile",
                        "Profile name",
                        Path.GetFileNameWithoutExtension(dialog.FileName),
                        out var name,
                        confirmButtonText: "Import"))
                {
                    return;
                }

                var profile = FormatProfileLibrary.CreateCustom(name, imported);
                _working.FormatProfiles.Add(profile);
                _selectedProfileId = profile.Id;
                PopulateFormatProfiles();
            }

            _working.ContinuousViewFormat.CopyFrom(imported);
            RefreshAllSliders();
            LoadColorFieldsFromWorking();
            ShowRoleLabelsCheckBox.IsChecked = _working.ContinuousViewFormat.ShowRoleLabels;
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

    private void FinalizeResultSettings()
    {
        _selectedProfileId = FormatProfileService.ResolveActiveProfileId(
            _working.ActiveModeSettings(),
            _working.ContinuousViewFormat,
            _selectedProfileId);
        _working.ActiveFormatProfileId = _selectedProfileId;
    }

    private bool TryCommit(out string? error)
    {
        if (!PhraseEditorControl.TryValidate(out error))
            return false;

        SyncBehaviorFieldsToWorking();
        SyncPhraseRulesFromEditor();
        FinalizeResultSettings();
        error = null;
        return true;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCommit(out _))
            return;

        ResultSettings = CloneSettings(_working);
        _dirty = false;
        DialogResult = true;
        Close();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryCommit(out _))
            return;

        ResultSettings = CloneSettings(_working);
        _applySettings?.Invoke(ResultSettings, true, null);
        _dirty = false;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

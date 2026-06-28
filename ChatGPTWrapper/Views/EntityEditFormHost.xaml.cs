using System.Windows;

using System.Windows.Controls;

using System.Windows.Media;

using ChatGPTWrapper.Adventure.Models;

using ChatGPTWrapper.Adventure.Services;

using ChatGPTWrapper.Adventure.Services.Canon;

using ChatGPTWrapper.Format;

using ChatGPTWrapper.Theme;

using Microsoft.Win32;



namespace ChatGPTWrapper.Views;



public partial class EntityEditFormHost : UserControl

{

    public static readonly DependencyProperty ShowInlineActionsProperty =

        DependencyProperty.Register(

            nameof(ShowInlineActions),

            typeof(bool),

            typeof(EntityEditFormHost),

            new PropertyMetadata(false, OnShowInlineActionsChanged));

    public static readonly DependencyProperty ShowPinToggleProperty =

        DependencyProperty.Register(

            nameof(ShowPinToggle),

            typeof(bool),

            typeof(EntityEditFormHost),

            new PropertyMetadata(true));



    private static readonly string[] HighlightPresetColors = PhraseHighlightPresetColors.ManualPicker;

    private const string DefaultHighlightColor = "#FFD166";



    private EntityEditModel? _model;

    private EntityReferenceEditCallbacks? _highlightCallbacks;

    private EntityEditSnapshot? _entitySnapshot;

    private bool _hasLinkedHighlightRule;

    private bool _baselineEnabled;

    private string _baselineColor = DefaultHighlightColor;

    private bool _baselineHadRule;

    private bool _suppressHighlightEvents;



    public event EventHandler? SaveRequested;



    public event EventHandler? CancelRequested;



    public event EventHandler? DeleteRequested;



    public EntityEditFormHost()

    {

        InitializeComponent();

        BuildHighlightPresetSwatches();

    }



    public static readonly DependencyProperty ShowGroupedSectionsProperty =
        DependencyProperty.Register(
            nameof(ShowGroupedSections),
            typeof(bool),
            typeof(EntityEditFormHost),
            new PropertyMetadata(false, (_, _) => { }));

    public bool ShowGroupedSections
    {
        get => (bool)GetValue(ShowGroupedSectionsProperty);
        set => SetValue(ShowGroupedSectionsProperty, value);
    }

    private EntityExtendedFieldsEditor? _extendedFieldsEditor;
    private Action<string>? _insertIntoComposer;

    public void SetComposerInsert(Action<string>? insertIntoComposer) =>
        _insertIntoComposer = insertIntoComposer;

    public bool ShowInlineActions

    {

        get => (bool)GetValue(ShowInlineActionsProperty);

        set => SetValue(ShowInlineActionsProperty, value);

    }

    public bool ShowPinToggle

    {

        get => (bool)GetValue(ShowPinToggleProperty);

        set => SetValue(ShowPinToggleProperty, value);

    }



    private static void OnShowInlineActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)

    {

        if (d is EntityEditFormHost host)

        {

            host.InlineActionsPanel.Visibility = (bool)e.NewValue ? Visibility.Visible : Visibility.Collapsed;

            host.DescriptionBox.VerticalScrollBarVisibility = (bool)e.NewValue

                ? ScrollBarVisibility.Auto

                : ScrollBarVisibility.Disabled;

        }

    }



    public void LoadModel(EntityEditModel model, EntityReferenceEditCallbacks? callbacks = null)

    {

        _model = model;

        _highlightCallbacks = callbacks;

        _entitySnapshot = EntityEditSnapshot.Capture(model);

        NameBox.Text = model.Name;

        SecondaryLabelBlock.Text = model.SecondaryLabel;

        RoleBox.Text = model.SecondaryValue;

        DescriptionBox.Text = model.Description;

        PinnedCheck.IsChecked = model.Pinned;

        PinnedCheck.Visibility = ShowPinToggle && model.CanPin ? Visibility.Visible : Visibility.Collapsed;

        InlineDeleteButton.Visibility = model.IsNew ? Visibility.Collapsed : Visibility.Visible;



        if (model.ShowQuestStatus)

        {

            QuestStatusPanel.Visibility = Visibility.Visible;

            QuestStatusBox.ItemsSource = Enum.GetValues<QuestStatus>();

            QuestStatusBox.SelectedItem = model.QuestStatus;

        }

        else

        {

            QuestStatusPanel.Visibility = Visibility.Collapsed;

        }



        TagsPanel.Visibility = model.ShowTags ? Visibility.Visible : Visibility.Collapsed;

        TagsBox.Text = model.TagsText;

        AliasesPanel.Visibility = model.ShowAliases ? Visibility.Visible : Visibility.Collapsed;

        AliasesBox.Text = model.AliasesText;



        BuildExtraFields();

        RefreshPortrait();

        RefreshHighlightFromChrome();

    }



    public void RefreshHighlightFromChrome() => LoadHighlightState();



    public bool HasEntityChanges()

    {

        if (_model is null || _entitySnapshot is null)

            return false;



        TryHarvestModel(out _);

        return !_entitySnapshot.Matches(_model);

    }



    public bool HasHighlightChanges()

    {

        if (_model is null || !PhraseHighlightRuleService.SupportsHighlightLinkage(_model.Category))

            return false;



        var enabled = HighlightEnabledCheck.IsChecked == true;

        var color = NormalizeColor(HighlightColorBox.Text);

        return enabled != _baselineEnabled

               || !string.Equals(color, _baselineColor, StringComparison.OrdinalIgnoreCase)

               || (!_baselineHadRule && enabled);

    }



    public bool TryHarvestModel(out string? validationMessage)

    {

        validationMessage = null;

        if (_model is null)

            return false;



        if (string.IsNullOrWhiteSpace(NameBox.Text))

        {

            validationMessage = "Name is required.";

            NameBox.Focus();

            return false;

        }



        _model.Name = NameBox.Text.Trim();

        _model.SecondaryValue = RoleBox.Text.Trim();

        _model.Description = DescriptionBox.Text.Trim();

        _model.Pinned = PinnedCheck.IsChecked == true;

        _model.TagsText = TagsBox.Text.Trim();

        _model.AliasesText = AliasesBox.Text.Trim();

        if (_model.ShowQuestStatus && QuestStatusBox.SelectedItem is QuestStatus status)
            _model.QuestStatus = status;

        if (_extendedFieldsEditor is not null && !_extendedFieldsEditor.TryHarvest(out validationMessage))
            return false;

        return true;

    }



    public bool TryCommitHighlightRulesIfChanged()

    {

        if (!HasHighlightChanges())

            return false;



        if (_model is null

            || _highlightCallbacks?.GetPhraseHighlightRules?.Invoke() is not { } existing

            || _highlightCallbacks.CommitPhraseHighlightRules is null

            || !PhraseHighlightRuleService.SupportsHighlightLinkage(_model.Category))

        {

            return false;

        }



        var enabled = HighlightEnabledCheck.IsChecked == true;

        var canvas = ThemeRuntime.Current.GetHex("BgBase");

        var color = ThemeContrast.EnsureReadable(NormalizeColor(HighlightColorBox.Text), canvas);

        var rules = existing.Select(r => r.Clone()).ToList();

        if (enabled)

        {

            PhraseHighlightRuleService.UpsertLinkedRule(

                rules,

                _model.Name.Trim(),

                _model.Category,

                _model.Id,

                color,

                enabled: true,

                canvas);

            PhraseHighlightRuleService.SyncEntityAliasHighlightRules(
                rules,
                _model.Category,
                _model.Id,
                _model.Name.Trim(),
                EntityEditMapper.ParseTags(_model.AliasesText));

        }

        else

        {

            PhraseHighlightRuleService.DisableLinkedRules(
                rules,
                _model.Category,
                _model.Id,
                _model.Name.Trim(),
                EntityEditMapper.ParseTags(_model.AliasesText));

        }



        _highlightCallbacks.CommitPhraseHighlightRules(rules);

        _highlightCallbacks.OnPhraseHighlightRulesChanged?.Invoke();

        CaptureHighlightBaseline();

        UpdateHighlightPreview();

        return true;

    }



    /// <summary>
    /// Propagates the entity's linked highlight to newly added aliases without changing primary highlight settings.
    /// </summary>
    public bool TrySyncEntityAliasHighlights()
    {
        if (_model is null
            || _highlightCallbacks?.GetPhraseHighlightRules?.Invoke() is not { } existing
            || _highlightCallbacks.CommitPhraseHighlightRules is null
            || !PhraseHighlightRuleService.SupportsHighlightLinkage(_model.Category))
        {
            return false;
        }

        var aliases = EntityEditMapper.ParseTags(_model.AliasesText).ToList();
        var rules = existing.Select(r => r.Clone()).ToList();
        var report = PhraseHighlightRuleService.SyncEntityAliasHighlightRules(
            rules,
            _model.Category,
            _model.Id,
            _model.Name.Trim(),
            aliases);

        if (report.AddedPhrases.Count == 0 && report.UpdatedPhrases.Count == 0)
            return false;

        _highlightCallbacks.CommitPhraseHighlightRules(rules);
        _highlightCallbacks.OnPhraseHighlightRulesChanged?.Invoke();
        CaptureHighlightBaseline();
        UpdateHighlightPreview();
        return true;
    }



    public EntityEditModel? Model => _model;



    private void LoadHighlightState()

    {

        if (_model is null || !PhraseHighlightRuleService.SupportsHighlightLinkage(_model.Category))

        {

            HighlightPanel.Visibility = Visibility.Collapsed;

            _hasLinkedHighlightRule = false;

            return;

        }



        HighlightPanel.Visibility = Visibility.Visible;

        var linked = _highlightCallbacks?.GetPhraseHighlightRules?.Invoke() is { } rules

            ? PhraseHighlightRuleService.ResolveForEntity(
                rules,
                _model.Category,
                _model.Id,
                _model.Name,
                EntityEditMapper.ParseTags(_model.AliasesText))
            : null;

        _hasLinkedHighlightRule = linked is not null;



        _suppressHighlightEvents = true;

        try

        {

            HighlightEnabledCheck.IsChecked = linked?.Enabled ?? false;

            HighlightColorBox.Text = linked?.Color ?? DefaultHighlightColor;

        }

        finally

        {

            _suppressHighlightEvents = false;

        }



        CaptureHighlightBaseline();

        UpdateHighlightColorPanelEnabled();

        UpdateHighlightPreview();

        UpdateHighlightPhraseHint(linked);

    }



    private void CaptureHighlightBaseline()

    {

        _baselineHadRule = _hasLinkedHighlightRule;

        _baselineEnabled = HighlightEnabledCheck.IsChecked == true;

        _baselineColor = NormalizeColor(HighlightColorBox.Text);

        _hasLinkedHighlightRule = _highlightCallbacks?.GetPhraseHighlightRules?.Invoke() is { } rules

                                    && _model is not null

                                    && PhraseHighlightRuleService.ResolveForEntity(
                                        rules,
                                        _model.Category,
                                        _model.Id,
                                        _model.Name,
                                        EntityEditMapper.ParseTags(_model.AliasesText)) is not null;

    }



    private void UpdateHighlightPhraseHint(PhraseHighlightRule? linked)

    {

        if (_model is null)

            return;



        var phrase = linked?.Phrase?.Trim();

        if (string.IsNullOrWhiteSpace(phrase))

            phrase = _model.Name.Trim();



        HighlightPhraseHintText.Text =

            $"Matches “{phrase}” in continuous view · also editable in Format → Highlights.";

    }



    private void UpdateHighlightColorPanelEnabled()

    {

        var enabled = HighlightEnabledCheck.IsChecked == true;

        HighlightColorPanel.IsEnabled = enabled;

        HighlightColorPanel.Opacity = enabled ? 1.0 : 0.55;

    }



    private void UpdateHighlightPreview()

    {

        if (_model is null)

            return;



        var samplePhrase = string.IsNullOrWhiteSpace(_model.Name) ? "Name" : _model.Name.Trim();

        var sample = $"{samplePhrase} opens the door.";

        HighlightPreviewText.Text = sample;



        var enabled = HighlightEnabledCheck.IsChecked == true;

        if (!enabled)

        {

            HighlightPreviewText.Foreground = (Brush)FindResource("TextPrimaryBrush");

            HighlightPreviewText.FontWeight = FontWeights.Normal;

            HighlightPreviewText.Opacity = 0.55;

            HighlightContrastHintText.Visibility = Visibility.Collapsed;

            return;

        }



        HighlightPreviewText.Opacity = 1.0;

        var canvas = ThemeRuntime.Current.GetHex("BgBase");

        var color = ThemeContrast.EnsureReadable(NormalizeColor(HighlightColorBox.Text), canvas);

        HighlightPreviewText.Foreground = TryBrush(color);

        HighlightPreviewText.FontWeight = FontWeights.SemiBold;



        var raw = NormalizeColor(HighlightColorBox.Text);

        HighlightContrastHintText.Visibility = ThemeContrast.IsReadable(raw, canvas)

            ? Visibility.Collapsed

            : Visibility.Visible;

        HighlightContrastHintText.Text = ThemeContrast.IsReadable(raw, canvas)

            ? ""

            : "Low contrast on transcript background — color will be adjusted in continuous view.";

    }



    private void HighlightField_Changed(object sender, RoutedEventArgs e)

    {

        if (_suppressHighlightEvents)

            return;



        UpdateHighlightColorPanelEnabled();

        UpdateHighlightPreview();

    }



    private void HighlightColorBox_TextChanged(object sender, TextChangedEventArgs e)

    {

        if (_suppressHighlightEvents)

            return;



        RefreshHighlightSwatch(HighlightColorBox.Text);

        UpdateHighlightPreview();

    }



    private void BuildHighlightPresetSwatches()

    {

        HighlightPresetSwatchesPanel.Children.Clear();

        foreach (var color in HighlightPresetColors)

        {

            var swatch = new Border

            {

                Width = 18,

                Height = 18,

                Margin = new Thickness(0, 0, 4, 4),

                CornerRadius = new CornerRadius(3),

                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),

                BorderThickness = new Thickness(1),

                Background = TryBrush(color),

                Cursor = System.Windows.Input.Cursors.Hand,

                ToolTip = color,

            };

            swatch.MouseLeftButtonUp += (_, _) =>

            {

                HighlightColorBox.Text = color;

                RefreshHighlightSwatch(color);

                UpdateHighlightPreview();

            };

            HighlightPresetSwatchesPanel.Children.Add(swatch);

        }

    }



    private void HighlightPickColor_Click(object sender, RoutedEventArgs e)

    {

        var owner = Window.GetWindow(this);

        var current = NormalizeColor(HighlightColorBox.Text);

        var canvas = ThemeRuntime.Current.GetHex("BgBase");
        var context = ColorPickerContextFactory.ForGeneric(canvas);
        if (!ColorPickerWorkflow.TryPickHex(owner, current, canvas, context, out var selected))
            return;

        var readable = ThemeContrast.EnsureReadable(selected, canvas);

        HighlightColorBox.Text = readable;

        RefreshHighlightSwatch(readable);

        UpdateHighlightPreview();

    }



    private void RefreshHighlightSwatch(string color) =>

        HighlightColorSwatch.Background = TryBrush(color);



    private static string NormalizeColor(string? value)

    {

        var trimmed = value?.Trim() ?? "";

        if (string.IsNullOrWhiteSpace(trimmed))

            return DefaultHighlightColor;

        return trimmed.StartsWith('#') ? trimmed : "#" + trimmed.TrimStart('#');

    }



    private static Brush TryBrush(string? hex)

    {

        try

        {

            if (string.IsNullOrWhiteSpace(hex))

                return Brushes.Transparent;

            var normalized = hex.Trim();

            if (!normalized.StartsWith('#'))

                normalized = "#" + normalized;

            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(normalized)!);

        }

        catch

        {

            return Brushes.Transparent;

        }

    }



    private void BuildExtraFields()
    {
        ExtraFieldsPanel.Children.Clear();
        _extendedFieldsEditor = null;
        if (_model is null)
            return;

        if (ShowGroupedSections)
        {
            BuildGroupedExtraFields();
            return;
        }

        foreach (var field in _model.Fields.OrderBy(f => f.Order))
            AddFieldEditor(ExtraFieldsPanel, field, _insertIntoComposer);
    }

    private void BuildGroupedExtraFields()
    {
        var groupOrder = new[]
        {
            CanonFieldGroup.Identity,
            CanonFieldGroup.Story,
            CanonFieldGroup.Capabilities,
            CanonFieldGroup.Relations,
            CanonFieldGroup.Custom,
        };

        foreach (var groupId in groupOrder)
        {
            if (groupId == CanonFieldGroup.Custom)
                continue;

            var fields = _model!.Fields
                .Where(f => string.Equals(f.GroupId, groupId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => f.DisplayOrder)
                .ThenBy(f => f.Order)
                .ToList();
            if (fields.Count == 0)
                continue;

            var card = CreateSectionCard(CanonFieldGroup.DisplayLabel(groupId));
            var panel = (StackPanel)card.Child;
            foreach (var field in fields)
                AddFieldEditor(panel, field, _insertIntoComposer);
            ExtraFieldsPanel.Children.Add(card);
        }

        var customCard = CreateSectionCard(CanonFieldGroup.DisplayLabel(CanonFieldGroup.Custom));
        var editor = new EntityExtendedFieldsEditor();
        editor.LoadFields(_model!.ExtendedFields);
        _extendedFieldsEditor = editor;
        ((StackPanel)customCard.Child).Children.Add(editor);
        ExtraFieldsPanel.Children.Add(customCard);
    }

    private static Border CreateSectionCard(string title)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8),
        });
        return new Border
        {
            Style = Application.Current.TryFindResource("ShellCardStyle") as Style,
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack,
        };
    }

    private static void AddFieldEditor(Panel panel, EntityEditField field, Action<string>? insertIntoComposer = null)
    {
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = field.Label,
            VerticalAlignment = VerticalAlignment.Center,
        });

        if (insertIntoComposer is not null && !string.IsNullOrWhiteSpace(field.Value))
        {
            var insert = new Button
            {
                Content = "Insert",
                Padding = new Thickness(8, 2, 8, 2),
                Margin = new Thickness(8, 0, 0, 0),
                Tag = field,
            };
            insert.Click += (_, _) =>
            {
                if (insert.Tag is EntityEditField f && !string.IsNullOrWhiteSpace(f.Value))
                    insertIntoComposer($"{f.Label}: {f.Value.Trim()}");
            };
            Grid.SetColumn(insert, 1);
            header.Children.Add(insert);
        }

        panel.Children.Add(header);

        var multiline = field.Multiline;
        var box = new TextBox
        {
            Text = field.Value,
            Tag = field,
            Margin = new Thickness(0, 0, 0, 10),
            AcceptsReturn = multiline,
            TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap,
            MinHeight = multiline ? 72 : 0,
            VerticalScrollBarVisibility = multiline ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden,
        };
        box.TextChanged += (_, _) => field.Value = box.Text;
        panel.Children.Add(box);
    }


    private void RefreshPortrait()

    {

        if (_model is null)

            return;



        ImageSource? source = null;

        if (!string.IsNullOrWhiteSpace(_model.PendingImageSourcePath))

            source = EntityMediaService.TryLoadImageFromAbsolute(_model.PendingImageSourcePath, 280);

        else if (!_model.ClearImage)

            source = EntityMediaService.TryLoadImage(_model.AdventureId, _model.ImagePath, 280);



        if (source is null)

        {

            PortraitImage.Source = null;

            PortraitImage.Visibility = Visibility.Collapsed;

            PortraitPlaceholder.Visibility = Visibility.Visible;

            ClearPortraitButton.Visibility = Visibility.Collapsed;

            return;

        }



        PortraitImage.Source = source;

        PortraitImage.Visibility = Visibility.Visible;

        PortraitPlaceholder.Visibility = Visibility.Collapsed;

        ClearPortraitButton.Visibility = Visibility.Visible;

    }



    private void ChoosePortrait_Click(object sender, RoutedEventArgs e)

    {

        if (_model is null)

            return;



        var dlg = new OpenFileDialog

        {

            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.bmp|All files|*.*",

            Title = "Choose portrait or reference image",

        };

        if (dlg.ShowDialog() != true)

            return;



        _model.PendingImageSourcePath = dlg.FileName;

        _model.ClearImage = false;

        RefreshPortrait();

    }



    private void ClearPortrait_Click(object sender, RoutedEventArgs e)

    {

        if (_model is null)

            return;



        _model.PendingImageSourcePath = null;

        _model.ClearImage = true;

        RefreshPortrait();

    }



    private void InlineSave_Click(object sender, RoutedEventArgs e) =>

        SaveRequested?.Invoke(this, EventArgs.Empty);



    private void InlineCancel_Click(object sender, RoutedEventArgs e) =>

        CancelRequested?.Invoke(this, EventArgs.Empty);



    private void InlineDelete_Click(object sender, RoutedEventArgs e) =>

        DeleteRequested?.Invoke(this, EventArgs.Empty);

}



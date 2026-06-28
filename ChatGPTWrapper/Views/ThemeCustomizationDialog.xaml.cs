using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class ThemeCustomizationDialog : ShellDialogWindow
{
    private static readonly JsonSerializerOptions ThemeJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly ThemeApplyOptions PreviewWpfOnly = new(Persist: false, RefreshWebView: false);
    private static readonly ThemeApplyOptions PreviewAll = new(Persist: false, RefreshWebView: true);
    private static readonly ThemeApplyOptions PersistAll = new(Persist: true, RefreshWebView: true);

    private ThemeSettings _original;
    private readonly ThemeSettings _working;
    private readonly Action<ThemeSettings, ThemeApplyOptions>? _applyTheme;
    private readonly Dictionary<string, TextBox> _colorBoxes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _colorSwatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ColorEditorRow> _colorRows = [];
    private readonly HashSet<string> _contrastWarningTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _previewDebounce;
    private readonly List<PresetListItem> _allPresetItems = [];
    private ICollectionView? _presetView;
    private bool _suppressPresetChange;
    private bool _suppressPresetFilterEvents;
    private bool _suppressCategoryEditorEvents;
    private bool _suppressFieldEvents;

    public ThemeSettings ResultSettings { get; private set; }

    public ThemeCustomizationDialog(
        ThemeSettings theme,
        Action<ThemeSettings, ThemeApplyOptions>? applyTheme = null)
    {
        InitializeComponent();
        _original = theme.Clone();
        _working = theme.Clone();
        ResultSettings = theme.Clone();
        _applyTheme = applyTheme;

        _previewDebounce = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _previewDebounce.Tick += (_, _) =>
        {
            _previewDebounce.Stop();
            PushLivePreview(PreviewWpfOnly);
        };

        Closing += (_, _) =>
        {
            if (DialogResult != true)
                _applyTheme?.Invoke(_original.Clone(), PreviewAll);
        };

        BuildColorEditors();
        EnsurePresetCategoryEditorInitialized();
        LoadPresets();
        LoadFieldsFromWorking();
        RefreshPreview();
    }

    private sealed class ColorEditorRow
    {
        public required string TokenKey { get; init; }
        public required string SearchText { get; init; }
        public required FrameworkElement Container { get; init; }
        public required Button ResetButton { get; init; }
        public required TextBlock WarningIcon { get; init; }
    }

    private void BuildColorEditors()
    {
        foreach (var group in ThemeTokenCatalog.All
                     .Where(t => !t.IsDerived)
                     .GroupBy(t => t.Group)
                     .OrderBy(g => g.Key))
        {
            var header = new TextBlock
            {
                Text = ThemeTokenDisplay.GetGroupLabel(group.Key),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 6),
            };
            ColorEditorsPanel.Children.Add(header);

            foreach (var token in group.OrderBy(t => t.TokenKey))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(148) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

                var label = new TextBlock
                {
                    Text = ThemeTokenDisplay.GetLabel(token.TokenKey),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var description = ThemeTokenDisplay.GetDescription(token.TokenKey);
                if (!string.IsNullOrWhiteSpace(description))
                    label.ToolTip = description;

                row.Children.Add(label);

                var swatchButton = new Button
                {
                    Width = 32,
                    Height = 32,
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
                swatchButton.Click += (_, _) => PickColor(token.TokenKey);
                Grid.SetColumn(swatchButton, 1);
                row.Children.Add(swatchButton);

                var box = new TextBox
                {
                    MaxLength = 9,
                    FontFamily = new FontFamily("Consolas"),
                };
                box.TextChanged += (_, _) => OnColorEdited(token.TokenKey, box, swatchInner);
                Grid.SetColumn(box, 2);
                row.Children.Add(box);

                var resetButton = new Button
                {
                    Content = "↺",
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    Margin = new Thickness(4, 0, 0, 0),
                    ToolTip = "Reset to preset",
                    Visibility = Visibility.Collapsed,
                    Style = TryFindResource("ShellCommandBarSecondarySlot") as Style,
                };
                resetButton.Click += (_, _) => ResetTokenColor(token.TokenKey);
                Grid.SetColumn(resetButton, 3);
                row.Children.Add(resetButton);

                var warningIcon = new TextBlock
                {
                    Text = "⚠",
                    Foreground = (Brush)FindResource("WarningBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                    ToolTip = "Low contrast on some surfaces",
                };
                Grid.SetColumn(warningIcon, 4);
                row.Children.Add(warningIcon);

                _colorBoxes[token.TokenKey] = box;
                _colorSwatches[token.TokenKey] = swatchInner;
                _colorRows.Add(new ColorEditorRow
                {
                    TokenKey = token.TokenKey,
                    SearchText = ThemeTokenDisplay.GetSearchText(token),
                    Container = row,
                    ResetButton = resetButton,
                    WarningIcon = warningIcon,
                });
                ColorEditorsPanel.Children.Add(row);
            }
        }
    }

    private sealed class PresetListItem
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string Category { get; init; }
        public int CategoryOrder { get; init; }
        public required string SearchText { get; init; }
        public bool IsUserPreset { get; init; }
        public required Brush Swatch0 { get; init; }
        public required Brush Swatch1 { get; init; }
        public required Brush Swatch2 { get; init; }
        public required Brush Swatch3 { get; init; }
    }

    private void LoadPresets()
    {
        _allPresetItems.Clear();

        foreach (var preset in _working.UserPresets.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            var swatches = ThemeUserPresetService.BuildSwatchColors(preset).Select(TryBrush).ToList();
            while (swatches.Count < 4)
                swatches.Add(Brushes.Gray);

            _allPresetItems.Add(new PresetListItem
            {
                Id = preset.Id,
                Name = preset.Name,
                Description = preset.Description ?? "Custom saved theme",
                Category = ThemePresetNavigation.NormalizeCategory(preset.Category),
                CategoryOrder = ThemePresetNavigation.GetCategoryOrder(preset.Category),
                SearchText = $"{preset.Name} {preset.Description} {preset.Category} {preset.Id}",
                IsUserPreset = true,
                Swatch0 = swatches[0],
                Swatch1 = swatches[1],
                Swatch2 = swatches[2],
                Swatch3 = swatches[3],
            });
        }

        foreach (var preset in ThemePresetLibrary.Presets)
        {
            var swatches = preset.SwatchColors.Select(TryBrush).ToList();
            while (swatches.Count < 4)
                swatches.Add(Brushes.Gray);

            _allPresetItems.Add(new PresetListItem
            {
                Id = preset.Id,
                Name = preset.Name,
                Description = preset.Description,
                Category = preset.Category,
                CategoryOrder = preset.CategoryOrder,
                SearchText = $"{preset.Name} {preset.Description} {preset.Category} {preset.Id}",
                IsUserPreset = false,
                Swatch0 = swatches[0],
                Swatch1 = swatches[1],
                Swatch2 = swatches[2],
                Swatch3 = swatches[3],
            });
        }

        EnsurePresetViewInitialized();
        _presetView!.Refresh();
        RefreshPresetFilterState();
        SelectPresetInList(_working.ActivePresetId);
        UpdatePresetUi();
    }

    private void EnsurePresetViewInitialized()
    {
        if (_presetView is not null)
            return;

        _suppressPresetFilterEvents = true;
        try
        {
            PresetCategoryFilter.ItemsSource = new[] { "All categories" }
                .Concat(ThemePresetCategories.All)
                .ToList();
            PresetCategoryFilter.SelectedIndex = 0;
        }
        finally
        {
            _suppressPresetFilterEvents = false;
        }

        _presetView = CollectionViewSource.GetDefaultView(_allPresetItems);
        _presetView.SortDescriptions.Add(new SortDescription(nameof(PresetListItem.CategoryOrder), ListSortDirection.Ascending));
        _presetView.SortDescriptions.Add(new SortDescription(nameof(PresetListItem.Name), ListSortDirection.Ascending));
        _presetView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PresetListItem.Category)));
        _presetView.Filter = PresetFilter;
        PresetList.ItemsSource = _presetView;
    }

    private PresetListItem? GetSelectedPresetItem() =>
        PresetList.SelectedItem as PresetListItem;

    private ThemeUserPreset? GetSelectedUserPreset()
    {
        var item = GetSelectedPresetItem();
        return item is null || !item.IsUserPreset
            ? null
            : ThemeUserPresetService.Find(_working.UserPresets, item.Id);
    }

    private bool IsUserPresetDirty()
    {
        var preset = GetSelectedUserPreset();
        if (preset is null)
            return false;

        ReadTypographyFields();
        ReadSpacingFields();
        return !ThemeUserPresetService.MatchesSettings(preset, _working);
    }

    private void UpdatePresetUi()
    {
        var selected = GetSelectedPresetItem();
        var userPreset = GetSelectedUserPreset();
        var hasOverrides = _working.CustomOverrides.Count > 0;

        if (selected is null)
        {
            if (hasOverrides)
                PresetStatusText.Text = "Custom tweaks on the current palette — pick a preset or save as new.";
            else if (_working.ActivePresetId.Equals(ThemePresetIds.Custom, StringComparison.OrdinalIgnoreCase))
                PresetStatusText.Text = "Custom palette — select a preset or save your changes.";
            else
                PresetStatusText.Text = string.Empty;
        }
        else if (selected.IsUserPreset && userPreset is not null)
        {
            PresetStatusText.Text = IsUserPresetDirty()
                ? $"“{userPreset.Name}” has unsaved changes."
                : userPreset.Description ?? $"Using saved preset “{userPreset.Name}”.";
        }
        else
        {
            PresetStatusText.Text = hasOverrides
                ? $"“{selected.Name}” with custom color tweaks."
                : selected.Description;
        }

        DuplicatePresetButton.IsEnabled = selected is not null || hasOverrides;
        RenamePresetButton.IsEnabled = userPreset is not null;
        DeletePresetButton.IsEnabled = userPreset is not null;
        SavePresetButton.IsEnabled = userPreset is not null && IsUserPresetDirty();
        SyncPresetCategoryEditor(userPreset);
    }

    private void EnsurePresetCategoryEditorInitialized()
    {
        if (PresetCategoryEditor.Items.Count > 0)
            return;

        foreach (var category in ThemePresetCategories.All)
            PresetCategoryEditor.Items.Add(category);
    }

    private void SyncPresetCategoryEditor(ThemeUserPreset? userPreset)
    {
        if (userPreset is null)
        {
            PresetCategoryEditorPanel.Visibility = Visibility.Collapsed;
            return;
        }

        PresetCategoryEditorPanel.Visibility = Visibility.Visible;
        _suppressCategoryEditorEvents = true;
        try
        {
            var category = ThemePresetNavigation.NormalizeCategory(userPreset.Category);
            PresetCategoryEditor.SelectedItem = PresetCategoryEditor.Items
                .Cast<string>()
                .FirstOrDefault(item => item.Equals(category, StringComparison.OrdinalIgnoreCase))
                ?? ThemePresetCategories.MyPresets;
        }
        finally
        {
            _suppressCategoryEditorEvents = false;
        }
    }

    private void PresetCategoryEditor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCategoryEditorEvents || PresetCategoryEditor.SelectedItem is not string category)
            return;

        var preset = GetSelectedUserPreset();
        if (preset is null)
            return;

        if (!ThemeUserPresetService.TrySetPresetCategory(preset, category, out _))
            return;

        LoadPresets();
        SelectPresetInList(preset.Id);
        UpdatePresetUi();
    }

    private string? GetSelectedPresetCategory() =>
        GetSelectedPresetItem()?.Category;

    private void ApplyPresetSelection(PresetListItem item)
    {
        _working.ActivePresetId = item.Id;
        _working.CustomOverrides.Clear();

        if (item.IsUserPreset)
        {
            var preset = ThemeUserPresetService.Find(_working.UserPresets, item.Id);
            if (preset is not null)
                ThemeUserPresetService.ApplyToSettings(preset, _working);
        }
        else
        {
            ThemeUserPresetService.ClearLayoutOverrides(_working);
        }
    }

    private void NewUserPreset_Click(object sender, RoutedEventArgs e)
    {
        if (!TextPromptDialog.TryPrompt(
                this,
                "New theme preset",
                "Preset name",
                "My theme",
                out var name,
                confirmButtonText: "Create"))
        {
            return;
        }

        ReadTypographyFields();
        ReadSpacingFields();
        var preset = ThemeUserPresetService.CreateFromSettings(
            name,
            _working,
            GetSelectedPresetCategory());
        _working.UserPresets.Add(preset);
        ThemeUserPresetService.ApplyToSettings(preset, _working);
        LoadPresets();
        PushLivePreview(PreviewAll);
    }

    private void DuplicatePreset_Click(object sender, RoutedEventArgs e)
    {
        ReadTypographyFields();
        ReadSpacingFields();

        var selected = GetSelectedPresetItem();
        ThemeUserPreset preset;
        if (selected?.IsUserPreset == true)
        {
            var source = ThemeUserPresetService.Find(_working.UserPresets, selected.Id);
            if (source is null)
                return;

            preset = IsUserPresetDirty()
                ? ThemeUserPresetService.CreateFromSettings($"{source.Name} copy", _working, source.Category)
                : ThemeUserPresetService.CreateCopy($"{source.Name} copy", source);
        }
        else if (selected is not null)
        {
            preset = _working.CustomOverrides.Count > 0
                ? ThemeUserPresetService.CreateFromSettings($"{selected.Name} copy", _working, selected.Category)
                : ThemeUserPresetService.CreateCopyFromBuiltIn($"{selected.Name} copy", selected.Id);
        }
        else
        {
            preset = ThemeUserPresetService.CreateFromSettings("Custom copy", _working);
        }

        _working.UserPresets.Add(preset);
        ThemeUserPresetService.ApplyToSettings(preset, _working);
        LoadPresets();
        PushLivePreview(PreviewAll);
    }

    private void RenamePreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = GetSelectedUserPreset();
        if (preset is null)
            return;

        if (!TextPromptDialog.TryPrompt(this, "Rename preset", "Preset name", preset.Name, out var name, confirmButtonText: "Rename"))
            return;

        if (!ThemeUserPresetService.TryRenamePreset(preset, name, out var error))
        {
            MessageBox.Show(this, error, "Rename preset", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        LoadPresets();
        UpdatePresetUi();
    }

    private void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = GetSelectedUserPreset();
        if (preset is null)
            return;

        if (MessageBox.Show(
                this,
                $"Delete preset “{preset.Name}”?",
                "Delete preset",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        if (!ThemeUserPresetService.TryDeletePreset(_working.UserPresets, preset.Id, out var error))
        {
            MessageBox.Show(this, error, "Delete preset", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _working.ActivePresetId = ThemePresetIds.DefaultDark;
        _working.CustomOverrides.Clear();
        ThemeUserPresetService.ClearLayoutOverrides(_working);
        LoadFieldsFromWorking();
        LoadPresets();
        PushLivePreview(PreviewAll);
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = GetSelectedUserPreset();
        if (preset is null)
            return;

        ReadTypographyFields();
        ReadSpacingFields();
        ThemeUserPresetService.SaveSettingsToPreset(preset, _working);
        _working.CustomOverrides.Clear();
        LoadPresets();
        UpdatePresetUi();
        PushLivePreview(PreviewAll);
    }

    private bool PresetFilter(object item)
    {
        if (item is not PresetListItem preset)
            return false;

        var category = PresetCategoryFilter.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(category)
            && !category.Equals("All categories", StringComparison.OrdinalIgnoreCase)
            && !preset.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            return false;

        var query = PresetSearchBox.Text.Trim();
        if (query.Length == 0)
            return true;

        return preset.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshPresetFilterState()
    {
        _presetView?.Refresh();
        var visible = _presetView?.Cast<PresetListItem>().ToList() ?? [];
        PresetEmptyHint.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PresetList.IsEnabled = visible.Count > 0;

        if (PresetList.SelectedItem is PresetListItem selected
            && visible.All(item => !ReferenceEquals(item, selected)))
        {
            _suppressPresetChange = true;
            try
            {
                PresetList.SelectedItem = null;
            }
            finally
            {
                _suppressPresetChange = false;
            }
        }
    }

    private void PresetSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPresetFilterEvents)
            return;

        RefreshPresetFilterState();
    }

    private void PresetCategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetFilterEvents)
            return;

        RefreshPresetFilterState();
    }

    private void SelectPresetInList(string presetId)
    {
        if (presetId.Equals(ThemePresetIds.Custom, StringComparison.OrdinalIgnoreCase))
        {
            _suppressPresetChange = true;
            try
            {
                PresetList.SelectedItem = null;
            }
            finally
            {
                _suppressPresetChange = false;
            }

            UpdatePresetUi();
            return;
        }

        _suppressPresetChange = true;
        try
        {
            PresetListItem? match = null;
            if (_presetView is not null)
            {
                foreach (PresetListItem item in _presetView)
                {
                    if (item.Id.Equals(presetId, StringComparison.OrdinalIgnoreCase))
                    {
                        match = item;
                        break;
                    }
                }
            }

            PresetList.SelectedItem = match;
            if (match is not null)
                PresetList.ScrollIntoView(match);
        }
        finally
        {
            _suppressPresetChange = false;
        }
    }

    private void LoadFieldsFromWorking()
    {
        _suppressFieldEvents = true;
        try
        {
            var resolved = ThemeApplicationService.ResolveEffectiveTheme(_working);

            foreach (var (tokenKey, box) in _colorBoxes)
            {
                var hex = resolved.GetHex(tokenKey);
                box.Text = hex;
                if (_colorSwatches.TryGetValue(tokenKey, out var swatch))
                    swatch.Background = TryBrush(hex);
            }

            FontFamilyBox.Text = resolved.FontFamily;
            FontSizeBodyBox.Text = resolved.FontSizeBody.ToString("0.##");
            FontSizeTitleBox.Text = resolved.FontSizeTitle.ToString("0.##");
            FontSizeHintBox.Text = resolved.FontSizeHint.ToString("0.##");
            SpaceXsBox.Text = resolved.SpaceXs.ToString("0.##");
            SpaceSmBox.Text = resolved.SpaceSm.ToString("0.##");
            SpaceMdBox.Text = resolved.SpaceMd.ToString("0.##");
            SpaceLgBox.Text = resolved.SpaceLg.ToString("0.##");
            SpaceXlBox.Text = resolved.SpaceXl.ToString("0.##");
            RadiusControlBox.Text = resolved.RadiusControl.ToString("0.##");
            RadiusCardBox.Text = resolved.RadiusCard.ToString("0.##");
        }
        finally
        {
            _suppressFieldEvents = false;
        }

        RefreshTokenResetStates();
        RefreshContrastUi();
        RefreshColorFilter();
        UpdatePresetUi();
    }

    private void OnColorEdited(string tokenKey, TextBox box, Border swatch)
    {
        if (_suppressFieldEvents)
            return;

        var hex = box.Text.Trim();
        swatch.Background = TryBrush(hex);

        if (hex.Length is 7 or 6 or 9)
        {
            _working.CustomOverrides[tokenKey] = hex.StartsWith('#') ? hex : "#" + hex;
            QueueDebouncedPreview();
        }

        RefreshTokenResetStates();
        RefreshContrastUi();
        UpdatePresetUi();
    }

    private void ResetTokenColor(string tokenKey)
    {
        _working.CustomOverrides.Remove(tokenKey);

        LoadFieldsFromWorking();
        PushLivePreview(PreviewAll);
    }

    private void RefreshTokenResetStates()
    {
        foreach (var row in _colorRows)
        {
            var customized = ThemeApplicationService.IsTokenCustomized(_working, row.TokenKey);
            row.ResetButton.Visibility = customized ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RefreshContrastUi()
    {
        _contrastWarningTokens.Clear();
        var failures = ThemeApplicationService.ValidateUserTokens(_working);
        foreach (var failure in failures)
            _contrastWarningTokens.Add(failure.ForegroundToken);

        foreach (var row in _colorRows)
        {
            var hasWarning = _contrastWarningTokens.Contains(row.TokenKey);
            row.WarningIcon.Visibility = hasWarning ? Visibility.Visible : Visibility.Collapsed;
        }

        if (failures.Count == 0)
        {
            ContrastSummary.Visibility = Visibility.Collapsed;
            return;
        }

        ContrastSummary.Visibility = Visibility.Visible;
        ContrastSummary.Text = failures.Count == 1
            ? "1 contrast warning — colors auto-adjust on apply, or use Auto-fix contrast."
            : $"{failures.Count} contrast warnings — colors auto-adjust on apply, or use Auto-fix contrast.";
    }

    private void ColorSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        RefreshColorFilter();

    private void RefreshColorFilter()
    {
        var query = ColorSearchBox.Text.Trim();
        foreach (var row in _colorRows)
        {
            var visible = query.Length == 0
                || row.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (_colorBoxes.TryGetValue(row.TokenKey, out var box)
                    && box.Text.Contains(query, StringComparison.OrdinalIgnoreCase));

            row.Container.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void AutoFixContrast_Click(object sender, RoutedEventArgs e)
    {
        ThemeApplicationService.EnforceUserTokenContrast(_working);
        LoadFieldsFromWorking();
        SelectPresetInList(_working.ActivePresetId);
        PushLivePreview(PreviewAll);
    }

    private void PickColor(string tokenKey)
    {
        if (!_colorBoxes.TryGetValue(tokenKey, out var box))
            return;

        var current = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(current))
            current = ThemeApplicationService.ResolveEffectiveTheme(_working).GetHex(tokenKey);

        var background = ColorPickerContextResolver.ResolveThemeTokenBackground(tokenKey, _working);
        var context = ColorPickerContextFactory.ForThemeToken(tokenKey, background);
        if (!ColorPickerWorkflow.TryPickHex(this, current, background, context, out var selected))
            return;

        box.Text = selected;
    }

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChange || PresetList.SelectedItem is not PresetListItem item)
            return;

        if (item.Id.Equals(_working.ActivePresetId, StringComparison.OrdinalIgnoreCase)
            && _working.CustomOverrides.Count == 0
            && (!item.IsUserPreset || !IsUserPresetDirty()))
            return;

        ApplyPresetSelection(item);
        LoadFieldsFromWorking();
        PushLivePreview(PreviewAll);
    }

    private void TypographyField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFieldEvents)
            return;

        QueueDebouncedPreview();
        UpdatePresetUi();
    }

    private void SpacingField_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressFieldEvents)
            return;

        QueueDebouncedPreview();
        UpdatePresetUi();
    }

    private void QueueDebouncedPreview()
    {
        _previewDebounce.Stop();
        _previewDebounce.Start();
    }

    private void PushLivePreview(ThemeApplyOptions options)
    {
        ReadTypographyFields();
        ReadSpacingFields();
        var clone = _working.Clone();
        _applyTheme?.Invoke(clone, options);
        RefreshPreview();
    }

    private void PersistWorkingTheme()
    {
        ReadTypographyFields();
        ReadSpacingFields();
        ResultSettings = _working.Clone();
        _original = _working.Clone();
        _applyTheme?.Invoke(ResultSettings, PersistAll);
        RefreshPreview();
    }

    private void ReadTypographyFields()
    {
        _working.FontFamily = string.IsNullOrWhiteSpace(FontFamilyBox.Text) ? null : FontFamilyBox.Text.Trim();
        _working.FontSizeBody = TryParseDouble(FontSizeBodyBox.Text);
        _working.FontSizeTitle = TryParseDouble(FontSizeTitleBox.Text);
        _working.FontSizeHint = TryParseDouble(FontSizeHintBox.Text);
    }

    private void ReadSpacingFields()
    {
        _working.SpaceXs = TryParseDouble(SpaceXsBox.Text);
        _working.SpaceSm = TryParseDouble(SpaceSmBox.Text);
        _working.SpaceMd = TryParseDouble(SpaceMdBox.Text);
        _working.SpaceLg = TryParseDouble(SpaceLgBox.Text);
        _working.SpaceXl = TryParseDouble(SpaceXlBox.Text);
        _working.RadiusControl = TryParseDouble(RadiusControlBox.Text);
        _working.RadiusCard = TryParseDouble(RadiusCardBox.Text);
    }

    private void RefreshPreview()
    {
        var resolved = ThemeApplicationService.ResolveEffectiveTheme(_working);
        PreviewTitle.FontSize = resolved.FontSizeTitle;
    }

    private void ResetPreset_Click(object sender, RoutedEventArgs e)
    {
        var presetId = _working.ActivePresetId;
        if (string.IsNullOrWhiteSpace(presetId) || presetId == ThemePresetIds.Custom)
            presetId = ThemePresetIds.DefaultDark;

        _working.ActivePresetId = presetId;
        _working.CustomOverrides.Clear();
        LoadFieldsFromWorking();
        SelectPresetInList(presetId);
        PushLivePreview(PreviewAll);
    }

    private void ResetAll_Click(object sender, RoutedEventArgs e)
    {
        var defaults = ThemeApplicationService.CreateDefaultSettings();
        _working.ActivePresetId = defaults.ActivePresetId;
        _working.CustomOverrides.Clear();
        _working.FontFamily = null;
        _working.FontSizeBody = null;
        _working.FontSizeTitle = null;
        _working.FontSizeHint = null;
        _working.SpaceSm = null;
        _working.SpaceMd = null;
        _working.SpaceLg = null;
        _working.SpaceXs = null;
        _working.SpaceXl = null;
        _working.RadiusControl = null;
        _working.RadiusCard = null;
        LoadFieldsFromWorking();
        SelectPresetInList(_working.ActivePresetId);
        PushLivePreview(PreviewAll);
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        ReadTypographyFields();
        ReadSpacingFields();
        ResultSettings = _working.Clone();
        _applyTheme?.Invoke(ResultSettings, PersistAll);
        DialogResult = true;
        Close();
    }

    private void ExportTheme_Click(object sender, RoutedEventArgs e)
    {
        ReadTypographyFields();
        ReadSpacingFields();
        var dlg = new SaveFileDialog
        {
            Filter = "Theme JSON (*.json)|*.json|All files|*.*",
            FileName = "chatgpt-wrapper-theme.json",
        };

        if (dlg.ShowDialog() != true)
            return;

        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(_working, ThemeJsonOptions));
    }

    private void ImportTheme_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Theme JSON (*.json)|*.json|All files|*.*",
            Multiselect = true,
        };

        if (dlg.ShowDialog() != true)
            return;

        try
        {
            var fileImports = new List<(string SourceLabel, ThemeImportResult Result)>();
            var errors = new List<string>();

            foreach (var fileName in dlg.FileNames.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var parsed = ThemeImportService.Parse(File.ReadAllText(fileName), ThemeJsonOptions);
                    fileImports.Add((Path.GetFileNameWithoutExtension(fileName), parsed));
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                MessageBox.Show(
                    this,
                    string.Join(Environment.NewLine, errors),
                    "Import theme",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var import = ThemeImportService.Combine(fileImports);
            if (import.PresetCount == 0 && import.ThemeToApply is null)
                throw new InvalidDataException("Files did not contain theme settings.");

            if (import.UseBulkImportFlow)
            {
                if (!TryConfirmMultiPresetImport(import, out var applyImported))
                    return;

                ThemeUserPresetService.MergeImportedPresets(_working.UserPresets, import.PresetsToMerge);

                if (applyImported && import.PresetsToMerge.Count > 0)
                    ApplyImportedSelection(import, import.PresetsToMerge[0]);
            }
            else
            {
                var imported = import.ThemeToApply
                    ?? throw new InvalidDataException("File did not contain theme settings.");
                var defaultName = fileImports[0].SourceLabel;

                var choice = MessageBox.Show(
                    this,
                    "Save imported theme as a preset?\n\nYes — add to My presets and apply.\nNo — apply to the working copy only.\nCancel — abort import.",
                    "Import theme JSON",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Cancel)
                    return;

                if (choice == MessageBoxResult.Yes)
                {
                    if (!TextPromptDialog.TryPrompt(
                            this,
                            "Save imported theme",
                            "Preset name",
                            defaultName,
                            out var name,
                            confirmButtonText: "Save"))
                    {
                        return;
                    }

                    ThemeUserPresetService.MergeImportedPresets(_working.UserPresets, import.PresetsToMerge);
                    var preset = ThemeUserPresetService.SaveImportedThemeAsPreset(_working.UserPresets, imported, name);
                    ThemeUserPresetService.ApplyToSettings(preset, _working);
                }
                else
                {
                    ThemeUserPresetService.ApplyImportedThemeFields(_working, imported, mergeUserPresets: true);
                }
            }

            LoadPresets();
            LoadFieldsFromWorking();
            SelectPresetInList(_working.ActivePresetId);
            PersistWorkingTheme();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Import theme", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private bool TryConfirmMultiPresetImport(ThemeImportResult import, out bool applyImported)
    {
        applyImported = false;

        var scope = import.IsMultiFileImport
            ? $"{import.PresetCount} theme presets from {import.SourceFileCount} files"
            : $"{import.PresetCount} theme presets";

        var message = import.ThemeToApply is null
            ? $"Import {scope} into your library?\n\nYes — merge presets and apply the first one.\nNo — merge presets only.\nCancel — abort import."
            : $"Import {scope}?\n\nYes — merge presets and apply the exported active theme from the first file.\nNo — merge presets only.\nCancel — abort import.";

        var choice = MessageBox.Show(
            this,
            message,
            "Import theme JSON",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (choice == MessageBoxResult.Cancel)
            return false;

        applyImported = choice == MessageBoxResult.Yes;
        return true;
    }

    private void ApplyImportedSelection(ThemeImportResult import, ThemeUserPreset fallbackPreset)
    {
        if (import.ThemeToApply is not null)
        {
            ThemeUserPresetService.ApplyImportedThemeFields(_working, import.ThemeToApply, mergeUserPresets: false);

            if (ThemePresetIds.IsUserPresetId(_working.ActivePresetId)
                && ThemeUserPresetService.Find(_working.UserPresets, _working.ActivePresetId) is not null)
            {
                return;
            }
        }

        ThemeUserPresetService.ApplyToSettings(fallbackPreset, _working);
    }

    private void OpenStylesFolder_Click(object sender, RoutedEventArgs e)
    {
        AppDirectories.EnsureCreated();
        Process.Start(new ProcessStartInfo
        {
            FileName = AppDirectories.StylesDirectory,
            UseShellExecute = true,
        });
    }

    private void OpenUserCss_Click(object sender, RoutedEventArgs e)
    {
        AppDirectories.EnsureCreated();
        var path = Path.Combine(AppDirectories.StylesDirectory, "user-overrides.css");
        if (!File.Exists(path))
            File.WriteAllText(path, "/* User CSS overrides — loaded after theme variables */\n");

        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    private static double? TryParseDouble(string? text) =>
        double.TryParse(text, out var value) ? value : null;

    private static bool TryParseColor(string hex, out Color color)
    {
        color = default;
        try
        {
            var normalized = hex.Trim();
            if (!normalized.StartsWith('#'))
                normalized = "#" + normalized;

            color = (Color)ColorConverter.ConvertFromString(normalized);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Brush TryBrush(string hex)
    {
        try
        {
            return ThemeBrushCache.GetBrush(hex);
        }
        catch
        {
            return Brushes.Transparent;
        }
    }
}

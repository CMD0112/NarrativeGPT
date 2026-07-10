using ChatGPTWrapper;
using ChatGPTWrapper.Theme;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Views.Dialogs;

public sealed partial class AppearanceThemePage : UserControl
{
    private readonly ThemeSettings _original;
    private readonly ThemeSettings _working;
    private readonly Action<ThemeSettings, ThemeApplyOptions> _applyTheme;
    private readonly List<AppearancePresetItem> _allPresets = [];
    private bool _suppressSelection;

    public AppearanceThemePage(
        ThemeSettings theme,
        Action<ThemeSettings, ThemeApplyOptions> applyTheme)
    {
        _applyTheme = applyTheme;
        _original = theme.Clone();
        _working = theme.Clone();
        InitializeComponent();
        LoadPresets();
        Loaded += (_, _) => SelectCurrentPreset();
    }

    public ThemeSettings ResultSettings => _working.Clone();

    public void Commit()
    {
        var chrome = UiChromeStore.Load();
        chrome.Theme = _working.Clone();
        UiChromeStore.Save(chrome);
        _applyTheme(_working.Clone(), new ThemeApplyOptions(Persist: true));
    }

    public void RevertPreview() =>
        _applyTheme(_original.Clone(), new ThemeApplyOptions(Persist: false));

    private void LoadPresets()
    {
        _allPresets.Clear();
        foreach (var preset in ThemePresetLibrary.Presets)
        {
            _allPresets.Add(new AppearancePresetItem
            {
                Id = preset.Id,
                Name = preset.Name,
                Description = preset.Description,
                Category = preset.Category,
            });
        }

        ApplyFilter();
    }

    private void SelectCurrentPreset()
    {
        _suppressSelection = true;
        try
        {
            var match = _allPresets.FirstOrDefault(p =>
                string.Equals(p.Id, _working.ActivePresetId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                PresetList.SelectedItem = match;
        }
        finally
        {
            _suppressSelection = false;
        }

        PresetStatusText.Text = $"Active preset: {_working.ActivePresetId}";
    }

    private void ApplyFilter()
    {
        var query = PresetSearchBox?.Text?.Trim() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allPresets
            : _allPresets.Where(p =>
                    p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || p.Category.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

        PresetList.ItemsSource = filtered;
    }

    private void PresetSearchBox_TextChanged(object sender, TextChangedEventArgs e) =>
        ApplyFilter();

    private void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || PresetList.SelectedItem is not AppearancePresetItem item)
            return;

        _working.ActivePresetId = item.Id;
        _working.CustomOverrides.Clear();
        ThemeUserPresetService.ClearLayoutOverrides(_working);
        PresetStatusText.Text = $"Previewing {item.Name}";
        _applyTheme(_working.Clone(), new ThemeApplyOptions(Persist: false));
    }

    public sealed class AppearancePresetItem
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required string Category { get; init; }
    }
}

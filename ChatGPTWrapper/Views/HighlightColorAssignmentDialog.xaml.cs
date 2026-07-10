using System.IO;
using System.Text.Json;
using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;
using Microsoft.Win32;

namespace ChatGPTWrapper.Views;

public partial class HighlightColorAssignmentDialog : ShellDialogWindow
{
    private static readonly JsonSerializerOptions ProfileJsonOptions = new() { WriteIndented = true };

    private readonly UiChromeSettings _working;
    private readonly ResolvedTheme _theme;
    private bool _suppressEvents;
    private string _selectedProfileId = HighlightColorProfileIds.ThemeHarmony;
    private HighlightColorAssignmentOptions _options = new();

    public string SelectedProfileId { get; private set; } = HighlightColorProfileIds.ThemeHarmony;

    public HighlightColorAssignmentOptions ResultOptions { get; private set; } = new();

    public HighlightColorAssignmentDialog(UiChromeSettings chrome)
    {
        InitializeComponent();

        _working = chrome;
        HighlightColorProfileService.Normalize(_working);
        _theme = ThemeRuntime.Current;
        _selectedProfileId = HighlightColorProfileService.ResolveInitialProfileId(_working);
        _options = HighlightColorProfileService.ResolveEffectiveOptions(_working);
        SelectedProfileId = _selectedProfileId;
        ResultOptions = _options.Clone();

        PopulateCombos();
        LoadFields();
        RefreshPreview();
    }

    public static bool? Show(Window? owner, UiChromeSettings chrome, out string profileId, out HighlightColorAssignmentOptions options)
    {
        profileId = HighlightColorProfileIds.ThemeHarmony;
        options = new HighlightColorAssignmentOptions();

        var dialog = new HighlightColorAssignmentDialog(chrome) { Owner = owner };
        if (dialog.ShowDialog() != true)
            return false;

        profileId = dialog.SelectedProfileId;
        options = dialog.ResultOptions.Clone();
        return true;
    }

    private void PopulateCombos()
    {
        ProfileCombo.ItemsSource = HighlightColorProfileService.ListSelectableProfiles(_working);

        BindEnumCombo(PaletteSourceCombo, typeof(HighlightPaletteSource));
        BindEnumCombo(HueAnchorCombo, typeof(HighlightHueAnchor));
        BindEnumCombo(CanvasSourceCombo, typeof(HighlightCanvasSource));
        BindEnumCombo(StrategyCombo, typeof(HighlightAssignmentStrategy));
        BindEnumCombo(PlayerColorCombo, typeof(HighlightPlayerColorMode));
        BindEnumCombo(AliasColorCombo, typeof(HighlightAliasColorMode));
    }

    private static void BindEnumCombo(ComboBox combo, Type enumType) =>
        combo.ItemsSource = Enum.GetNames(enumType);

    private void LoadFields()
    {
        _suppressEvents = true;
        try
        {
            SelectProfileCombo(_selectedProfileId);
            ProfileDescriptionText.Text = DescribeProfile(_selectedProfileId);

            PaletteSourceCombo.SelectedItem = _options.PaletteSource.ToString();
            HueAnchorCombo.SelectedItem = _options.HueAnchor.ToString();
            HueStepBox.Text = _options.HueStepDegrees.ToString("0.###");
            GeneratedCountBox.Text = _options.GeneratedColorCount.ToString();
            CanvasSourceCombo.SelectedItem = _options.CanvasSource.ToString();
            StrategyCombo.SelectedItem = _options.AssignmentStrategy.ToString();
            PlayerColorCombo.SelectedItem = _options.PlayerColorMode.ToString();
            AliasColorCombo.SelectedItem = _options.AliasColorMode.ToString();
            MinContrastSlider.Value = _options.MinContrastRatio;
            AssignmentSaltBox.Text = _options.AssignmentSalt.ToString();
            AvoidDuplicatesCheck.IsChecked = _options.AvoidDuplicateColors;

            SaturationAutoCheck.IsChecked = _options.Saturation is null;
            SaturationSlider.IsEnabled = _options.Saturation is not null;
            if (_options.Saturation is not null)
                SaturationSlider.Value = _options.Saturation.Value;

            LightnessAutoCheck.IsChecked = _options.Lightness is null;
            LightnessSlider.IsEnabled = _options.Lightness is not null;
            if (_options.Lightness is not null)
                LightnessSlider.Value = _options.Lightness.Value;

            PlayerCustomColorBox.Text = _options.PlayerCustomColor ?? "#FFD166";
            UpdateDynamicPanels();
            RefreshCustomSeedSwatches();
            UpdateStrategyHint();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void SelectProfileCombo(string profileId)
    {
        if (ProfileCombo.ItemsSource is not IEnumerable<HighlightColorAssignmentProfile> profiles)
            return;

        ProfileCombo.SelectedItem = profiles.FirstOrDefault(p =>
            p.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase));
    }

    private string DescribeProfile(string profileId)
    {
        if (profileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            return "Custom options — save from the Highlights tab or export JSON below.";

        return HighlightColorProfileLibrary.Find(_working.HighlightColorProfiles, profileId)?.Description
               ?? string.Empty;
    }

    private void ProfileCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressEvents || ProfileCombo.SelectedItem is not HighlightColorAssignmentProfile profile)
            return;

        _selectedProfileId = profile.Id;
        ProfileDescriptionText.Text = profile.Description ?? string.Empty;

        if (profile.Id.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            _options = _working.HighlightColorCustomOptions.Clone();
        else
            _options = profile.Options.Clone();

        LoadFields();
        RefreshPreview();
    }

    private void PlayerColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDynamicPanels();
        Option_Changed(sender, e);
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppressEvents)
            return;

        if (!TryReadFields(out var options, out var error))
        {
            ShowValidation(error);
            return;
        }

        HideValidation();
        _options = options;
        _selectedProfileId = HighlightColorProfileService.ResolveActiveProfileId(_working, _options, _selectedProfileId);
        if (_selectedProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            SelectProfileCombo(HighlightColorProfileIds.Custom);

        UpdateDynamicPanels();
        UpdateStrategyHint();
        RefreshPreview();
    }

    private void UpdateDynamicPanels()
    {
        var paletteSource = PaletteSourceCombo.SelectedItem as string;
        CustomSeedsPanel.Visibility = string.Equals(paletteSource, nameof(HighlightPaletteSource.CustomSeeds), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        var playerMode = PlayerColorCombo.SelectedItem as string;
        PlayerCustomColorPanel.Visibility = string.Equals(playerMode, nameof(HighlightPlayerColorMode.Custom), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;

        SaturationSlider.IsEnabled = SaturationAutoCheck.IsChecked != true;
        LightnessSlider.IsEnabled = LightnessAutoCheck.IsChecked != true;
    }

    private void UpdateStrategyHint()
    {
        if (StrategyHintText is null)
            return;

        StrategyHintText.Text = (StrategyCombo.SelectedItem as string) switch
        {
            nameof(HighlightAssignmentStrategy.OptimalDistinct) =>
                "Optimal distinct picks the palette color farthest from colors already assigned — best for readability and separation.",
            nameof(HighlightAssignmentStrategy.Sequential) =>
                "Sequential walks the palette in discovery order — colors change when you reroll.",
            nameof(HighlightAssignmentStrategy.StableHash) =>
                "Stable identity keeps the same phrase on the same palette slot; reroll shifts all assignments via salt.",
            nameof(HighlightAssignmentStrategy.RoleBuckets) =>
                "Role buckets separate player, party, and cast across palette regions.",
            nameof(HighlightAssignmentStrategy.RoleBased) =>
                "Role-based uses optimal distinct selection without bucket offsets (legacy alias for optimal distinct).",
            _ => string.Empty,
        };
    }

    private bool TryReadFields(out HighlightColorAssignmentOptions options, out string error)
    {
        options = _options.Clone();
        error = "";

        if (!Enum.TryParse<HighlightPaletteSource>(PaletteSourceCombo.SelectedItem as string, out var paletteSource))
        {
            error = "Choose a palette source.";
            return false;
        }

        if (!Enum.TryParse<HighlightHueAnchor>(HueAnchorCombo.SelectedItem as string, out var hueAnchor))
        {
            error = "Choose a hue anchor.";
            return false;
        }

        if (!double.TryParse(HueStepBox.Text.Trim(), out var hueStep) || hueStep <= 0 || hueStep > 360)
        {
            error = "Hue step must be between 0 and 360 degrees.";
            return false;
        }

        if (!int.TryParse(GeneratedCountBox.Text.Trim(), out var generated)
            || generated < 0
            || (generated > 0 && (generated < 4 || generated > HighlightColorCatalog.MaxGeneratedColors)))
        {
            error = $"Generated colors must be 0 (auto) or between 4 and {HighlightColorCatalog.MaxGeneratedColors}.";
            return false;
        }

        if (!Enum.TryParse<HighlightCanvasSource>(CanvasSourceCombo.SelectedItem as string, out var canvasSource))
        {
            error = "Choose a contrast canvas.";
            return false;
        }

        if (!Enum.TryParse<HighlightAssignmentStrategy>(StrategyCombo.SelectedItem as string, out var strategy))
        {
            error = "Choose an assignment strategy.";
            return false;
        }

        if (!Enum.TryParse<HighlightPlayerColorMode>(PlayerColorCombo.SelectedItem as string, out var playerMode))
        {
            error = "Choose a player color mode.";
            return false;
        }

        if (!Enum.TryParse<HighlightAliasColorMode>(AliasColorCombo.SelectedItem as string, out var aliasMode))
        {
            error = "Choose an alias color mode.";
            return false;
        }

        if (!int.TryParse(AssignmentSaltBox.Text.Trim(), out var salt) || salt < 0 || salt > 9999)
        {
            error = "Assignment salt must be between 0 and 9999.";
            return false;
        }

        options.PaletteSource = paletteSource;
        options.HueAnchor = hueAnchor;
        options.HueStepDegrees = hueStep;
        options.GeneratedColorCount = generated;
        options.CanvasSource = canvasSource;
        options.AssignmentStrategy = strategy;
        options.PlayerColorMode = playerMode;
        options.AliasColorMode = aliasMode;
        options.MinContrastRatio = MinContrastSlider.Value;
        options.AvoidDuplicateColors = AvoidDuplicatesCheck.IsChecked == true;
        options.AssignmentSalt = salt;
        options.Saturation = SaturationAutoCheck.IsChecked == true ? null : SaturationSlider.Value;
        options.Lightness = LightnessAutoCheck.IsChecked == true ? null : LightnessSlider.Value;
        options.PlayerCustomColor = playerMode == HighlightPlayerColorMode.Custom
            ? PlayerCustomColorBox.Text.Trim()
            : null;
        options.CustomSeedColors = _options.CustomSeedColors.Select(c => c).ToList();
        return true;
    }

    private void ShuffleAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(AssignmentSaltBox.Text.Trim(), out var salt))
            salt = 0;
        AssignmentSaltBox.Text = (salt + 1).ToString();
        Option_Changed(sender, e);
    }

    private void AddSeedColor_Click(object sender, RoutedEventArgs e)
    {
        var canvas = ThemeRuntime.Current.GetHex("BgBase");
        var context = ColorPickerContextFactory.ForGeneric(canvas);
        if (!ColorPickerWorkflow.TryPickHex(this, "#FFD166", canvas, context, out var picked))
            return;

        _options.CustomSeedColors.Add(picked);
        RefreshCustomSeedSwatches();
        Option_Changed(sender, e);
    }

    private void ClearSeeds_Click(object sender, RoutedEventArgs e)
    {
        _options.CustomSeedColors.Clear();
        RefreshCustomSeedSwatches();
        Option_Changed(sender, e);
    }

    private void RefreshCustomSeedSwatches()
    {
        CustomSeedsSwatchesPanel.Children.Clear();
        foreach (var color in _options.CustomSeedColors)
        {
            var swatch = new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                Background = CreateBrush(color),
                ToolTip = color,
                Tag = color,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _options.CustomSeedColors.Remove(color);
                RefreshCustomSeedSwatches();
                Option_Changed(swatch, new RoutedEventArgs());
            };
            CustomSeedsSwatchesPanel.Children.Add(swatch);
        }
    }

    private void PickPlayerCustomColor_Click(object sender, RoutedEventArgs e)
    {
        var canvas = ThemeRuntime.Current.GetHex("BgBase");
        var context = ColorPickerContextFactory.ForGeneric(canvas);
        if (!ColorPickerWorkflow.TryPickHex(this, PlayerCustomColorBox.Text, canvas, context, out var picked))
            return;

        PlayerCustomColorBox.Text = picked;
        Option_Changed(sender, e);
    }

    private void ExportProfileJson_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFields(out var options, out var error))
        {
            ShowValidation(error);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            FileName = "highlight-color-profile.json",
            Title = "Export color profile",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        var payload = new HighlightColorProfileExportPayload
        {
            Name = ProfileCombo.SelectedItem is HighlightColorAssignmentProfile p ? p.Name : "Custom",
            Options = options,
        };
        File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(payload, ProfileJsonOptions));
    }

    private void ImportProfileJson_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json",
            Title = "Import color profile",
        };
        if (dialog.ShowDialog(this) != true)
            return;

        try
        {
            var payload = JsonSerializer.Deserialize<HighlightColorProfileExportPayload>(File.ReadAllText(dialog.FileName));
            if (payload?.Options is null)
            {
                ShowValidation("No profile options found in file.");
                return;
            }

            _options = payload.Options.Clone();
            _selectedProfileId = HighlightColorProfileIds.Custom;
            LoadFields();
            RefreshPreview();
            HideValidation();
        }
        catch (Exception ex)
        {
            ShowValidation($"Could not import profile: {ex.Message}");
        }
    }

    private void RefreshPreview()
    {
        PalettePreviewPanel.Children.Clear();
        var canvas = ResolveCanvas(_options);
        foreach (var color in HighlightColorAssignmentEngine.BuildPalette(_options, _theme, canvas))
        {
            PalettePreviewPanel.Children.Add(new Border
            {
                Width = 22,
                Height = 22,
                Margin = new Thickness(0, 0, 6, 6),
                CornerRadius = new CornerRadius(4),
                BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
                BorderThickness = new Thickness(1),
                Background = CreateBrush(color),
                ToolTip = color,
            });
        }
    }

    private string ResolveCanvas(HighlightColorAssignmentOptions options) =>
        HighlightColorAssignmentEngine.ResolveCanvas(options, _theme, ResolveHighlightCanvas());

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

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadFields(out var options, out var error))
        {
            ShowValidation(error);
            return;
        }

        SelectedProfileId = HighlightColorProfileService.ResolveActiveProfileId(_working, options, _selectedProfileId);
        ResultOptions = options.Clone();
        DialogResult = true;
    }

    private static string ResolveHighlightCanvas()
    {
        if (Application.Current?.Resources["BgBaseBrush"] is SolidColorBrush brush)
            return $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";

        return ThemeRuntime.Current.GetHex("BgBase");
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private void HideValidation()
    {
        ValidationText.Visibility = Visibility.Collapsed;
        ValidationText.Text = "";
    }

    private sealed class HighlightColorProfileExportPayload
    {
        public string Name { get; set; } = "";

        public HighlightColorAssignmentOptions Options { get; set; } = new();
    }
}

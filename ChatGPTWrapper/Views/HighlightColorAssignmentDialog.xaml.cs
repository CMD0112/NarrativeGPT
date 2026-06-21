using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class HighlightColorAssignmentDialog : Window
{
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
        HighlightColorAssignmentService.Normalize(_working);
        _theme = ThemeRuntime.Current;
        _selectedProfileId = HighlightColorAssignmentService.ResolveInitialProfileId(_working);
        _options = HighlightColorAssignmentService.ResolveEffectiveOptions(_working);
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
        ProfileCombo.ItemsSource = _working.HighlightColorProfiles
            .Where(p => p.IsBuiltIn)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Concat(
            [
                new HighlightColorAssignmentProfile
                {
                    Id = HighlightColorProfileIds.Custom,
                    Name = "Custom",
                    Description = "Your customized options.",
                    IsBuiltIn = false,
                    Options = _working.HighlightColorCustomOptions.Clone(),
                },
            ])
            .ToList();

        BindEnumCombo(PaletteSourceCombo, typeof(HighlightPaletteSource));
        BindEnumCombo(HueAnchorCombo, typeof(HighlightHueAnchor));
        BindEnumCombo(CanvasSourceCombo, typeof(HighlightCanvasSource));
        BindEnumCombo(StrategyCombo, typeof(HighlightAssignmentStrategy));
        BindEnumCombo(PlayerColorCombo, typeof(HighlightPlayerColorMode));
        BindEnumCombo(AliasColorCombo, typeof(HighlightAliasColorMode));
    }

    private static void BindEnumCombo(ComboBox combo, Type enumType)
    {
        combo.ItemsSource = Enum.GetNames(enumType);
    }

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
            MinContrastBox.Text = _options.MinContrastRatio.ToString("0.##");
            AvoidDuplicatesCheck.IsChecked = _options.AvoidDuplicateColors;
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
            return "Custom options — saved separately from built-in presets.";

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
        _selectedProfileId = HighlightColorAssignmentService.ResolveActiveProfileId(_working, _options, _selectedProfileId);
        if (_selectedProfileId.Equals(HighlightColorProfileIds.Custom, StringComparison.OrdinalIgnoreCase))
            SelectProfileCombo(HighlightColorProfileIds.Custom);

        RefreshPreview();
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

        if (!int.TryParse(GeneratedCountBox.Text.Trim(), out var generated) || generated < 4 || generated > 48)
        {
            error = "Generated colors must be between 4 and 48.";
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

        if (!double.TryParse(MinContrastBox.Text.Trim(), out var minContrast) || minContrast < 3 || minContrast > 12)
        {
            error = "Min contrast must be between 3 and 12.";
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
        options.MinContrastRatio = minContrast;
        options.AvoidDuplicateColors = AvoidDuplicatesCheck.IsChecked == true;
        return true;
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

        SelectedProfileId = HighlightColorAssignmentService.ResolveActiveProfileId(_working, options, _selectedProfileId);
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
}

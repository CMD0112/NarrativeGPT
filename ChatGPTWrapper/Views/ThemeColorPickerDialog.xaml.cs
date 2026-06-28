using System.Windows;
using ChatGPTWrapper.Shell;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChatGPTWrapper.Format;
using ChatGPTWrapper.Theme;

namespace ChatGPTWrapper.Views;

public partial class ThemeColorPickerDialog : ShellDialogWindow
{
    private const double SwatchSize = 28;
    private const double SwatchRadius = 5;

    protected override bool ApplyDesignSizeOnOpen => false;

    private readonly string _contextBackgroundHex;
    private readonly ColorPickerContext? _pickerContext;
    private readonly IReadOnlyList<string>? _recentColors;
    private bool _suppressEvents;
    private bool _svDragging;
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;

    public string SelectedHex { get; private set; } = "#000000";

    public ThemeColorPickerDialog(Window owner, string initialHex)
        : this(owner, initialHex, null)
    {
    }

    public ThemeColorPickerDialog(Window owner, string initialHex, ColorPickerDialogOptions? options)
    {
        Owner = owner;
        InitializeComponent();

        _contextBackgroundHex = options?.ContextBackgroundHex
            ?? ThemeRuntime.Current.GetHex("BgBase");
        _pickerContext = options?.Context;
        _recentColors = options?.RecentColors;

        SvPlane.SizeChanged += (_, _) => UpdateSvThumb();
        Loaded += OnDialogLoaded;

        var color = ColorSpaceConverter.ParseColor(initialHex);
        SelectedHex = ColorSpaceConverter.ToHex(color);
        SetColorFromRgb(color, updatePickers: true);
    }

    private void OnDialogLoaded(object sender, RoutedEventArgs e)
    {
        UpdateSvThumb();
        BuildRecentSwatches(_recentColors);
        BuildHelpersPanel();
        BuildHarmonySwatches();
        BuildShadingGrid();
        ReapplyViewportLayout();
    }

    private void MoreTuningExpander_Expanded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(ReapplyViewportLayout, DispatcherPriority.Loaded);

    private void MoreTuningExpander_Collapsed(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(ReapplyViewportLayout, DispatcherPriority.Loaded);

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedHex = ColorSpaceConverter.ToHex(CurrentRgbColor());
        DialogResult = true;
        Close();
    }

    private void HueSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents)
            return;

        _hue = HueSlider.Value;
        UpdateHueLayer();
        ApplyHsvToUi();
    }

    private void RgbSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents)
            return;

        SyncRgbBoxesFromSliders();
        SetColorFromRgb(CurrentRgbColor(), updatePickers: false);
    }

    private void RgbBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || sender is not TextBox box)
            return;

        if (!byte.TryParse(box.Text, out var channel))
            return;

        _suppressEvents = true;
        try
        {
            switch (box.Tag as string)
            {
                case "R":
                    RedSlider.Value = channel;
                    break;
                case "G":
                    GreenSlider.Value = channel;
                    break;
                case "B":
                    BlueSlider.Value = channel;
                    break;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        SetColorFromRgb(CurrentRgbColor(), updatePickers: false);
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents)
            return;

        var text = HexBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (!ColorSpaceConverter.TryParseColor(text, out var color))
            return;

        SetColorFromRgb(color, updatePickers: true);
    }

    private void HslSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppressEvents)
            return;

        SyncHslBoxesFromSliders();
        ApplyHslFromControls();
    }

    private void HsvBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || sender is not TextBox box)
            return;

        if (!double.TryParse(box.Text, out var value))
            return;

        _suppressEvents = true;
        try
        {
            switch (box.Tag as string)
            {
                case "HsvH":
                    _hue = Math.Clamp(value, 0, 360);
                    HueSlider.Value = _hue;
                    break;
                case "HsvS":
                    _saturation = Math.Clamp(value, 0, 100) / 100.0;
                    break;
                case "HsvV":
                    _value = Math.Clamp(value, 0, 100) / 100.0;
                    break;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        ApplyHsvToUi();
    }

    private void HslBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEvents || sender is not TextBox box)
            return;

        if (!double.TryParse(box.Text, out var value))
            return;

        _suppressEvents = true;
        try
        {
            switch (box.Tag as string)
            {
                case "H":
                    HueHslSlider.Value = Math.Clamp(value, 0, 360);
                    break;
                case "S":
                    SaturationSlider.Value = Math.Clamp(value, 0, 100);
                    break;
                case "L":
                    LightnessSlider.Value = Math.Clamp(value, 0, 100);
                    break;
            }
        }
        finally
        {
            _suppressEvents = false;
        }

        ApplyHslFromControls();
    }

    private void ApplyHslFromControls()
    {
        var color = ColorSpaceConverter.HslToRgb(
            HueHslSlider.Value,
            SaturationSlider.Value / 100.0,
            LightnessSlider.Value / 100.0);
        SetColorFromRgb(color, updatePickers: true);
    }

    private void CopyFormatButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
            return;

        var text = button.Tag as string;
        var copyText = text switch
        {
            "rgb" => RgbFormatBox.Text,
            "hsl" => HslFormatBox.Text,
            "hsv" => HsvFormatBox.Text,
            "hex" => SelectedHex,
            _ => SelectedHex,
        };

        if (!string.IsNullOrWhiteSpace(copyText))
            Clipboard.SetText(copyText);
    }

    private void FixContrastButton_Click(object sender, RoutedEventArgs e) =>
        ApplyHelper("fix-contrast");

    private void ApplyHelper(string helperId)
    {
        var current = ColorSpaceConverter.ToHex(CurrentRgbColor());
        var result = ColorPickerHelperExecutor.Apply(helperId, _pickerContext, current);
        if (!ColorSpaceConverter.TryParseColor(result, out var color))
            return;

        SetColorFromRgb(color, updatePickers: true);
    }

    private void BuildHelpersPanel()
    {
        HelpersWrapPanel.Children.Clear();

        var hint = ColorPickerHelperCatalog.GetContextHint(_pickerContext);
        if (!string.IsNullOrWhiteSpace(hint))
        {
            HelpersHintText.Text = hint;
            HelpersHintText.Visibility = Visibility.Visible;
        }
        else
        {
            HelpersHintText.Visibility = Visibility.Collapsed;
        }

        var helpers = ColorPickerHelperCatalog.GetHelpers(_pickerContext);
        if (helpers.Count == 0)
        {
            HelpersSection.Visibility = Visibility.Collapsed;
            return;
        }

        HelpersSection.Visibility = Visibility.Visible;
        foreach (var helper in helpers)
        {
            if (string.Equals(helper.Id, "fix-contrast", StringComparison.Ordinal))
                continue;

            var button = new Button
            {
                Content = helper.Label,
                ToolTip = helper.Description,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 0, 8, 8),
                Style = TryFindResource("ShellCommandBarSecondarySlot") as Style,
            };
            var helperId = helper.Id;
            button.Click += (_, _) => ApplyHelper(helperId);
            HelpersWrapPanel.Children.Add(button);
        }
    }

    private void SvPlane_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _svDragging = true;
        SvPlane.CaptureMouse();
        UpdateSvFromPoint(e.GetPosition(SvPlane));
    }

    private void SvPlane_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_svDragging)
            return;

        UpdateSvFromPoint(e.GetPosition(SvPlane));
    }

    private void SvPlane_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _svDragging = false;
        SvPlane.ReleaseMouseCapture();
    }

    private void UpdateSvFromPoint(Point point)
    {
        EnsureSvPlaneMeasured();

        var width = SvPlane.ActualWidth;
        var height = SvPlane.ActualHeight;
        if (width <= 0 || height <= 0)
            return;

        _saturation = Math.Clamp(point.X / width, 0, 1);
        _value = Math.Clamp(1 - point.Y / height, 0, 1);
        ApplyHsvToUi();
    }

    private void SetColorFromRgb(Color color, bool updatePickers)
    {
        ColorSpaceConverter.RgbToHsv(color, out _hue, out _saturation, out _value);

        _suppressEvents = true;
        try
        {
            PreviewSwatch.Background = new SolidColorBrush(color);
            SelectedHex = ColorSpaceConverter.ToHex(color);
            HexBox.Text = SelectedHex.ToUpperInvariant();
            RgbFormatBox.Text = ColorSpaceConverter.FormatRgb(color);
            HslFormatBox.Text = ColorSpaceConverter.FormatHsl(color);
            HsvFormatBox.Text = ColorSpaceConverter.FormatHsv(color);
            UpdateNearestColorName(color);

            if (updatePickers)
            {
                HueSlider.Value = _hue;
                RedSlider.Value = color.R;
                GreenSlider.Value = color.G;
                BlueSlider.Value = color.B;
                SyncRgbBoxesFromSliders();
                SyncHsvBoxesFromState();

                ColorSpaceConverter.RgbToHsl(color, out var h, out var s, out var l);
                HueHslSlider.Value = h;
                SaturationSlider.Value = s * 100;
                LightnessSlider.Value = l * 100;
                SyncHslBoxesFromSliders();
            }

            UpdateHueLayer();
            UpdateSvThumb();
            UpdateContrastPreview(color);
            BuildHarmonySwatches();
            BuildShadingGrid();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplyHsvToUi()
    {
        var color = ColorSpaceConverter.HsvToRgb(_hue, _saturation, _value);
        SetColorFromRgb(color, updatePickers: true);
    }

    private void SyncRgbBoxesFromSliders()
    {
        RedBox.Text = ((int)RedSlider.Value).ToString();
        GreenBox.Text = ((int)GreenSlider.Value).ToString();
        BlueBox.Text = ((int)BlueSlider.Value).ToString();
    }

    private void SyncHslBoxesFromSliders()
    {
        HueHslBox.Text = ((int)HueHslSlider.Value).ToString();
        SaturationBox.Text = ((int)SaturationSlider.Value).ToString();
        LightnessBox.Text = ((int)LightnessSlider.Value).ToString();
    }

    private void SyncHsvBoxesFromState()
    {
        HueHsvBox.Text = ((int)Math.Round(_hue)).ToString();
        SaturationHsvBox.Text = ((int)Math.Round(_saturation * 100)).ToString();
        ValueHsvBox.Text = ((int)Math.Round(_value * 100)).ToString();
    }

    private Color CurrentRgbColor() =>
        Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);

    private void UpdateHueLayer()
    {
        var hueColor = ColorSpaceConverter.HsvToRgb(_hue, 1, 1);
        SvHueLayer.Background = new SolidColorBrush(hueColor);
    }

    private void UpdateSvThumb()
    {
        if (SvPlane.ActualWidth < 14 || SvPlane.ActualHeight < 14)
            return;

        const double thumbRadius = 7;
        var width = SvPlane.ActualWidth;
        var height = SvPlane.ActualHeight;
        var x = _saturation * width;
        var y = (1 - _value) * height;

        var maxLeft = Math.Max(0, width - thumbRadius * 2);
        var maxTop = Math.Max(0, height - thumbRadius * 2);

        Canvas.SetLeft(SvThumb, Math.Clamp(x - thumbRadius, 0, maxLeft));
        Canvas.SetTop(SvThumb, Math.Clamp(y - thumbRadius, 0, maxTop));
    }

    private void UpdateContrastPreview(Color foreground)
    {
        var foregroundHex = ColorSpaceConverter.ToHex(foreground);
        PreviewOnBackgroundBorder.Background = CreateBrush(_contextBackgroundHex);
        PreviewOnBackgroundText.Foreground = new SolidColorBrush(foreground);

        var ratio = ThemeContrast.ContrastRatio(foregroundHex, _contextBackgroundHex);
        var readable = ThemeContrast.IsReadable(foregroundHex, _contextBackgroundHex);
        ContrastRatioText.Text = readable
            ? $"Contrast {ratio:F1}:1 on background"
            : $"Low contrast {ratio:F1}:1 (needs {ThemeContrast.MinBodyRatio:F1}:1)";
        ContrastRatioText.Foreground = readable
            ? (Brush)FindResource("TextMutedBrush")
            : (Brush)FindResource("WarningBrush");
        ContrastPanel.BorderBrush = readable
            ? (Brush)FindResource("BorderSubtleBrush")
            : (Brush)FindResource("WarningBrush");
    }

    private void UpdateNearestColorName(Color color)
    {
        var name = ColorSpaceConverter.TryFindNearestNamedColor(color);
        if (string.IsNullOrWhiteSpace(name))
        {
            NearestColorNameText.Visibility = Visibility.Collapsed;
            NearestColorNameText.Text = string.Empty;
            return;
        }

        NearestColorNameText.Text = $"Nearest: {name}";
        NearestColorNameText.Visibility = Visibility.Visible;
    }

    private void BuildRecentSwatches(IReadOnlyList<string>? recentColors)
    {
        RecentColorsWrapPanel.Children.Clear();
        if (recentColors is null || recentColors.Count == 0)
        {
            RecentColorsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        RecentColorsPanel.Visibility = Visibility.Visible;
        foreach (var color in recentColors)
        {
            var swatch = CreatePaletteSwatch(color);
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                if (ColorSpaceConverter.TryParseColor(color, out var parsed))
                    SetColorFromRgb(parsed, updatePickers: true);
            };
            RecentColorsWrapPanel.Children.Add(swatch);
        }
    }

    private void BuildHarmonySwatches()
    {
        HarmonyAnalogousPanel.Children.Clear();
        HarmonyComplementPanel.Children.Clear();
        HarmonyTriadPanel.Children.Clear();

        var baseHex = ColorSpaceConverter.ToHex(CurrentRgbColor());
        AddHarmonySwatch(HarmonyAnalogousPanel, ColorSpaceConverter.RotateHue(baseHex, -30), "−30°");
        AddHarmonySwatch(HarmonyAnalogousPanel, baseHex, "Base");
        AddHarmonySwatch(HarmonyAnalogousPanel, ColorSpaceConverter.RotateHue(baseHex, 30), "+30°");
        AddHarmonySwatch(HarmonyComplementPanel, ColorSpaceConverter.RotateHue(baseHex, 180), "Complement");
        AddHarmonySwatch(HarmonyTriadPanel, baseHex, "0°");
        AddHarmonySwatch(HarmonyTriadPanel, ColorSpaceConverter.RotateHue(baseHex, 120), "+120°");
        AddHarmonySwatch(HarmonyTriadPanel, ColorSpaceConverter.RotateHue(baseHex, 240), "+240°");
    }

    private void AddHarmonySwatch(WrapPanel panel, string hex, string label)
    {
        var swatch = CreatePaletteSwatch(hex);
        swatch.ToolTip = $"{label}: {hex}";
        swatch.MouseLeftButtonUp += (_, _) =>
        {
            if (ColorSpaceConverter.TryParseColor(hex, out var color))
                SetColorFromRgb(color, updatePickers: true);
        };
        panel.Children.Add(swatch);
    }

    private void BuildShadingGrid()
    {
        ShadingGrid.Children.Clear();
        ColorSpaceConverter.RgbToHsl(CurrentRgbColor(), out var hue, out _, out _);

        for (var row = 0; row < 5; row++)
        {
            for (var col = 0; col < 5; col++)
            {
                var saturation = 0.2 + row * 0.2;
                var lightness = 0.15 + col * 0.175;
                var hex = ColorSpaceConverter.HslToHex(hue, saturation, lightness);
                var cell = CreatePaletteSwatch(hex, size: 24, margin: 2);
                cell.ToolTip = hex;
                cell.MouseLeftButtonUp += (_, _) =>
                {
                    if (ColorSpaceConverter.TryParseColor(hex, out var color))
                        SetColorFromRgb(color, updatePickers: true);
                };
                ShadingGrid.Children.Add(cell);
            }
        }
    }

    private Border CreatePaletteSwatch(string color, double size = SwatchSize, double margin = 8)
    {
        return new Border
        {
            Width = size,
            Height = size,
            Margin = new Thickness(0, 0, margin, margin),
            CornerRadius = new CornerRadius(SwatchRadius),
            Background = CreateBrush(color),
            BorderBrush = (Brush)FindResource("BorderStrongBrush"),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = color,
        };
    }

    private static SolidColorBrush CreateBrush(string hex)
    {
        if (!ColorSpaceConverter.TryParseColor(hex, out var color))
            return new SolidColorBrush(Colors.Gray);

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void EnsureSvPlaneMeasured()
    {
        if (SvPlane.ActualWidth > 0 && SvPlane.ActualHeight > 0)
            return;

        SvPlane.UpdateLayout();
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UpdateHueLayer();
        UpdateSvThumb();
        ReapplyViewportLayout();
    }
}

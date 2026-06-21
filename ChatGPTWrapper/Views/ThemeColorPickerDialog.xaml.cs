using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ChatGPTWrapper.Views;

public partial class ThemeColorPickerDialog : Window
{
    private bool _suppressEvents;
    private bool _svDragging;
    private double _hue;
    private double _saturation = 1;
    private double _value = 1;

    public string SelectedHex { get; private set; } = "#000000";

    public ThemeColorPickerDialog(Window owner, string initialHex)
    {
        Owner = owner;
        InitializeComponent();

        SvPlane.SizeChanged += (_, _) => UpdateSvThumb();
        Loaded += (_, _) => UpdateSvThumb();

        var color = ParseColor(initialHex);
        SelectedHex = ToHex(color);
        SetColorFromRgb(color, updatePickers: true);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedHex = ToHex(CurrentRgbColor());
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
        if (text.Length is not (6 or 7))
            return;

        if (!text.StartsWith('#'))
            text = "#" + text;

        if (!TryParseColor(text, out var color))
            return;

        SetColorFromRgb(color, updatePickers: true);
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
        RgbToHsv(color, out _hue, out _saturation, out _value);

        _suppressEvents = true;
        try
        {
            PreviewSwatch.Background = new SolidColorBrush(color);
            SelectedHex = ToHex(color);
            HexBox.Text = SelectedHex.ToUpperInvariant();

            if (updatePickers)
            {
                HueSlider.Value = _hue;
                RedSlider.Value = color.R;
                GreenSlider.Value = color.G;
                BlueSlider.Value = color.B;
                SyncRgbBoxesFromSliders();
            }

            UpdateHueLayer();
            UpdateSvThumb();
        }
        finally
        {
            _suppressEvents = false;
        }
    }

    private void ApplyHsvToUi()
    {
        var color = HsvToRgb(_hue, _saturation, _value);
        SetColorFromRgb(color, updatePickers: true);
    }

    private void SyncRgbBoxesFromSliders()
    {
        RedBox.Text = ((int)RedSlider.Value).ToString();
        GreenBox.Text = ((int)GreenSlider.Value).ToString();
        BlueBox.Text = ((int)BlueSlider.Value).ToString();
    }

    private Color CurrentRgbColor() =>
        Color.FromRgb((byte)RedSlider.Value, (byte)GreenSlider.Value, (byte)BlueSlider.Value);

    private void UpdateHueLayer()
    {
        var hueColor = HsvToRgb(_hue, 1, 1);
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
    }

    private static Color ParseColor(string hex) =>
        TryParseColor(hex, out var color) ? color : Colors.White;

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

    private static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static void RgbToHsv(Color color, out double h, out double s, out double v)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;

        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        v = max;
        s = max <= 0 ? 0 : delta / max;

        if (delta <= 0)
        {
            h = 0;
            return;
        }

        if (max == r)
            h = 60 * (((g - b) / delta) % 6);
        else if (max == g)
            h = 60 * (((b - r) / delta) + 2);
        else
            h = 60 * (((r - g) / delta) + 4);

        if (h < 0)
            h += 360;
    }

    private static Color HsvToRgb(double h, double s, double v)
    {
        if (s <= 0)
        {
            var gray = (byte)Math.Round(v * 255);
            return Color.FromRgb(gray, gray, gray);
        }

        h = (h % 360 + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;

        double r, g, b;
        if (h < 60)
            (r, g, b) = (c, x, 0);
        else if (h < 120)
            (r, g, b) = (x, c, 0);
        else if (h < 180)
            (r, g, b) = (0, c, x);
        else if (h < 240)
            (r, g, b) = (0, x, c);
        else if (h < 300)
            (r, g, b) = (x, 0, c);
        else
            (r, g, b) = (c, 0, x);

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}

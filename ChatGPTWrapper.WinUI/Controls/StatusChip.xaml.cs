using ChatGPTWrapper.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Controls;

public sealed partial class StatusChip : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusChip), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int?), typeof(StatusChip), new PropertyMetadata(null, OnCountChanged));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusChipKind), typeof(StatusChip), new PropertyMetadata(StatusChipKind.Neutral, OnKindChanged));

    public StatusChip()
    {
        InitializeComponent();
        UpdateCountVisibility();
        ApplyKindStyle();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public int? Count
    {
        get => (int?)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public StatusChipKind Kind
    {
        get => (StatusChipKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? Click;

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusChip chip)
            chip.LabelText.Text = chip.Label;
    }

    private static void OnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusChip chip)
            chip.UpdateCountVisibility();
    }

    private static void OnKindChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is StatusChip chip)
            chip.ApplyKindStyle();
    }

    private void UpdateCountVisibility()
    {
        if (Count is int count && count > 0)
        {
            CountBadge.Visibility = Visibility.Visible;
            CountText.Text = count.ToString();
            return;
        }

        CountBadge.Visibility = Visibility.Collapsed;
        CountText.Text = string.Empty;
    }

    private void ApplyKindStyle()
    {
        var resources = Application.Current.Resources;
        ChipButton.Background = Kind switch
        {
            StatusChipKind.Attention => (Brush)resources["WarningSubtleBrush"],
            StatusChipKind.Success => (Brush)resources["SuccessSubtleBrush"],
            StatusChipKind.Running => (Brush)resources["AccentSubtleBrush"],
            _ => (Brush)resources["BgElevatedBrush"],
        };
    }

    private void ChipButton_Click(object sender, RoutedEventArgs e) =>
        Click?.Invoke(this, e);
}

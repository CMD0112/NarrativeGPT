using System.Windows;
using System.Windows.Controls;

namespace ChatGPTWrapper.Controls;

public partial class StatusChip : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusChip), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int?), typeof(StatusChip), new PropertyMetadata(null, OnCountChanged));

    public static readonly DependencyProperty KindProperty =
        DependencyProperty.Register(nameof(Kind), typeof(StatusChipKind), typeof(StatusChip), new PropertyMetadata(StatusChipKind.Neutral, OnKindChanged));

    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(StatusChip));

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

    public event RoutedEventHandler Click
    {
        add => AddHandler(ClickEvent, value);
        remove => RemoveHandler(ClickEvent, value);
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
        ChipButton.Background = Kind switch
        {
            StatusChipKind.Attention => (System.Windows.Media.Brush)FindResource("WarningSubtleBrush"),
            StatusChipKind.Success => (System.Windows.Media.Brush)FindResource("SuccessSubtleBrush"),
            StatusChipKind.Running => (System.Windows.Media.Brush)FindResource("AccentSubtleBrush"),
            _ => (System.Windows.Media.Brush)FindResource("BgElevatedBrush"),
        };
    }

    private void ChipButton_Click(object sender, RoutedEventArgs e) =>
        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
}

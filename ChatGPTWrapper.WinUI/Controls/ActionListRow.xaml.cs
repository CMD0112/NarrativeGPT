using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Controls;

public sealed partial class ActionListRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(ActionListRow), new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(ActionListRow), new PropertyMetadata(string.Empty, OnHintChanged));

    public static readonly DependencyProperty RowEnabledProperty =
        DependencyProperty.Register(nameof(RowEnabled), typeof(bool), typeof(ActionListRow), new PropertyMetadata(true, OnEnabledChanged));

    public static readonly DependencyProperty ActionLabelProperty =
        DependencyProperty.Register(nameof(ActionLabel), typeof(string), typeof(ActionListRow), new PropertyMetadata("Open…", OnActionLabelChanged));

    public static readonly DependencyProperty ChromelessProperty =
        DependencyProperty.Register(nameof(Chromeless), typeof(bool), typeof(ActionListRow), new PropertyMetadata(false, OnChromelessChanged));

    public event EventHandler? RunRequested;

    public ActionListRow()
    {
        InitializeComponent();
        RunButton.Content = ActionLabel;
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Hint
    {
        get => (string)GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    public bool RowEnabled
    {
        get => (bool)GetValue(RowEnabledProperty);
        set => SetValue(RowEnabledProperty, value);
    }

    public string ActionLabel
    {
        get => (string)GetValue(ActionLabelProperty);
        set => SetValue(ActionLabelProperty, value);
    }

    public bool Chromeless
    {
        get => (bool)GetValue(ChromelessProperty);
        set => SetValue(ChromelessProperty, value);
    }

    private static void OnChromelessChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActionListRow row)
            row.ApplyChromeless(row.Chromeless);
    }

    private void ApplyChromeless(bool chromeless)
    {
        if (chromeless)
        {
            ChromeBorder.Background = null;
            ChromeBorder.BorderThickness = new Thickness(0);
            ChromeBorder.Padding = new Thickness(0);
            ChromeBorder.Margin = new Thickness(0);
        }
        else
        {
            ChromeBorder.Background = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BgSurfaceBrush"];
            ChromeBorder.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["BorderSubtleBrush"];
            ChromeBorder.BorderThickness = new Thickness(1);
            ChromeBorder.Padding = new Thickness(10, 8, 10, 8);
            ChromeBorder.Margin = new Thickness(0, 0, 0, 6);
        }
    }

    private static void OnActionLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActionListRow row)
            row.RunButton.Content = row.ActionLabel;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActionListRow row)
            row.TitleText.Text = row.Title;
    }

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActionListRow row)
            row.HintText.Text = row.Hint;
    }

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ActionListRow row)
            row.RunButton.IsEnabled = row.RowEnabled;
    }

    private void RunButton_Click(object sender, RoutedEventArgs e) =>
        RunRequested?.Invoke(this, EventArgs.Empty);
}

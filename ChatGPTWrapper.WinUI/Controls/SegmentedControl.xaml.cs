using ChatGPTWrapper.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace ChatGPTWrapper.WinUI.Controls;

public sealed partial class SegmentedControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IList<object>),
            typeof(SegmentedControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(SegmentedControl),
            new PropertyMetadata(-1, OnSelectedIndexChanged));

    public static readonly DependencyProperty SelectedTagProperty =
        DependencyProperty.Register(
            nameof(SelectedTag),
            typeof(object),
            typeof(SegmentedControl),
            new PropertyMetadata(null));

    private readonly List<Button> _segmentButtons = [];
    private bool _suppressSelectionEvents;

    public SegmentedControl()
    {
        InitializeComponent();
    }

    public IList<object>? ItemsSource
    {
        get => (IList<object>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedTag
    {
        get => GetValue(SelectedTagProperty);
        set => SetValue(SelectedTagProperty, value);
    }

    public event EventHandler? SelectionChanged;

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SegmentedControl control)
            control.RebuildSegments();
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SegmentedControl control)
            control.ApplySelectedIndex();
    }

    private void RebuildSegments()
    {
        SegmentPanel.Children.Clear();
        _segmentButtons.Clear();

        var items = ItemsSource ?? [];
        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var sourceItem = items[i];
            var (content, tag, enabled) = ResolveItem(sourceItem);
            var button = new Button
            {
                Content = content,
                Tag = tag,
                IsEnabled = enabled,
                Padding = TryGetSegmentPadding(),
                MinHeight = (double)Application.Current.Resources["ControlMinHeight"],
            };
            button.Click += (_, _) =>
            {
                if (_suppressSelectionEvents)
                    return;

                SelectedIndex = index;
                SelectedTag = tag;
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            };
            _segmentButtons.Add(button);
            SegmentPanel.Children.Add(button);
        }

        ApplySelectedIndex();

        if (SelectedIndex < 0 && _segmentButtons.Count > 0)
            SelectedIndex = 0;
    }

    private static (string content, object? tag, bool enabled) ResolveItem(object item) =>
        item switch
        {
            SegmentedItemModel segmented => (segmented.Content, segmented.Tag ?? segmented.Content, segmented.IsEnabled),
            string text => (text, text, true),
            _ => (item.ToString() ?? string.Empty, item, true),
        };

    private static Thickness TryGetSegmentPadding() =>
        Application.Current.Resources.TryGetValue("SegmentButtonPadding", out var value) && value is Thickness padding
            ? padding
            : new Thickness(14, 6, 14, 6);

    public void RefreshVisuals() => ApplySelectedIndex();

    private void ApplySelectedIndex()
    {
        if (_segmentButtons.Count == 0)
            return;

        _suppressSelectionEvents = true;
        try
        {
            var selectedBrush = (Brush)Application.Current.Resources["AccentPrimaryBrush"];
            var normalBrush = (Brush)Application.Current.Resources["BgSurfaceBrush"];

            for (var i = 0; i < _segmentButtons.Count; i++)
            {
                _segmentButtons[i].Background = i == SelectedIndex ? selectedBrush : normalBrush;
                if (i == SelectedIndex)
                    SelectedTag = _segmentButtons[i].Tag;
            }
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }
}

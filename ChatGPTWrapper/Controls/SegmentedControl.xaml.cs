using System.Collections;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ChatGPTWrapper.Controls;

public partial class SegmentedControl : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable),
            typeof(SegmentedControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(SegmentedControl),
            new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedIndexChanged));

    public static readonly DependencyProperty SelectedTagProperty =
        DependencyProperty.Register(
            nameof(SelectedTag),
            typeof(object),
            typeof(SegmentedControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private readonly List<Button> _segmentButtons = [];
    private bool _suppressSelectionEvents;

    public SegmentedControl()
    {
        InitializeComponent();
    }

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
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

    public event RoutedEventHandler? SelectionChanged;

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

        var items = ItemsSource?.Cast<object>().ToList() ?? [];
        var normal = (Style)FindResource("ModeButtonStyle");
        var selected = (Style)FindResource("ModeButtonSelectedStyle");

        for (var i = 0; i < items.Count; i++)
        {
            var index = i;
            var sourceItem = items[i];
            var (content, tag) = ResolveItem(sourceItem);
            var button = new Button
            {
                Content = content,
                Tag = tag,
                Style = normal,
                IsEnabled = sourceItem is SegmentedItem segmentedItem ? segmentedItem.IsEnabled : true,
            };
            button.Click += (_, _) =>
            {
                if (_suppressSelectionEvents)
                    return;

                SelectedIndex = index;
                SelectedTag = tag;
                SelectionChanged?.Invoke(this, new RoutedEventArgs());
            };
            _segmentButtons.Add(button);
            SegmentPanel.Children.Add(button);
        }

        ApplySelectedIndex();
    }

    private static (string content, object? tag) ResolveItem(object item) =>
        item switch
        {
            SegmentedItem segmented => (segmented.Content, segmented.Tag ?? segmented.Content),
            string text => (text, text),
            _ => (item.ToString() ?? string.Empty, item),
        };

    private void ApplySelectedIndex()
    {
        if (_segmentButtons.Count == 0)
            return;

        _suppressSelectionEvents = true;
        try
        {
            var normal = (Style)FindResource("ModeButtonStyle");
            var selected = (Style)FindResource("ModeButtonSelectedStyle");

            for (var i = 0; i < _segmentButtons.Count; i++)
                _segmentButtons[i].Style = i == SelectedIndex ? selected : normal;

            if (SelectedIndex >= 0 && SelectedIndex < _segmentButtons.Count)
                SelectedTag = _segmentButtons[SelectedIndex].Tag;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }
    }
}

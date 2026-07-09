using ChatGPTWrapper.WinUI.Theming;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ChatGPTWrapper.WinUI.Controls;

public sealed partial class ScopeBadgeView : UserControl
{
    public static readonly DependencyProperty ScopeLabelProperty =
        DependencyProperty.Register(
            nameof(ScopeLabel),
            typeof(string),
            typeof(ScopeBadgeView),
            new PropertyMetadata("", OnScopeLabelChanged));

    public ScopeBadgeView()
    {
        InitializeComponent();
    }

    public string ScopeLabel
    {
        get => (string)GetValue(ScopeLabelProperty);
        set => SetValue(ScopeLabelProperty, value);
    }

    private static void OnScopeLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScopeBadgeView view)
            ScopeBadgePalette.Apply(view.BadgeBorder, view.BadgeText, e.NewValue as string);
    }
}

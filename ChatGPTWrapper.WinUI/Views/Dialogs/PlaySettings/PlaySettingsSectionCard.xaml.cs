using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace ChatGPTWrapper.WinUI.Views.Dialogs.PlaySettings;

[ContentProperty(Name = nameof(Body))]
public sealed partial class PlaySettingsSectionCard : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(PlaySettingsSectionCard),
            new PropertyMetadata("", OnTitleChanged));

    public static readonly DependencyProperty HintProperty =
        DependencyProperty.Register(nameof(Hint), typeof(string), typeof(PlaySettingsSectionCard),
            new PropertyMetadata("", OnHintChanged));

    public static readonly DependencyProperty ScopeLabelProperty =
        DependencyProperty.Register(nameof(ScopeLabel), typeof(string), typeof(PlaySettingsSectionCard),
            new PropertyMetadata(""));

    public PlaySettingsSectionCard()
    {
        InitializeComponent();
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

    public string ScopeLabel
    {
        get => (string)GetValue(ScopeLabelProperty);
        set => SetValue(ScopeLabelProperty, value);
    }

    public UIElement? Body
    {
        get => BodyHost.Content as UIElement;
        set => BodyHost.Content = value;
    }

    private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlaySettingsSectionCard card)
            card.TitleBlock.Text = e.NewValue as string ?? "";
    }

    private static void OnHintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlaySettingsSectionCard card)
        {
            var text = e.NewValue as string ?? "";
            card.HintBlock.Text = text;
            card.HintBlock.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
